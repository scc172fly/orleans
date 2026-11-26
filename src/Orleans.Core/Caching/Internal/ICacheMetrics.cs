namespace Orleans.Caching.Internal;

/// <summary>
/// 代表缓冲使用寿命期间收集的缓存量度。
/// 如果测量标准被禁用。
/// </summary>
// 由Alex Peck从 BitFaster.Caching 取出
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/ICacheMetrics.cs?plain=1#L8C22-L8C35
internal interface ICacheMetrics
{
    /// <summary>
    /// 获得点击率和误差率, 其中值为 1 表示100%的点击率 。
    /// </summary>
    double HitRatio { get; }

    /// <summary>
    /// 获得向缓存处提出的申请总数。
    /// </summary>
    long Total { get; }

    /// <summary>
    /// 获得缓冲点击总数。
    /// </summary>
    long Hits { get; }

    /// <summary>
    /// 获得缓存失灵的总数。
    /// </summary>
    long Misses { get; }

    /// <summary>
    /// 获得被逐出物品的总数。
    /// </summary>
    long Evicted { get; }

    /// <summary>
    /// 获得更新项目总数。
    /// </summary>
    long Updated { get; }
}
