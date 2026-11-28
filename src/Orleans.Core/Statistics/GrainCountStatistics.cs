using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orleans.Runtime;

/// <summary>
/// 针对各 Grain 类型激活实例数量的集中统计信息。
/// </summary>
internal class GrainCountStatistics(CatalogInstruments instruments)
{
    public IEnumerable<KeyValuePair<string, long>> GetSimpleGrainStatistics()
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var metrics in instruments.GrainTypes)
        {
            if (Volatile.Read(ref metrics.GrainClassName) is { } grainClassName)
            {
                ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, grainClassName, out _);
                count += Volatile.Read(ref metrics.GrainCount);
            }
        }

        return counts.Where(p => p.Value > 0);
    }
}
