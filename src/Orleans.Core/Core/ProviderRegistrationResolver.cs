using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Internal;

namespace Orleans
{
    /// <summary>
    /// 确定配置驱动的供应商登记。
    /// </summary>
    /// <remarks>
    /// 登记首先从 <see cref="ProviderMetadataRegistry"/>读取。如果被请求的提供者缺席,则
    /// 由旧代码发电机制作的组件经过扫描一次, <see cref="RegisterProviderAttribute"/>
    /// 登记被用作兼容性后退。只有实施 <see cref="IProviderMetadataProvider"/>的供应商才使用该软件。
    /// 以 <c>OrleansCodeGen</c> 所生成的标记在后继发现时被激活;其他;
    /// <see cref="TypeManifestProviderAttribute"/> 提供商从不即时化。
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
        /// 获取指定 <paramref name="target"/>的注册服务提供方,按类型和名称键入。
        /// </summary>
        /// <param name="assemblies">供应商注册扫描的组件。</param>
        /// <param name="target">例如,要过滤的登记目标 <c>"Client"</c> 或 <c>"Silo"</c>.</param>
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
