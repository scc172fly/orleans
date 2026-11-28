/*
 * Written by Doug Lea with assistance from members of JCP JSR-166
 * Expert Group and released to the public domain, as explained at
 * http://creativecommons.org/publicdomain/zero/1.0/
 */

using Orleans;

namespace Orleans.Caching.Internal;

/// <summary>
/// 线程安全的计数器，适用于多个并发线程的高吞吐量计数。
/// </summary>
/// 基于 Doug Lea 的 LongAdder 类。
// Derived from BitFaster.Caching by Alex Peck
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Counters/Counter.cs
internal sealed class Counter : Striped64
{
    /// <summary>
    /// 创建初始总和为零的新 Counter。
    /// </summary>
    public Counter() { }

    /// <summary>
    /// 计算当前计数。
    /// </summary>
    /// <returns>当前总和。</returns>
    public long Count()
    {
        var @as = cells; Cell a;
        var sum = @base.VolatileRead();
        if (@as != null)
        {
            for (var i = 0; i < @as.Length; ++i)
            {
                if ((a = @as[i]) != null)
                    sum += a.Value.VolatileRead();
            }
        }
        return sum;
    }

    /// <summary>
    /// 增加 1。
    /// </summary>
    public void Increment()
    {
        Add(1L);
    }

    /// <summary>
    /// 加上指定值。
    /// </summary>
    /// <param name="value">要添加的值。</param>
    public void Add(long value)
    {
        Cell[]? @as;
        long b, v;
        int m;
        Cell a;
        if ((@as = cells) != null || !@base.CompareAndSwap(b = @base.VolatileRead(), b + value))
        {
            var uncontended = true;
            if (@as == null || (m = @as.Length - 1) < 0 || (a = @as[GetProbe() & m]) == null || !(uncontended = a.Value.CompareAndSwap(v = a.Value.VolatileRead(), v + value)))
            {
                LongAccumulate(value, uncontended);
            }
        }
    }
}
