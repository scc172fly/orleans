namespace Orleans.Caching.Internal;

/// <summary>
/// 表示在缓存生命周期内收集的缓存指标。
/// 如果指标已禁用。
/// </summary>
// Derived from BitFaster.Caching by Alex Peck
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/ICacheMetrics.cs?plain=1#L8C22-L8C35
internal interface ICacheMetrics
{
    /// <summary>
    /// 获取命中与未命中的比率，值为 1 表示命中率 100%。
    /// </summary>
    double HitRatio { get; }

    /// <summary>
    /// 获取向缓存发出的请求总数。
    /// </summary>
    long Total { get; }

    /// <summary>
    /// 获取缓存命中的总次数。
    /// </summary>
    long Hits { get; }

    /// <summary>
    /// 获取缓存未命中的总次数。
    /// </summary>
    long Misses { get; }

    /// <summary>
    /// 获取已逐出的条目总数。
    /// </summary>
    long Evicted { get; }

    /// <summary>
    /// 获取已更新的条目总数。
    /// </summary>
    long Updated { get; }
}
