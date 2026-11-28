using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Metadata
{
    /// <summary>
    /// 负责解析 <see cref="GrainProperties"/>，其对应的类型值为 <see cref="GrainType"/>。
    /// </summary>
    public class GrainPropertiesResolver
    {
        private readonly IClusterManifestProvider _clusterManifestProvider;

        /// <summary>
        /// 初始化 <see cref="GrainPropertiesResolver"/> 类的新实例。
        /// </summary>
        /// <param name="clusterManifestProvider">
        /// 集群清单提供程序。
        /// </param>
        public GrainPropertiesResolver(IClusterManifestProvider clusterManifestProvider)
        {
            _clusterManifestProvider = clusterManifestProvider;
        }

        /// <summary>
        /// 获取所提供类型的 Grain 属性。
        /// </summary>
        /// <param name="grainType">
        /// Grain 类型。
        /// </param>
        /// <returns>
        /// Grain 属性。
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
        /// 获取所提供类型的 Grain 属性。
        /// </summary>
        /// <param name="grainType">
        /// Grain 类型。
        /// </param>
        /// <param name="properties">
        /// Grain 属性。
        /// </param>
        /// <returns>
        /// 指示能否找到所提供 Grain 类型对应属性的值。
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
