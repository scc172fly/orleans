using System;
using System.Diagnostics;

namespace Orleans.Caching.Internal;

/// <summary>
/// 能力分割方案,通过分配80%的配额,有利于经常存取的项目
/// 以热队列显示容量。
/// </summary>
// 由Alex Peck从 BitFaster.Caching 取出
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Lru/FavorWarmPartition.cs
[DebuggerDisplay("{Hot}/{Warm}/{Cold}")]
internal readonly struct CapacityPartition
{
    /// <summary>
    /// 默认值为80% 容量分配到热队列,20% 以热和冷平均分。
    /// 这有助于经常访问的物品。
    /// </summary>
    public const double DefaultWarmRatio = 0.8;

    /// <summary>
    /// 初始化 CapacityPartition 类的新实例,该例子具有指定容量和默认热率。
    /// </summary>
    /// <param name="totalCapacity">总能力。</param>
    public CapacityPartition(int totalCapacity)
        : this(totalCapacity, DefaultWarmRatio)
    {
    }

    /// <summary>
    /// 初始化一个 CapacityPartition 类的新实例,该例子具有特定容量和热比。
    /// </summary>
    /// <param name="totalCapacity">总能力。</param>
    /// <param name="warmRatio">热物与热物和冷物之比。</param>
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
