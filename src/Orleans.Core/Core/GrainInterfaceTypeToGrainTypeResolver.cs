using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Utilities;

namespace Orleans
{
    /// <summary>
    /// 将 <see cref="GrainInterfaceType"/> 与兼容的 <see cref="GrainType"/> 关联。
    /// </summary>
    /// <remarks>
    /// 主要供最终用户调用 <see cref="IGrainFactory"/> 方法时使用，使其无须过多地显式指定类型。
    /// </remarks>
    public class GrainInterfaceTypeToGrainTypeResolver
    {
        private readonly Lock _lockObj = new();
        private readonly ConcurrentDictionary<GrainInterfaceType, GrainType> _genericMapping = new ConcurrentDictionary<GrainInterfaceType, GrainType>();
        private readonly IClusterManifestProvider _clusterManifestProvider;
        private Cache? _cache;

        /// <summary>
        /// 初始化 <see cref="GrainInterfaceTypeToGrainTypeResolver"/> 类的新实例。
        /// </summary>
        /// <param name="clusterManifestProvider">集群清单提供程序。</param>
        public GrainInterfaceTypeToGrainTypeResolver(IClusterManifestProvider clusterManifestProvider)
        {
            _clusterManifestProvider = clusterManifestProvider;
        }

        /// <summary>
        /// 返回一个 <see cref="GrainType"/>，它支持所提供的 <see cref="GrainInterfaceType"/>，且实现类型名称以所提供的前缀字符串开头。
        /// </summary>
        public GrainType GetGrainType(GrainInterfaceType interfaceType, string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return GetGrainType(interfaceType);
            }

            GrainType result = default;

            GrainInterfaceType lookupType;
            if (GenericGrainInterfaceType.TryParse(interfaceType, out var genericInterface))
            {
                lookupType = genericInterface.GetGenericGrainType().Value;
            }
            else
            {
                lookupType = interfaceType;
            }

            var cache = GetCache();
            if (cache.Map.TryGetValue(lookupType, out var entry))
            {
                var hasCandidate = false;
                foreach (var impl in entry.Implementations)
                {
                    if (impl.Prefix.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        if (impl.Prefix.Length == prefix.Length)
                        {
                            // 完全匹配优先。
                            result = impl.GrainType;
                            break;
                        }

                        if (hasCandidate)
                        {
                            var candidates = string.Join(", ", entry.Implementations.Select(i => $"{i.GrainType} ({i.Prefix})"));
                            throw new ArgumentException($"Unable to identify a single appropriate grain type for interface {interfaceType} with implementation prefix \"{prefix}\". Candidates: {candidates}");
                        }

                        result = impl.GrainType;
                        hasCandidate = true;
                    }
                }
            }

            if (result.IsDefault)
            {
                throw new ArgumentException($"Could not find an implementation matching prefix \"{prefix}\" for interface {interfaceType}");
            }

            if (GenericGrainType.TryParse(result, out var genericGrainType) && !genericGrainType.IsConstructed)
            {
                result = genericGrainType.GetConstructed(genericInterface);
            }

            return result;
        }

        /// <summary>
        /// 返回实现所提供接口类型的 <see cref="GrainType"/>，接口类型为 <see cref="GrainInterfaceType"/>。
        /// </summary>
        public GrainType GetGrainType(GrainInterfaceType interfaceType)
        {
            if (!TryGetGrainType(interfaceType, out var result))
            {
                throw new ArgumentException($"Could not find an implementation for interface {interfaceType}");
            }

            return result;
        }

        /// <summary>
        /// 解析实现所提供接口类型的 <see cref="GrainType"/>，接口类型为 <see cref="GrainInterfaceType"/>；如果找到实现则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <returns><see langword="true"/> 表示已找到实现，否则返回 <see langword="false"/>。</returns>
        public bool TryGetGrainType(GrainInterfaceType interfaceType, out GrainType result)
        {
            result = default;
            var cache = GetCache();
            if (cache.Map.TryGetValue(interfaceType, out var entry))
            {
                if (!entry.PrimaryImplementation.IsDefault)
                {
                    result = entry.PrimaryImplementation;
                }
                else if (entry.Implementations.Count == 1)
                {
                    result = entry.Implementations[0].GrainType;
                }
                else if (entry.Implementations.Count > 1)
                {
                    var candidates = string.Join(", ", entry.Implementations.Select(i => $"{i.GrainType} ({i.Prefix})"));
                    throw new ArgumentException($"Unable to identify a single appropriate grain type for interface {interfaceType}. Candidates: {candidates}");
                }
                else
                {
                    // 没有实现。
                }
            }
            else if (_genericMapping.TryGetValue(interfaceType, out result))
            {
            }
            else if (GenericGrainInterfaceType.TryParse(interfaceType, out var genericInterface) && genericInterface.IsConstructed)
            {
                var unconstructedInterface = genericInterface.GetGenericGrainType();
                if (TryGetGrainType(unconstructedInterface.Value, out var unconstructed))
                {
                    if (GenericGrainType.TryParse(unconstructed, out var genericGrainType))
                    {
                        if (genericGrainType.IsConstructed)
                        {
                            result = genericGrainType.GrainType;
                        }
                        else
                        {
                            result = genericGrainType.GetConstructed(genericInterface);
                        }
                    }
                    else
                    {
                        result = unconstructed;
                    }
                }

                _genericMapping[interfaceType] = result;
            }

            return !result.IsDefault;
        }

        /// <summary>
        /// 返回缓存；如果缓存已过期，则重新构建。
        /// </summary>
        /// <returns>缓存。</returns>
        private Cache GetCache()
        {
            if (_cache is Cache cache && cache.Version == _clusterManifestProvider.Current.Version)
            {
                return cache;
            }

            lock (_lockObj)
            {
                var manifest = _clusterManifestProvider.Current;
                cache = _cache!;
                if (cache is not null && cache.Version == manifest.Version)
                {
                    return cache;
                }

                return _cache = BuildCache(manifest);
            }
        }

        /// <summary>
        /// 构建缓存的解析映射。
        /// </summary>
        /// <param name="clusterManifest">当前集群清单。</param>
        /// <returns>缓存。</returns>
        private static Cache BuildCache(ClusterManifest clusterManifest)
        {
            var result = new Dictionary<GrainInterfaceType, CacheEntry>();

            foreach (var manifest in clusterManifest.AllGrainManifests)
            {
                foreach (var grainType in manifest.Grains)
                {
                    var id = grainType.Key;
                    grainType.Value.Properties.TryGetValue(WellKnownGrainTypeProperties.TypeName, out var typeName);
                    grainType.Value.Properties.TryGetValue(WellKnownGrainTypeProperties.FullTypeName, out var fullTypeName);
                    foreach (var property in grainType.Value.Properties)
                    {
                        if (!property.Key.StartsWith(WellKnownGrainTypeProperties.ImplementedInterfacePrefix, StringComparison.Ordinal)) continue;
                        var implemented = GrainInterfaceType.Create(property.Value);
                        string? interfaceTypeName;
                        if (manifest.Interfaces.TryGetValue(implemented, out var interfaceProperties))
                        {
                            interfaceProperties.Properties.TryGetValue(WellKnownGrainInterfaceProperties.TypeName, out interfaceTypeName);
                        }
                        else
                        {
                            interfaceTypeName = null;
                        }

                        // 尝试确定最合适的主要实现。
                        result.TryGetValue(implemented, out var entry);

                        var implementations = entry.Implementations ?? new List<(string Prefix, GrainType GrainType)>();
                        if (!implementations.Contains((fullTypeName!, id))) implementations.Add((fullTypeName!, id));

                        GrainType primaryImplementation;
                        if (!entry.PrimaryImplementation.IsDefault)
                        {
                            primaryImplementation = entry.PrimaryImplementation;
                        }
                        else if (interfaceProperties?.Properties is { } props && props.TryGetValue(WellKnownGrainInterfaceProperties.DefaultGrainType, out var defaultTypeString))
                        {
                            // 显式指定的默认 Grain 类型优先于其他类型。
                            primaryImplementation = GrainType.Create(defaultTypeString);
                        }
                        else if (string.Equals(interfaceTypeName?[1..], typeName, StringComparison.Ordinal))
                        {
                            // 否则，去掉接口名称中的“I”前缀后执行子字符串匹配。
                            primaryImplementation = id;
                        }
                        else
                        {
                            primaryImplementation = default;
                        }
                        result[implemented] = new CacheEntry(primaryImplementation, implementations);
                    }
                }
            }

            return new Cache(clusterManifest.Version, result);
        }

        /// <summary>
        /// 包含从 Grain 接口类型到其实现的映射。
        /// </summary>
        private class Cache
        {
            /// <summary>
            /// 初始化 <see cref="Cache"/> 类的新实例。
            /// </summary>
            /// <param name="version">此实例对应的集群清单版本。</param>
            /// <param name="map">接口映射。</param>
            public Cache(MajorMinorVersion version, Dictionary<GrainInterfaceType, CacheEntry> map)
            {
                this.Version = version;
                this.Map = map;
            }

            /// <summary>
            /// 获取此缓存对应的集群清单版本。
            /// </summary>
            public MajorMinorVersion Version { get; }

            /// <summary>
            /// 获取从 Grain 接口类型到其实现的映射。
            /// </summary>
            public Dictionary<GrainInterfaceType, CacheEntry> Map { get; }
        }

        /// <summary>
        /// 表示某个 Grain 接口类型的各个实现对应的 <see cref="GrainType"/> 值。
        /// </summary>
        private readonly struct CacheEntry
        {
            /// <summary>
            /// 初始化 <see cref="CacheEntry"/> 结构的新实例。
            /// </summary>
            /// <param name="primaryImplementation">主要实现类型。</param>
            /// <param name="implementations">其他实现及其 Grain 类型前缀的集合。</param>
            public CacheEntry(GrainType primaryImplementation, List<(string Prefix, GrainType GrainType)> implementations)
            {
                this.PrimaryImplementation = primaryImplementation;
                this.Implementations = implementations;
            }

            /// <summary>
            /// 获取主要实现类型。
            /// </summary>
            public GrainType PrimaryImplementation { get; }

            /// <summary>
            /// 获取实现类型及其类名前缀的集合。
            /// </summary>
            public List<(string Prefix, GrainType GrainType)> Implementations { get; }
        }
    }
}
