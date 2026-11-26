using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Metadata
{
    /// <summary>
    /// 负责解决 <see cref="GrainProperties"/> 值的 <see cref="GrainType"/> 。
    /// </summary>
    public class GrainPropertiesResolver
    {
        private readonly IClusterManifestProvider _clusterManifestProvider;

        /// <summary>
        /// 初始化 <see cref="GrainPropertiesResolver"/> 类的新实例。
        /// </summary>
        /// <param name="clusterManifestProvider">
        /// 集束货单提供者。
        /// </param>
        public GrainPropertiesResolver(IClusterManifestProvider clusterManifestProvider)
        {
            _clusterManifestProvider = clusterManifestProvider;
        }

        /// <summary>
        /// 获取所提供类型中的 grain 属性。
        /// </summary>
        /// <param name="grainType">
        /// 类型 grain 。
        /// </param>
        /// <returns>
        /// grain 属性。
        /// </returns>
        public GrainProperties GetGrainProperties(GrainType grainType)
        {
            if (!TryGetGrainProperties(grainType, out var result))
            {
                //ThrowNotFoundException(grainType);
                result = new GrainProperties(ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal, StringComparer.Ordinal));
            }

            return result;
        }

        /// <summary>
        /// 获取所提供类型中的 grain 属性。
        /// </summary>
        /// <param name="grainType">
        /// 类型 grain 。
        /// </param>
        /// <param name="properties">
        /// grain 属性。
        /// </param>
        /// <returns>
        /// 表示是否 grain 提供的属性 grain 类型。
        /// </returns>
        public bool TryGetGrainProperties(GrainType grainType, [NotNullWhen(true)] out GrainProperties? properties)
        {
            var clusterManifest = _clusterManifestProvider.Current;
            if (clusterManifest is null)
            {
                properties = default;
                return false;
            }

            GrainType lookupKey;
            if (GenericGrainType.TryParse(grainType, out var generic))
            {
                lookupKey = generic.GetUnconstructedGrainType().GrainType;
            }
            else
            {
                lookupKey = grainType;
            }

            foreach (var manifest in clusterManifest.AllGrainManifests)
            {
                if (manifest.Grains.TryGetValue(lookupKey, out properties))
                {
                    return true;
                }
            }

            properties = default;
            return false;
        }
    }
}
