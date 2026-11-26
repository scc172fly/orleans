using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Orleans.Runtime;

namespace Orleans.Metadata
{
    /// <summary>
    /// 描述给定 grain 类型类型的绑定。
    /// </summary>
    /// <remarks>
    /// 约束性是将 grains 与其他资源建立公开联系的一种方式。
    /// </remarks>
    public class GrainBindings
    {
        /// <summary>
        /// 初始化 <see cref="GrainBindings"/> 类的新实例。
        /// </summary>
        /// <param name="grainType"> grain 类型。</param>
        /// <param name="bindings">指定 grain 类型组装的绑定。</param>
        public GrainBindings(GrainType grainType, ImmutableArray<ImmutableDictionary<string, string>> bindings)
        {
            this.GrainType = grainType;
            this.Bindings = bindings;
        }

        /// <summary>
        /// 获取 grain 类型。
        /// </summary>
        public GrainType GrainType { get; }

        /// <summary>
        /// 获取指定 grain 类型上的绑定。
        /// </summary>
        public ImmutableArray<ImmutableDictionary<string, string>> Bindings { get; }
    }

    /// <summary>
    /// 解决对 grain 类型的约束。
    /// </summary>
    public class GrainBindingsResolver
    {
        private const string BindingPrefix = WellKnownGrainTypeProperties.BindingPrefix + ".";
        private const char BindingIndexEnd = '.';
        private readonly Lock _lockObj = new();
        private readonly ConcurrentDictionary<GenericGrainType, GrainType> _genericMapping = new ConcurrentDictionary<GenericGrainType, GrainType>();
        private readonly IClusterManifestProvider _clusterManifestProvider;
        private Cache _cache;

        /// <summary>
        /// 初始化 <see cref="GrainBindingsResolver"/> 类的新实例。
        /// </summary>
        /// <param name="clusterManifestProvider">
        /// 集束货单提供者。
        /// </param>
        public GrainBindingsResolver(IClusterManifestProvider clusterManifestProvider)
        {
            _clusterManifestProvider = clusterManifestProvider;
            _cache = BuildCache(_clusterManifestProvider.Current);
        }

        /// <summary>
        /// 为提供 grain 类型设置绑定。
        /// </summary>
        /// <param name="grainType">
        /// 类型 grain 。
        /// </param>
        /// <returns>装订 grain 。</returns>
        public GrainBindings GetBindings(GrainType grainType)
        {
            GrainType lookupType;
            if (GenericGrainType.TryParse(grainType, out var generic))
            {
                if (!_genericMapping.TryGetValue(generic, out lookupType))
                {
                    lookupType = _genericMapping[generic] = generic.GetUnconstructedGrainType().GrainType;
                }
            }
            else
            {
                lookupType = grainType;
            }

            var cache = GetCache();
            if (cache.Map.TryGetValue(lookupType, out var result))
            {
                return result;
            }

            return new GrainBindings(grainType, ImmutableArray<ImmutableDictionary<string, string>>.Empty);
        }

        /// <summary>
        /// 装订所有的约束。
        /// </summary>
        /// <returns>全部收藏 grain 约束性。</returns>
        public (MajorMinorVersion Version, ImmutableDictionary<GrainType, GrainBindings> Bindings) GetAllBindings()
        {
            var cache = GetCache();
            return (cache.Version, cache.Map);
        }

        private Cache GetCache()
        {
            var cache = _cache;
            var manifest = _clusterManifestProvider.Current;
            if (manifest.Version == cache.Version)
            {
                return cache;
            }

            lock (_lockObj)
            {
                cache = _cache;
                manifest = _clusterManifestProvider.Current;
                if (manifest.Version == cache.Version)
                {
                    return cache;
                }

                return _cache = BuildCache(manifest);
            }
        }

        private static Cache BuildCache(ClusterManifest clusterManifest)
        {
            var result = new Dictionary<GrainType, GrainBindings>();

            var bindings = new Dictionary<string, Dictionary<string, string>>();
            foreach (var manifest in clusterManifest.AllGrainManifests)
            {
                foreach (var grainType in manifest.Grains)
                {
                    var id = grainType.Key;
                    if (result.ContainsKey(id)) continue;
                    bindings.Clear();
                    foreach (var pair in grainType.Value.Properties)
                    {
                        if (TryExtractBindingProperty(pair, out var binding))
                        {
                            if (!bindings.TryGetValue(binding.Index, out var properties))
                            {
                                bindings[binding.Index] = properties = new Dictionary<string, string>();
                            }

                            properties.Add(binding.Key, binding.Value);
                        }
                    }

                    var builder = ImmutableArray.CreateBuilder<ImmutableDictionary<string, string>>();
                    foreach (var binding in bindings.Values)
                    {
                        builder.Add(ImmutableDictionary.CreateRange(binding));
                    }

                    result.Add(id, new GrainBindings(id, builder.ToImmutable()));
                }
            }

            return new Cache(clusterManifest.Version, result.ToImmutableDictionary());

            bool TryExtractBindingProperty(KeyValuePair<string, string> property, out (string Index, string Key, string Value) result)
            {
                if (!property.Key.StartsWith(BindingPrefix, StringComparison.Ordinal)
                    || property.Key.IndexOf(BindingIndexEnd, BindingPrefix.Length) is int indexEndIndex && indexEndIndex < 0)
                {
                    result = default;
                    return false;
                }

                var bindingIndex = property.Key[BindingPrefix.Length..indexEndIndex];
                var bindingKey = property.Key[(indexEndIndex + 1)..];

                if (string.IsNullOrWhiteSpace(bindingIndex) || string.IsNullOrWhiteSpace(bindingKey))
                {
                    result = default;
                    return false;
                }

                result = (bindingIndex, bindingKey, property.Value);
                return true;
            }
        }

        private class Cache
        {
            public Cache(MajorMinorVersion version, ImmutableDictionary<GrainType, GrainBindings> map)
            {
                this.Version = version;
                this.Map = map;
            }

            public MajorMinorVersion Version { get; }

            public ImmutableDictionary<GrainType, GrainBindings> Map { get; }
        }
    }
}
