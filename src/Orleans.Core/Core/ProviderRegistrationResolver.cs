using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Internal;

namespace Orleans
{
    /// <summary>
    /// 解析由配置驱动的提供程序注册。
    /// </summary>
    /// <remarks>
    /// 首先从 <see cref="ProviderMetadataRegistry"/> 读取注册信息。如果缺少所请求的提供程序，
    /// 由旧版代码生成器生成的程序集会被扫描一次，并将其 <see cref="RegisterProviderAttribute"/>
    /// 注册用作兼容性回退。只有实现 <see cref="IProviderMetadataProvider"/>
    /// 且标记为由 <c>OrleansCodeGen</c> 生成的提供程序，才会在回退发现过程中被激活；其他
    /// 由 <see cref="TypeManifestProviderAttribute"/> 标记的提供程序绝不会被实例化。
    /// </remarks>
    internal sealed class ProviderRegistrationResolver
    {
        private readonly Assembly _registryAssembly;
        private readonly Lazy<Dictionary<(string Target, string Kind, string Name), Type>> _legacyProviders;

        internal static ProviderRegistrationResolver Default { get; } = new(
            typeof(ProviderRegistrationResolver).Assembly,
            ReferencedAssemblyProvider.GetRelevantAssemblies);

        internal ProviderRegistrationResolver(Assembly registryAssembly, Func<IEnumerable<Assembly>> legacyAssemblyProvider)
        {
            _registryAssembly = registryAssembly ?? throw new ArgumentNullException(nameof(registryAssembly));
            if (legacyAssemblyProvider is null)
            {
                throw new ArgumentNullException(nameof(legacyAssemblyProvider));
            }

            _legacyProviders = new Lazy<Dictionary<(string Target, string Kind, string Name), Type>>(
                () => ScanAssemblies(legacyAssemblyProvider()),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal Dictionary<(string Kind, string Name), Type> GetRegisteredProviders(string target)
            => FilterProviders(ProviderMetadataRegistry.GetRegisteredProviders(_registryAssembly), target);

        internal bool TryGetRegisteredProvider(
            Dictionary<(string Kind, string Name), Type> knownProviders,
            string target,
            string kind,
            string name,
            out Type type)
        {
            if (knownProviders.TryGetValue((kind, name), out type!))
            {
                return true;
            }

            MergeProviders(knownProviders, ProviderMetadataRegistry.GetRegisteredProviders(_registryAssembly), target, overwrite: true);
            if (knownProviders.TryGetValue((kind, name), out type!))
            {
                return true;
            }

            MergeProviders(knownProviders, _legacyProviders.Value, target, overwrite: false);
            return knownProviders.TryGetValue((kind, name), out type!);
        }

        /// <summary>
        /// 获取为指定 <paramref name="target"/> 注册的提供程序，以种类和名称为键。
        /// </summary>
        /// <param name="assemblies">要扫描其提供程序注册的程序集。</param>
        /// <param name="target">用于筛选的注册目标，例如 <c>"Client"</c> 或 <c>"Silo"</c>。</param>
        public static Dictionary<(string Kind, string Name), Type> GetRegisteredProviders(IEnumerable<Assembly> assemblies, string target)
        {
            if (assemblies is null)
            {
                throw new ArgumentNullException(nameof(assemblies));
            }

            return FilterProviders(ScanAssemblies(assemblies), target);
        }

        private static Dictionary<(string Target, string Kind, string Name), Type> ScanAssemblies(IEnumerable<Assembly> assemblies)
        {
            var registeredProviders = new Dictionary<(string Target, string Kind, string Name), Type>();

            foreach (var asm in assemblies)
            {
                var hasGeneratedProviderMetadata = false;
                var attrs = asm.GetCustomAttributes<TypeManifestProviderAttribute>();
                foreach (var attr in attrs)
                {
                    if (typeof(IProviderMetadataProvider).IsAssignableFrom(attr.ProviderType)
                        && attr.ProviderType.GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>() is { Tool: "OrleansCodeGen" }
                        && Activator.CreateInstance(attr.ProviderType) is IProviderMetadataProvider provider)
                    {
                        provider.ConfigureProviders(registeredProviders);
                        hasGeneratedProviderMetadata = true;
                    }
                }

                if (!hasGeneratedProviderMetadata && asm.IsDefined(typeof(RegisterProviderAttribute)))
                {
                    foreach (var attr in asm.GetCustomAttributes<RegisterProviderAttribute>())
                    {
                        registeredProviders[(attr.Target, attr.Kind, attr.Name)] = attr.Type;
                    }
                }
            }

            return registeredProviders;
        }

        private static Dictionary<(string Kind, string Name), Type> FilterProviders(
            Dictionary<(string Target, string Kind, string Name), Type> providers,
            string target)
        {
            var result = new Dictionary<(string Kind, string Name), Type>();
            MergeProviders(result, providers, target, overwrite: true);
            return result;
        }

        private static void MergeProviders(
            Dictionary<(string Kind, string Name), Type> destination,
            Dictionary<(string Target, string Kind, string Name), Type> source,
            string target,
            bool overwrite)
        {
            foreach (var registration in source)
            {
                if (!string.Equals(registration.Key.Target, target, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = (registration.Key.Kind, registration.Key.Name);
                if (overwrite)
                {
                    destination[key] = registration.Value;
                }
                else
                {
                    destination.TryAdd(key, registration.Value);
                }
            }
        }
    }
}
