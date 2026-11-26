/*
 * 由Doug Lea在JCP成员协助下撰写
 * 向公共领域开放的专家组,如以下解释
 * http://creativecommons.org/publicdomain/zero/1.0/
 */

using Orleans;

namespace Orleans.Caching.Internal;

/// <summary>
/// 适合高通量的线性安全计数器 能够同时通过许多线索
/// </summary>
/// 根据道格·莉亚的 LongAdder 类
// 由Alex Peck从 BitFaster.Caching 取出
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Counters/Counter.cs
internal sealed class Counter : Striped64
{
    /// <summary>
    /// 创建初始数为零的新的对帐台。
    /// </summary>
    public Counter() { }

    /// <summary>
    /// 计算当前数。
    /// </summary>
    /// <returns>当前金额。</returns>
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
    /// 增加1倍
    /// </summary>
    public void Increment()
    {
        Add(1L);
    }

    /// <summary>
    /// 添加指定值。
    /// </summary>
    /// <param name="value">增加值。</param>
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
