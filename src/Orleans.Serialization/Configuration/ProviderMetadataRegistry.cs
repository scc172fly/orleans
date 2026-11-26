using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
#if NETCOREAPP3_1_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
#endif

namespace Orleans.Serialization.Configuration
{
    /// <summary>
    /// 由 Orleans 源产生器排放的储存配置驱动提供者元数据。
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ProviderMetadataRegistry
    {
        private static readonly object SyncRoot = new();
#if NETCOREAPP3_1_OR_GREATER
        private static readonly ConditionalWeakTable<AssemblyLoadContext, Registry> Registries = new();
#else
        private static readonly Registry SharedRegistry = new();
#endif

        /// <summary>
        /// 登记册生成供应商元数据。
        /// </summary>
        /// <param name="provider">生成的提供者元数据。</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Register(IProviderMetadataProvider provider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            var registrations = new Dictionary<(string Target, string Kind, string Name), Type>();
            provider.ConfigureProviders(registrations);

            var providerType = provider.GetType();
            var providerIdentity = providerType.AssemblyQualifiedName ?? providerType.FullName ?? providerType.Name;
            lock (SyncRoot)
            {
#if NETCOREAPP3_1_OR_GREATER
                var loadContext = AssemblyLoadContext.GetLoadContext(providerType.Assembly) ?? AssemblyLoadContext.Default;
                Registries.GetValue(loadContext, static context =>
                {
                    context.Unloading += OnLoadContextUnloading;
                    return new Registry();
                }).Set(providerIdentity, registrations);
#else
                SharedRegistry.Set(providerIdentity, registrations);
#endif
            }
        }

        internal static Dictionary<(string Target, string Kind, string Name), Type> GetRegisteredProviders(Assembly assembly)
        {
            if (assembly is null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            var result = new Dictionary<(string Target, string Kind, string Name), Type>();
            lock (SyncRoot)
            {
#if NETCOREAPP3_1_OR_GREATER
                var loadContext = AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;
                if (Registries.TryGetValue(loadContext, out var registry))
                {
                    registry.CopyTo(result);
                }
#else
                SharedRegistry.CopyTo(result);
#endif
            }

            return result;
        }

#if NETCOREAPP3_1_OR_GREATER
        private static void OnLoadContextUnloading(AssemblyLoadContext loadContext)
        {
            lock (SyncRoot)
            {
                Registries.Remove(loadContext);
            }
        }
#endif

        private sealed class Registry
        {
            private readonly SortedDictionary<string, Dictionary<(string Target, string Kind, string Name), Type>> _registrations = new(StringComparer.Ordinal);

            public void Set(
                string providerIdentity,
                Dictionary<(string Target, string Kind, string Name), Type> registrations)
                => _registrations[providerIdentity] = registrations;

            public void CopyTo(Dictionary<(string Target, string Kind, string Name), Type> destination)
            {
                foreach (var registrations in _registrations.Values)
                {
                    foreach (var registration in registrations)
                    {
                        destination[registration.Key] = registration.Value;
                    }
                }
            }
        }
    }
}
