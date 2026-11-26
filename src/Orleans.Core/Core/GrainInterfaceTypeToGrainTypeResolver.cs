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
    /// 协理律师 <see cref="GrainInterfaceType"/>与兼容的 s 相 <see cref="GrainType"/>.
    /// </summary>
    /// <remarks>
    /// 这主要用于最终用户呼唤 <see cref="IGrainFactory"/> 方法,而不需要过于明确。
    /// </remarks>
    public class GrainInterfaceTypeToGrainTypeResolver
    {
        private readonly Lock _lockObj = new();
        private readonly ConcurrentDictionary<GrainInterfaceType, GrainType> _genericMapping = new ConcurrentDictionary<GrainInterfaceType, GrainType>();
        private readonly IClusterManifestProvider _clusterManifestProvider;
        private Cache? _cache;

        /// <summary>
        /// 创建 <see cref="GrainInterfaceTypeToGrainTypeResolver"/> 类的新实例。
        /// </summary>
        /// <param name="clusterManifestProvider">集束货单提供者。</param>
        public GrainInterfaceTypeToGrainTypeResolver(IClusterManifestProvider clusterManifestProvider)
        {
            _clusterManifestProvider = clusterManifestProvider;
        }

        /// <summary>
        /// 返回 <see cref="GrainType"/> 支持所提供的 <see cref="GrainInterfaceType"/> 并且该字符串有一个执行型号,从提供的前缀字符串开始。
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
                            // 精确匹配优先
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
        /// 回返a <see cref="GrainType"/> 执行规定 <see cref="GrainInterfaceType"/>.
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
        /// 确定一个 <see cref="GrainType"/> 来实施所提供的 <see cref="GrainInterfaceType"/>,如果发现已经执行,则返回 <see langword="true"/> ;否则 <see langword="false"/>。
        /// </summary>
        /// <returns><see langword="true"/> 如果发现已执行;否则 <see langword="false"/>。</returns>
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
                    // 无执行
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
        /// 返回缓存,如果它过时则重建它。
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
        /// 构建缓存分辨率映射。
        /// </summary>
        /// <param name="clusterManifest">当前组群显示。</param>
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

                        // 尝试制定最佳初级实施
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
                            // 指定默认 grain 类型比其他类型要高。
                            primaryImplementation = GrainType.Create(defaultTypeString);
                        }
                        else if (string.Equals(interfaceTypeName?[1..], typeName, StringComparison.Ordinal))
                        {
                            // 否则,将使用接口名称上的子字符串匹配, 下划“ I” 。
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
        /// 包含从 grain 接口类型到该接口执行的映射。
        /// </summary>
        private class Cache
        {
            /// <summary>
            /// 初始化 <see cref="Cache"/> 类的新实例。
            /// </summary>
            /// <param name="version">本例对应的组群表单版本。</param>
            /// <param name="map">接口映射图。</param>
            public Cache(MajorMinorVersion version, Dictionary<GrainInterfaceType, CacheEntry> map)
            {
                this.Version = version;
                this.Map = map;
            }

            /// <summary>
            /// 获取此缓存对应的集表列表版本 。
            /// </summary>
            public MajorMinorVersion Version { get; }

            /// <summary>
            /// 将映射从 grain 接口类型获取到执行。
            /// </summary>
            public Dictionary<GrainInterfaceType, CacheEntry> Map { get; }
        }

        /// <summary>
        /// 代表执行 <see cref="GrainType"/> a 数值的值 grain 界面类型。
        /// </summary>
        private readonly struct CacheEntry
        {
            /// <summary>
            /// 初始化 <see cref="CacheEntry"/> 结构的新实例。
            /// </summary>
            /// <param name="primaryImplementation">主要执行类型。</param>
            /// <param name="implementations">其他执行的组及其 grain 型前缀。</param>
            public CacheEntry(GrainType primaryImplementation, List<(string Prefix, GrainType GrainType)> implementations)
            {
                this.PrimaryImplementation = primaryImplementation;
                this.Implementations = implementations;
            }

            /// <summary>
            /// 获得主要实施类型。
            /// </summary>
            public GrainType PrimaryImplementation { get; }

            /// <summary>
            /// 收集实施类型及其类名前缀。
            /// </summary>
            public List<(string Prefix, GrainType GrainType)> Implementations { get; }
        }
    }
}
