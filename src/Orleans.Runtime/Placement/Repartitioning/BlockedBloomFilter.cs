using System.Diagnostics;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Placement.Repartitioning;

/// <summary>
/// 经过调优的分块布隆过滤器实现。
/// </summary>
/// <remarks><i>
/// <para>这是经过调优以满足所需误报率的分块布隆过滤器（BBF）。
/// 调优需要大量时间，因此此过滤器支持 [0.1% - 1%] 范围内的误报率。
/// 因为误报率通过多项式回归拟合，所以支持此范围内任意精度的数值。</para>
/// <para>详情参见第 3 节：https://www.cs.amherst.edu/~ccmcgeoch/cs34/papers/cacheefficientbloomfilters-jea.pdf</para>
/// </i></remarks>
internal sealed class BlockedBloomFilter
{
    private const int BlockSize = 32; // 较大的值可提高速度，但空间开销也很大。
    private const double Ln2Squared = 0.4804530139182014246671025263266649717305529515945455;
    private const double MinFpRate = 0.001; // 0.1%
    private const double MaxFpRate = 0.01;  // 1%

    private readonly int _blocks;
    private readonly int[] _filter;

    // 用于匹配 fpRate 的回归系数（通过多项式回归得出），因为 fpRate 越低，实际误报率偏差越明显。
    // 例如参见 https://gist.github.com/ledjon-behluli/d339cbd54568ceb5464d3a947ac8f08e
    private static readonly double[] Coefficients =
    [
         4.0102253166524500e-003,
        -1.6272682781603145e+001,
         2.7169897602930665e+004,
        -2.4527698904812500e+007,
         1.3273846004698063e+010,
        -4.4943809759769805e+012,
         9.5588839677303638e+014,
        -1.2081452101930328e+017,
         6.8958853188430172e+018,
         2.6889929911921561e+020,
        -7.1061179529975569e+022,
         4.4109449793357217e+024,
        -9.8041203512310751e+025
    ];

    /// <param name="capacity">预计的元素总数。</param>
    /// <param name="fpRate">限制在 [<see cref="MinFpRate"/> - <see cref="MaxFpRate"/>] 范围内。</param>
    /// <exception cref="ArgumentOutOfRangeException"/>
    public BlockedBloomFilter(int capacity, double fpRate)
    {
        if (fpRate is < MinFpRate or > MaxFpRate)
        {
            throw new ArgumentOutOfRangeException($"False positive rate '{fpRate}', is outside of the allowed range '{MinFpRate} - {MaxFpRate}'");
        }

        var adjFpRate = RegressFpRate(fpRate);
        Debug.Assert(adjFpRate < fpRate);
        var bits = (int)(-1 * capacity * Math.Log(adjFpRate) / Ln2Squared);

        _blocks = bits / BlockSize;
        _filter = new int[_blocks + 1];
    }

    private static double RegressFpRate(double fpRate)
    {
        double temp = 1;
        double result = 0;

        foreach (var coefficient in Coefficients)
        {
            result += coefficient * temp;
            temp *= fpRate;
        }

        return Math.Abs(result);
    }

    public void Add(GrainId id)
    {
        var hash = XxHash3.HashToUInt64(id.Key.AsSpan(), id.GetUniformHashCode());
        var index = GetBlockIndex(hash, _blocks); // 必须在旋转哈希值之前获取索引。

        hash ^= BitOperations.RotateLeft(hash, 32);

        // 使用两个掩码将哈希值的位分散到过滤器的多个位置。
        var mask1 = ComputeMask1(hash);
        var mask2 = ComputeMask2(hash);

        // 跨两个块设置位，使单个哈希值的位更均匀地分布在过滤器中。
        _filter[index] |= mask1;
        _filter[index + 1] |= mask2;
    }

    public bool Contains(GrainId id)
    {
        var hash = XxHash3.HashToUInt64(id.Key.AsSpan(), id.GetUniformHashCode());
        var index = GetBlockIndex(hash, _blocks); // 必须在旋转哈希值之前获取索引。

        hash ^= BitOperations.RotateLeft(hash, 32);

        var block1 = _filter[index];
        var block2 = _filter[index + 1];

        var mask1 = ComputeMask1(hash);
        var mask2 = ComputeMask2(hash);

        return (mask1 & block1) == mask1 && (mask2 & block2) == mask2;
    }

    public void Reset() => Array.Clear(_filter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBlockIndex(ulong hash, int buckets) => (int)(((int)hash & 0xffffffffL) * buckets >> 32);

    /// <summary>
    /// 设置 <paramref name="hash"/> 中与低位以及右移 6 位后对应的位。
    /// </summary>
    /// <param name="hash">旋转后的哈希值。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeMask1(ulong hash) => (1 << (int)hash) | (1 << ((int)hash >> 6));

    /// <summary>
    /// 设置 <paramref name="hash"/> 中对应的位，以及右移 12 位和 18 位后的位。
    /// </summary>
    /// <param name="hash">旋转后的哈希值。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeMask2(ulong hash) => (1 << ((int)hash >> 12)) | (1 << ((int)hash >> 18));
}

/// <summary>
/// 实现为一组 <see cref="BlockedBloomFilter"/>，通过轮换来保留已锚定的 Grain。
/// 如果 Grain 处于“热”状态，它很可能在多轮之后仍被保留，否则会被移除。
/// </summary>
internal sealed class AnchoredGrainsFilter
{
    // 索引 0 对应“活动”过滤器。
    // 索引 1 至 N-1 对应“逐步淘汰”的过滤器。
    private readonly BlockedBloomFilter[] _filters;

    public AnchoredGrainsFilter(int capacity, double fpRate, int generations)
    {
        if (generations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(generations), "Must have at least 1 generation.");
        }

        _filters = new BlockedBloomFilter[generations];

        for (var i = 0; i < generations; i++)
        {
            _filters[i] = new BlockedBloomFilter(capacity, fpRate);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(GrainId id) => _filters[0].Add(id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(GrainId id)
    {
        for (var i = 0; i < _filters.Length; i++)
        {
            if (_filters[i].Contains(id))
            {
                return true;
            }
        }

        return false;
    }

    public void Rotate()
    {
        // 通过轮换内部过滤器来实现类似滑动窗口的效果。
        // 原先的活动过滤器（索引 0）向后移动，在接下来的 n-1 轮中仍可被查询，之后若未重新加入则被淘汰。
        // 这样确保“冷”Grain 最终被移除，而“热”Grain 仍保持锚定。

        var length = _filters.Length;

        // 获取最旧的过滤器并重置，使其成为新的活动过滤器。
        var nextActive = _filters[length - 1];
        nextActive.Reset();

        // 将所有现有代的索引向后移动一位。从后向前遍历，
        // 避免覆盖尚未移动的元素。

        for (var i = length - 1; i > 0; i--)
        {
            _filters[i] = _filters[i - 1];
        }

        // 将刚重置的过滤器放到最前面。
        _filters[0] = nextActive;
    }

    public void Reset()
    {
        var filters = _filters;

        for (var i = 0; i < filters.Length; i++)
        {
            filters[i].Reset();
        }
    }
}
