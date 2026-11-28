using Microsoft.Extensions.DependencyModel;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

#if NETCOREAPP3_1_OR_GREATER
using System.Runtime.Loader;
#endif

namespace Orleans.Serialization.Internal
{
    /// <summary>
    /// 发现包含 Orleans 序列化应用程序部件的程序集。
    /// </summary>
    public static class ReferencedAssemblyProvider
    {
        /// <summary>
        /// 获取已加载且包含 Orleans 序列化应用程序部件的程序集。
        /// </summary>
        /// <returns>包含 Orleans 序列化应用程序部件的程序集。</returns>
        public static IEnumerable<Assembly> GetRelevantAssemblies()
        {
            var parts = new HashSet<Assembly>();
            var entryAssembly = Assembly.GetEntryAssembly();

            if (entryAssembly is not null)
            {
                AddAssembly(parts, entryAssembly);
            }

            if (AssemblyFilesAvailable)
            {
                AddFromDependencyContext(parts, entryAssembly);
            }

#if NETCOREAPP3_1_OR_GREATER
            AddFromAssemblyLoadContext(parts);
#endif

            foreach (var loadedAsm in AppDomain.CurrentDomain.GetAssemblies())
            {
                AddAssembly(parts, loadedAsm);
            }

            return parts;
        }

        /// <summary>
        /// 将程序集及其引用的 Orleans 序列化应用程序部件添加到集合。
        /// </summary>
        /// <param name="parts">要向其中添加程序集的集合。</param>
        /// <param name="assembly">要检查的程序集。</param>
        /// <exception cref="ArgumentNullException"><paramref name="parts"/> 或 <paramref name="assembly"/> 为 <see langword="null"/>。</exception>
        public static void AddAssembly(HashSet<Assembly> parts, Assembly assembly)
        {
            if (parts is null) throw new ArgumentNullException(nameof(parts));
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (!assembly.IsDefined(typeof(ApplicationPartAttribute)))
            {
                return;
            }

            if (!parts.Add(assembly))
            {
                return;
            }

            // 添加所有被引用的应用程序部件。
            foreach (var referencedAsm in GetApplicationPartAssemblies(assembly))
            {
                AddAssembly(parts, referencedAsm);
            }
        }

#if NETCOREAPP3_1_OR_GREATER
        /// <summary>
        /// 将程序集加载上下文中的 Orleans 序列化应用程序部件添加到集合。
        /// </summary>
        /// <param name="parts">要向其中添加程序集的集合。</param>
        /// <param name="context">要检查的程序集加载上下文。</param>
        public static void AddFromAssemblyLoadContext(HashSet<Assembly> parts, AssemblyLoadContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            foreach (var asm in context.Assemblies)
            {
                AddAssembly(parts, asm);
            }
        }

        /// <summary>
        /// 将包含指定程序集的加载上下文中的 Orleans 序列化应用程序部件添加到集合。
        /// </summary>
        /// <param name="parts">要向其中添加程序集的集合。</param>
        /// <param name="assembly">
        /// 要检查其加载上下文的程序集；指定 <see langword="null"/> 时使用包含此类型的程序集。
        /// </param>
        public static void AddFromAssemblyLoadContext(HashSet<Assembly> parts, Assembly? assembly = null)
        {
            assembly ??= typeof(ReferencedAssemblyProvider).Assembly;
            var assemblies = new HashSet<Assembly>();
            var context = AssemblyLoadContext.GetLoadContext(assembly)!;
            foreach (var asm in context.Assemblies)
            {
                // 跳过未执行过代码生成的程序集以及已经遇到的程序集。
                if (!asm.IsDefined(typeof(ApplicationPartAttribute)) || !assemblies.Add(asm))
                {
                    continue;
                }

                AddAssembly(parts, asm);
            }
        }
#endif

        /// <summary>
        /// 将程序集依赖项上下文中的 Orleans 序列化应用程序部件添加到集合。
        /// </summary>
        /// <param name="parts">要向其中添加程序集的集合。</param>
        /// <param name="assembly">
        /// 要检查其依赖项上下文的程序集；指定 <see langword="null"/> 时使用入口程序集。
        /// </param>
        [RequiresAssemblyFiles("Dependency-context discovery reads assembly files. Use " + nameof(GetRelevantAssemblies) + " for single-file-compatible discovery.")]
        public static void AddFromDependencyContext(HashSet<Assembly> parts, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetEntryAssembly();
            DependencyContext? dependencyContext;
            if (assembly is null || assembly.IsDynamic)
            {
                dependencyContext = DependencyContext.Default;
            }
            else
            {
                dependencyContext = DependencyContext.Load(assembly);
            }

            var assemblies = new HashSet<Assembly>();
            if (assembly != null && assembly.IsDefined(typeof(ApplicationPartAttribute)))
            {
                AddAssembly(parts, assembly);
                assemblies.Add(assembly);
            }

            if (dependencyContext == null)
            {
                return;
            }

#if NETCOREAPP3_1_OR_GREATER
            var assemblyContext = assembly is not null
                ? AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default
                : AssemblyLoadContext.Default;
#endif

            foreach (var lib in dependencyContext.RuntimeLibraries)
            {
                if (!lib.Name.Contains("Orleans.Serialization", StringComparison.Ordinal) && !lib.Dependencies.Any(dep => dep.Name.Contains("Orleans.Serialization", StringComparison.Ordinal)))
                {
                    continue;
                }

                try
                {
#if NET5_0_OR_GREATER
                    var name = lib.GetRuntimeAssemblyNames(dependencyContext, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier).FirstOrDefault();
                    if (name is null)
                    {
                        continue;
                    }

                    var asm = assemblyContext.LoadFromAssemblyName(name);
#else
                    var name = lib.GetRuntimeAssemblyNames(dependencyContext, Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.GetRuntimeIdentifier()).FirstOrDefault();
                    if (name is null)
                    {
                        continue;
                    }

#if NETCOREAPP3_1_OR_GREATER
                    var asm = assemblyContext.LoadFromAssemblyName(name);
#else
                    var asm = Assembly.Load(name);
#endif
#endif
                    if (asm.IsDefined(typeof(ApplicationPartAttribute)) && assemblies.Add(asm))
                    {
                        AddAssembly(parts, asm);
                    }
                }
                catch
                {
                    // 忽略非显式程序集加载过程中抛出的所有异常。
                }
            }
        }

#if NET9_0_OR_GREATER
        [FeatureGuard(typeof(RequiresAssemblyFilesAttribute))]
#endif
        internal static bool AssemblyFilesAvailable => AreAssemblyFilesAvailable(Assembly.GetEntryAssembly());

#if NET5_0_OR_GREATER
        [UnconditionalSuppressMessage(
            "SingleFile",
            "IL3000",
            Justification = "Assembly.Location is used only as the documented availability check for bundled assembly files.")]
#endif
        internal static bool AreAssemblyFilesAvailable(Assembly? entryAssembly)
        {
            var assembly = entryAssembly is null || entryAssembly.IsDynamic
                ? typeof(ReferencedAssemblyProvider).Assembly
                : entryAssembly;
            return File.Exists(assembly.Location);
        }

        private static IEnumerable<Assembly> GetApplicationPartAssemblies(Assembly assembly)
        {
            if (!assembly.IsDefined(typeof(ApplicationPartAttribute)))
            {
                return Array.Empty<Assembly>();
            }

#if NETCOREAPP3_1_OR_GREATER
            var assemblyContext = AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;
#endif

            return ExpandApplicationParts(
                new[] { assembly }.Concat(assembly.GetCustomAttributes<ApplicationPartAttribute>()
                    .Select(name =>
#if NETCOREAPP3_1_OR_GREATER
                        assemblyContext.LoadFromAssemblyName(new AssemblyName(name.AssemblyName))
#else
                        Assembly.Load(new AssemblyName(name.AssemblyName))
#endif
                    )));

            static IEnumerable<Assembly> ExpandApplicationParts(IEnumerable<Assembly> assemblies)
            {
                if (assemblies == null)
                {
                    throw new ArgumentNullException(nameof(assemblies));
                }

                var relatedAssemblies = new HashSet<Assembly>();
                foreach (var assembly in assemblies)
                {
                    if (relatedAssemblies.Add(assembly))
                    {
                        ExpandAssembly(relatedAssemblies, assembly);
                    }
                }

                return relatedAssemblies.OrderBy(assembly => assembly.FullName, StringComparer.Ordinal);

                static void ExpandAssembly(HashSet<Assembly> assemblies, Assembly assembly)
                {
                    var attributes = assembly.GetCustomAttributes<ApplicationPartAttribute>().ToArray();
                    if (attributes.Length == 0)
                    {
                        return;
                    }

#if NETCOREAPP3_1_OR_GREATER
                    var assemblyContext = AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;
#endif

                    foreach (var attribute in attributes)
                    {
                        var assemblyName = new AssemblyName(attribute.AssemblyName);
#if NETCOREAPP3_1_OR_GREATER
                        var referenced = assemblyContext.LoadFromAssemblyName(assemblyName);
#else
                        var referenced = Assembly.Load(assemblyName);
#endif
                        if (assemblies.Add(referenced))
                        {
                            ExpandAssembly(assemblies, referenced);
                        }
                    }
                }
            }
        }
    }
}
