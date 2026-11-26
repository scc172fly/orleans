using System.Diagnostics;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Placement.Repartitioning;

/// <summary>
/// 封杀的过滤器执行的调版。
/// </summary>
/// <remarks><i>
/// <para>这是BBFF的调制版,以满足规定的按自由币兑换率。
/// 调试需要很多时间,所以这个过滤器可以在[0.1% - 1%]的热潮中接受节能汇率。
/// 使用范围的任何值,只要精确度为任何值,均支持该数值,因为通过多面回归法</para>,FP率会回退
/// <para>更多信息可从第3节读取: https://www.cs.amherst.edu/~ccmcgeoch/cs34/papers/cacheefficientbloomfilters-jea.pdf</para>
/// </i></remarks>
internal sealed class BlockedBloomFilter
{
    private const int BlockSize = 32; // 高值提高能产生更快的效益,但以高空间成本
    private const double Ln2Squared = 0.4804530139182014246671025263266649717305529515945455;
    private const double MinFpRate = 0.001; // 0.1%
    private const double MaxFpRate = 0.01;  // 1%

    private readonly int _blocks;
    private readonly int[] _filter;

    // 递减系数(通过多元回归得出),与 "fpRate" 相匹配,因为实际偏差与 "fpRate" 的下低显著偏差相当。
    // Eg,见 https://gist.github.com/ledjon-behluli/d339cbd54568ceb5464d3a947ac8f08e
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

    /// <param name="capacity">估计人口规模。</param>
    /// <param name="fpRate">内[<see cref="MinFpRate"/> - <see cref="MaxFpRate"/></param>
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
        var index = GetBlockIndex(hash, _blocks); // 旋转散列前获得索引的重要性

        hash ^= BitOperations.RotateLeft(hash, 32);

        // 我们用两个面罩将散列值的比特分布在过滤器中的多个位置
        var mask1 = ComputeMask1(hash);
        var mask2 = ComputeMask2(hash);

        // 我们把比特设置在两个街区之间,这样一个散列值的比特就会分布得更均匀。
        _filter[index] |= mask1;
        _filter[index + 1] |= mask2;
    }

    public bool Contains(GrainId id)
    {
        var hash = XxHash3.HashToUInt64(id.Key.AsSpan(), id.GetUniformHashCode());
        var index = GetBlockIndex(hash, _blocks); // 旋转散列前获得索引的重要性

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
    /// 设置与下顺序位数相对的 <paramref name="hash"/> 位数,而比数向右移动了6个位置
    /// </summary>
    /// <param name="hash">旋转的散列</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeMask1(ulong hash) => (1 << (int)hash) | (1 << ((int)hash >> 6));

    /// <summary>
    /// 设置 <paramref name="hash"/>的比特,而比特则向右移动12和18个位置
    /// </summary>
    /// <param name="hash">旋转的散列</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeMask2(ulong hash) => (1 << ((int)hash >> 12)) | (1 << ((int)hash >> 18));
}

/// <summary>
/// 一套已执行的 <see cref="BlockedBloomFilter"/>(s) 旋转固定 grains.
/// 如果 grain 是“热”它可能会在多个周期内存活下来,否则就会被淘汰。
/// </summary>
internal sealed class AnchoredGrainsFilter
{
    // 索引 0 是“ 激活” 过滤器 。
    // 指数1到N-1是“退休”过滤器。
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
        // 理想是旋转内部过滤器, 以实施某种滑动窗口 。
        // 上一个活动周期( 指数 0) 向下移动, 剩下的 n-1 循环“ 可检查” , 除非重新添加, 否则会消失 。
        // 这保证了"冷" grains 最终退学,而热"的 继续固定。

        var length = _filters.Length;

        // 我们抓住最老的过滤器 重置它,让它变成新的活动过滤器
        var nextActive = _filters[length - 1];
        nextActive.Reset();

        // 需要将所有今世后代都从1下移到1下,我们向后推
        // 所以我们不覆盖元素,我们还没有改变。

        for (var i = length - 1; i > 0; i--)
        {
            _filters[i] = _filters[i - 1];
        }

        // 新的重置过滤器在前面
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
