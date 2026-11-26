using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

/*
 * 由Doug Lea在JCP成员协助下撰写
 * 向公共领域开放的专家组,如以下解释
 * http://creativecommons.org/publicdomain/zero/1.0/
 */

namespace Orleans.Caching.Internal;

/*
 * 本类维持一个低迷初始原子表
 * 更新的变量,加上一个额外的“基准”字段。
 * 是一个两个的功率。索引使用隐形的全线散列码。
 * 这一类的几乎所有申报都是一揽子私人申报,
 * 子类直接访问。
 *
 * 表条目为类单元格; AtomicLong 加插的变量
 * 以降低大多数处理器的缓存争议。
 * 多数原子的超杀率高,因为它们通常不规则
 * 散落在记忆中,所以不互相干扰。
 * 原子物体的阵列中
 * 彼此相邻而居,彼此之间常常分享
 * 缓存行(具有巨大的负性性效影响)
 * 预防这个
 *
 * 部分是因为细胞相对较大,我们避免了产生
 * 直到他们被需要,直到他们获得他们所需要的。
 * 向基字段更新。
 * 基调更新时失败 CAS),表格初始化为大小 2。
 * 进一步争论后,表格的面积翻一番,直到
 * 功率,以大于或等于两千米的最接近功率计算。
 * CPUS数目。 表空位一直为空( 无效) , 直到它们
 * 需要。
 *
 * 用于初始化和
 * 调整表格的大小,以及用新的单元格填充空格。
 * 无需设置锁锁;当锁不锁时
 * 可用, 线索尝试其他空格( 或基底) 。 在这些
 * 重试,争议增加,地点减少
 * 仍然比替代品好。
 *
 * 纯散列代码初始化为随机值。
 * 内相和/或表碰撞用失败表示
 * CASs 进行更新操作时使用CAS(见方法)
 * retryUpdate.)碰撞时,如果表尺寸小于
 * 容量, 除非有其他线索, 否则其尺寸会加倍
 * 锁定。如果一个散列空位是空的,而锁是
 * 可用时, 新单元格会创建。 否则, 如果空格
 * 存在,CAS正在测试。 重试以“ 双散列” 进行,
 * 使用二类大麻( Marsaglia XorShift)来查找
 * 空位。
 *
 * 表格大小被上盖, 因为当有更多线索时
 * 假设每条线都系在CPU上,
 * 会有完美的散列函数绘图线
 * 消除碰撞的空格。当我们到达能力,
 * 随机修改散列代码,以查找此映射
 * 相撞线。 因为搜索是随机的, 碰撞
 * 只能通过CAS失败才知道,趋同速度可能缓慢,
 * 并且因为线条通常不会永远被连接到CPUS,
 * 然而,尽管存在这些限制,
 * 在这些情况下,观察到的争议率一般较低。
 *
 * 当一个单元格在
 * 发生下列情况时终止:
 * 将表格翻倍, 使桌子下没有线条
 * 我们不试图检测或移除这些细胞
 * 假设对长期存在的情况,观察到
 * 参数水平将重复, 所以单元格最终会
 * 需要再次;对于短期生命,这不重要。
 */

/// <summary>
/// 维持一个由原子更新变量加上额外变量组成的 
/// 表格大小是两个的功率。索引使用遮蔽线条识别码。
/// </summary>
// 由Alex Peck从 BitFaster.Caching 取出
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Counters/Striped64.cs
[ExcludeFromCodeCoverage]
internal abstract class Striped64
{
    // CPUS数目,按表格大小排列
    private static readonly int MaxBuckets = Environment.ProcessorCount * 4;

    /// <summary>
    /// 基值主要用于没有争议但作为后备使用的基值 
    /// 在表格初始化赛事中更新。 通过 CAS 更新 。
    /// </summary>
    protected PaddedLong @base;

    /// <summary>
    /// 当非核时,大小为 2 的功率。
    /// </summary>
    protected Cell[]? cells;
    private int _cellsBusy;

    /// <summary>
    /// PaddedLong的包装纸。
    /// </summary>
    /// <param name="value">数值。</param>
    protected sealed class Cell(long value)
    {
        /// <summary>
        /// 单元格的值 。
        /// </summary>
        public PaddedLong Value = new() { Value = value };
    }

    /**
     * CAS 字段 cellsBusy 从 0 到 1 获取锁定 。
     */
    private bool CasCellsBusy() => Interlocked.CompareExchange(ref _cellsBusy, 1, 0) == 0;

    private void VolatileWriteNotBusy() => Volatile.Write(ref _cellsBusy, 0);

    /**
     * 返回当前线索的探测值。
     * 由于包装限制,从 ThreadLocalRandom 中复制。
     */
    protected static int GetProbe() =>
        // 注:这导致的产量高于随机引入。
        Environment.CurrentManagedThreadId;

    /**
    * 优度随机推进并记录给定的探测值
    * 给定线索。
    * 由于包装限制,从 ThreadLocalRandom 中复制。
    */
    private static int AdvanceProbe(int probe)
    {
        // xorfinight 转换器
        probe ^= probe << 13;
        probe ^= (int)((uint)probe >> 17);
        probe ^= probe << 5;
        return probe;
    }

    /**
     * 处理涉及初始化、重新确定大小、
     * 创建新单元格和/或争论。见上文
     * 这种方法受到通常的非模式性的影响
     * 依赖重新校对的一套
     * 读起来。
     *
     * @ 参数 x 值
     * @ 参数 wasUncontended 如果 CAS 在呼叫前失败, 则无效
     */
    protected void LongAccumulate(long x, bool wasUncontended)
    {
        var h = GetProbe();

        // 如果最后一个空格为真实
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
                        // 尝试附加新单元格
                        // 乐观地创建
                        var r = new Cell(x);
#pragma warning disable CA1508 // Another thread can acquire the cell lock between the optimistic reads; CAS establishes ownership.
                        if (_cellsBusy == 0 && CasCellsBusy())
#pragma warning restore CA1508
                        {
                            try
                            {
                                // 锁定中重新检查
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

                            // 槽现在不是空的
                            continue;
                        }
                    }
                    collide = false;
                }
                else if (!wasUncontended)
                {
                    // CAS 已知已失败
                    // 重新发牌后继续
                    wasUncontended = true;
                }
                else if (a.Value.CompareAndSwap(v = a.Value.VolatileRead(), v + x))
                {
                    break;
                }
                else if (n >= MaxBuckets || cells != @as)
                {
                    // 最大尺寸或淡度
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
                            // 除非变色, 除非平滑
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

                    // 重试使用扩大表格
                    continue;
                }

                // 重写
                h = AdvanceProbe(h);
            }
            else if (_cellsBusy == 0 && cells == @as && CasCellsBusy())
            {
                try
                {
                    // 初始化表格
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

            // 返回基地
            else if (@base.CompareAndSwap(v = @base.VolatileRead(), v + x))
            {
                break;
            }
        }
    }
}
