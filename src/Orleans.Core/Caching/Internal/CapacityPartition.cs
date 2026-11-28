using System;
using System.Diagnostics;

namespace Orleans.Caching.Internal;

/// <summary>
/// 容量分区方案，通过将 80% 的容量分配给温队列，
/// 优先保留频繁访问的条目。
/// </summary>
// Derived from BitFaster.Caching by Alex Peck
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Lru/FavorWarmPartition.cs
[DebuggerDisplay("{Hot}/{Warm}/{Cold}")]
internal readonly struct CapacityPartition
{
    /// <summary>
    /// 默认将 80% 的容量分配给温队列，其余 20% 在热队列和冷队列之间平均分配。
    /// 这有利于保留频繁访问的条目。
    /// </summary>
    public const double DefaultWarmRatio = 0.8;

    /// <summary>
    /// 使用指定容量和默认温区段比例初始化 CapacityPartition 类的新实例。
    /// </summary>
    /// <param name="totalCapacity">总容量。</param>
    public CapacityPartition(int totalCapacity)
        : this(totalCapacity, DefaultWarmRatio)
    {
    }

    /// <summary>
    /// 使用指定容量和温区段比例初始化 CapacityPartition 类的新实例。
    /// </summary>
    /// <param name="totalCapacity">总容量。</param>
    /// <param name="warmRatio">温区段条目与热、冷区段条目的比例。</param>
    public CapacityPartition(int totalCapacity, double warmRatio)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalCapacity, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(warmRatio, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(warmRatio, 1.0);

        var (hot, warm, cold) = ComputeQueueCapacity(totalCapacity, warmRatio);
        Debug.Assert(cold >= 1);
        Debug.Assert(warm >= 1);
        Debug.Assert(hot >= 1);
        Hot = hot;
        Warm = warm;
        Cold = cold;
    }

    public int Cold { get; }

    public int Warm { get; }

    public int Hot { get; }

    private static (int hot, int warm, int cold) ComputeQueueCapacity(int capacity, double warmRatio)
    {
        var warm2 = (int)(capacity * warmRatio);
        var hot2 = (capacity - warm2) / 2;

        if (hot2 < 1)
        {
            hot2 = 1;
        }

        var cold2 = hot2;

        var overflow = warm2 + hot2 + cold2 - capacity;
        warm2 -= overflow;

        return (hot2, warm2, cold2);
    }
}
