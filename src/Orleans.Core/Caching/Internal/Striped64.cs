using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

/*
 * Written by Doug Lea with assistance from members of JCP JSR-166
 * Expert Group and released to the public domain, as explained at
 * http://creativecommons.org/publicdomain/zero/1.0/
 */

namespace Orleans.Caching.Internal;

/*
 * 此类维护一个延迟初始化的表，其中包含以原子方式
 * 更新的变量，另有一个额外的“base”字段。表的大小
 * 为 2 的幂。索引使用每个线程的哈希码与掩码计算得到。
 * 此类中几乎所有声明都是包级私有的，
 * 由子类直接访问。
 *
 * 表项是 Cell 类的实例；它是经过填充的 AtomicLong 变体，
 * 用于减少大多数处理器上的缓存争用。对于大多数原子对象，
 * 填充并无必要，因为它们通常不规则地
 * 分散在内存中，彼此不太会相互干扰。
 * 但数组中的原子对象往往
 * 相邻存放，因此如果没有这一措施，通常会共享
 * 缓存行（对性能产生很大的负面影响），
 * 而填充正是用于预防这一情况。
 *
 * 部分原因在于 Cell 较大，我们避免提前创建
 * 它们，直到需要时才创建。没有争用时，所有
 * 更新都写入 base 字段。首次发生争用时（即
 * 更新 base 的 CAS 失败），将表初始化为大小 2。
 * 如果继续发生争用，则将表大小加倍，直到
 * 达到大于或等于 CPU 数量的最小
 * 2 的幂。表中的槽位保持为空（null），直到
 * 需要使用它们为止。
 *
 * 使用单个自旋锁（“busy”）初始化和
 * 调整表的大小，并用新 Cell 填充槽位。
 * 不需要阻塞锁；如果无法获取锁，
 * 线程会尝试其他槽位（或 base）。这些
 * 重试会增加争用、降低局部性，
 * 但仍优于其他方案。
 *
 * 每个线程的哈希码初始化为随机值。
 * 在执行更新操作时，CAS 失败表示出现争用
 * 或表中的哈希冲突（参见
 * retryUpdate 方法）。发生冲突时，如果表大小尚未达到
 * 容量上限，则将其加倍，除非其他线程
 * 持有锁。如果哈希对应的槽位为空且锁
 * 可用，则创建新 Cell。否则，如果槽位已有 Cell，
 * 则尝试 CAS。重试采用“双重哈希”，
 * 使用第二个哈希（Marsaglia XorShift）尝试找到一个
 * 空闲槽位。
 *
 * 对表的大小设定上限，是因为当线程数
 * 超过 CPU 数时，假设每个线程都绑定到某个 CPU，
 * 就存在一个将线程映射到槽位且能够消除冲突的
 * 完美哈希函数。达到容量上限时，我们通过
 * 随机改变发生冲突的线程的哈希码
 * 来寻找这一映射。由于搜索是随机的，且只有
 * CAS 失败时才知道发生了冲突，收敛可能很慢，
 * 并且由于线程通常并不永久绑定到 CPU，
 * 甚至可能完全不收敛。然而，尽管存在这些限制，
 * 在这些情况下实际观测到的争用率通常较低。
 *
 * 当曾经哈希到某个 Cell 的线程终止时，
 * 或者当表大小加倍导致没有任何线程在
 * 扩展后的掩码下哈希到该 Cell 时，该 Cell 可能不再被使用。
 * 我们不尝试检测或移除这样的 Cell，
 * 因为我们假设对于长时间运行的实例，曾观测到的
 * 争用水平会再次出现，因此最终还会
 * 需要这些 Cell；对于短期实例，则无关紧要。
 */

/// <summary>
/// 维护一个延迟初始化的表，其中包含以原子方式更新的变量，另有一个额外的
/// “base”字段。表大小为 2 的幂。索引使用线程 ID 与掩码计算得到。
/// </summary>
// Derived from BitFaster.Caching by Alex Peck
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Counters/Striped64.cs
[ExcludeFromCodeCoverage]
internal abstract class Striped64
{
    // CPU 数量，用于限制表的大小。
    private static readonly int MaxBuckets = Environment.ProcessorCount * 4;

    /// <summary>
    /// 基准值，主要在无争用时使用，也作为表初始化发生竞态时的
    /// 后备值。通过 CAS 更新。
    /// </summary>
    protected PaddedLong @base;

    /// <summary>
    /// 非 null 时，大小为 2 的幂。
    /// </summary>
    protected Cell[]? cells;
    private int _cellsBusy;

    /// <summary>
    /// PaddedLong 的包装器。
    /// </summary>
    /// <param name="value">值。</param>
    protected sealed class Cell(long value)
    {
        /// <summary>
        /// Cell 的值。
        /// </summary>
        public PaddedLong Value = new() { Value = value };
    }

    /**
     * 将 cellsBusy 字段通过 CAS 从 0 改为 1，以获取锁。
     */
    private bool CasCellsBusy() => Interlocked.CompareExchange(ref _cellsBusy, 1, 0) == 0;

    private void VolatileWriteNotBusy() => Volatile.Write(ref _cellsBusy, 0);

    /**
     * 返回当前线程的探测值。
     * 由于包组织方面的限制，从 ThreadLocalRandom 复制而来。
     */
    protected static int GetProbe() =>
        // 注意：相比引入随机数，这样做的吞吐量更高。
        Environment.CurrentManagedThreadId;

    /**
    * 以伪随机方式推进并记录给定线程的
    * 指定探测值。
    * 由于包组织方面的限制，从 ThreadLocalRandom 复制而来。
    */
    private static int AdvanceProbe(int probe)
    {
        // 异或移位（xorshift）。
        probe ^= probe << 13;
        probe ^= (int)((uint)probe >> 17);
        probe ^= probe << 5;
        return probe;
    }

    /**
     * 处理涉及初始化、调整大小、
     * 创建新 Cell 和/或发生争用的更新情况。解释见
     * 上文。此方法存在乐观重试代码常见的模块化问题，
     * 依赖对一组读取操作进行
     * 重新检查。
     *
     * @param x 值
     * @param wasUncontended 如果调用前 CAS 失败，则为 false
     */
    protected void LongAccumulate(long x, bool wasUncontended)
    {
        var h = GetProbe();

        // 如果上次槽位非空，则为 true。
        var collide = false;
        while (true)
        {
            Cell[]? @as; Cell a; int n; long v;
            if ((@as = cells) != null && (n = @as.Length) > 0)
            {
                if ((a = @as[n - 1 & h]) == null)
                {
                    if (_cellsBusy == 0)
                    {
                        // 尝试附加新 Cell。
                        // 乐观地创建。
                        var r = new Cell(x);
#pragma warning disable CA1508 // 另一线程可能在乐观读取之间获取 Cell 锁；CAS 确立所有权。
                        if (_cellsBusy == 0 && CasCellsBusy())
#pragma warning restore CA1508
                        {
                            try
                            {
                                // 持锁重新检查。
                                Cell[]? rs; int m, j;
                                if ((rs = cells) != null &&
                                    (m = rs.Length) > 0 &&
                                    rs[j = m - 1 & h] == null)
                                {
                                    rs[j] = r;
                                    break;
                                }
                            }
                            finally
                            {
                                VolatileWriteNotBusy();
                            }

                            // 槽位现在非空。
                            continue;
                        }
                    }
                    collide = false;
                }
                else if (!wasUncontended)
                {
                    // 已知此前 CAS 已失败。
                    // 重新计算哈希后继续。
                    wasUncontended = true;
                }
                else if (a.Value.CompareAndSwap(v = a.Value.VolatileRead(), v + x))
                {
                    break;
                }
                else if (n >= MaxBuckets || cells != @as)
                {
                    // 已达到最大大小或信息已过时。
                    collide = false;
                }
                else if (!collide)
                {
                    collide = true;
                }
                else if (_cellsBusy == 0 && CasCellsBusy())
                {
                    try
                    {
                        if (cells == @as)
                        {
                            // 如果信息未过时，则扩展表。
                            var rs = new Cell[n << 1];
                            for (var i = 0; i < n; ++i)
                            {
                                rs[i] = @as[i];
                            }

                            cells = rs;
                        }
                    }
                    finally
                    {
                        VolatileWriteNotBusy();
                    }

                    collide = false;

                    // 使用扩展后的表重试。
                    continue;
                }

                // 重新计算哈希。
                h = AdvanceProbe(h);
            }
            else if (_cellsBusy == 0 && cells == @as && CasCellsBusy())
            {
                try
                {
                    // 初始化表。
                    if (cells == @as)
                    {
                        var rs = new Cell[2];
                        rs[h & 1] = new Cell(x);
                        cells = rs;
                        break;
                    }
                }
                finally
                {
                    VolatileWriteNotBusy();
                }
            }

            // 回退为使用 base。
            else if (@base.CompareAndSwap(v = @base.VolatileRead(), v + x))
            {
                break;
            }
        }
    }
}
