using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Placement;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace Orleans.Runtime.Placement.Filtering;

internal class PreferredMatchSiloMetadataPlacementFilterDirector(
    ILocalSiloDetails localSiloDetails,
    ISiloMetadataCache siloMetadataCache)
    : IPlacementFilterDirector
{
    public IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos)
    {
        var preferredMatchSiloMetadataPlacementFilterStrategy = filterStrategy as PreferredMatchSiloMetadataPlacementFilterStrategy;
        var minCandidates = preferredMatchSiloMetadataPlacementFilterStrategy?.MinCandidates ?? 1;
        var orderedMetadataKeys = preferredMatchSiloMetadataPlacementFilterStrategy?.OrderedMetadataKeys ?? [];

        var localSiloMetadata = siloMetadataCache.GetSiloMetadata(localSiloDetails.SiloAddress).Metadata;

        if (localSiloMetadata.Count == 0)
        {
            return silos;
        }

        var siloList = silos.ToList();
        if (siloList.Count <= minCandidates)
        {
            return siloList;
        }

        // 返回符合大多数元数据密钥的 silos 列表。列表中的第一个密钥是最不重要的。
        // 这意味着列表中最后一个关键字是最重要的。
        // 如果未找到 silos 符合任何元数据密钥,请返回 silos的原始列表。
        var maxScore = 0;
        var siloScores = new int[siloList.Count];
        var scoreCounts = new int[orderedMetadataKeys.Length + 1];
        for (var i = 0; i < siloList.Count; i++)
        {
            var siloMetadata = siloMetadataCache.GetSiloMetadata(siloList[i]).Metadata;
            var siloScore = 0;
            for (var j = orderedMetadataKeys.Length - 1; j >= 0; --j)
            {
                if (siloMetadata.TryGetValue(orderedMetadataKeys[j], out var siloMetadataValue) &&
                    localSiloMetadata.TryGetValue(orderedMetadataKeys[j], out var localSiloMetadataValue) &&
                    siloMetadataValue == localSiloMetadataValue)
                {
                    siloScore = ++siloScores[i];
                    maxScore = Math.Max(maxScore, siloScore);
                }
                else
                {
                    break;
                }
            }
            scoreCounts[siloScore]++;
        }

        if (maxScore == 0)
        {
            return siloList;
        }

        var candidateCount = 0;
        var scoreCutOff = orderedMetadataKeys.Length;
        for (var i = scoreCounts.Length - 1; i >= 0; i--)
        {
            candidateCount += scoreCounts[i];
            if (candidateCount >= minCandidates)
            {
                scoreCutOff = i;
                break;
            }
        }

        return siloList.Where((_, i) => siloScores[i] >= scoreCutOff);
    }
}
