using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Orleans.Runtime
{
    /// <summary>
    /// 代表一个虚拟环周围的幅度或一组范围,其中圆环上的点用 <see cref="uint"/> 值来识别。
    /// </summary>
    public interface IRingRange
    {
        /// <summary>
        /// 返回一个数值,表示 <paramref name="value"/> 是否在这个环范围内。
        /// </summary>
        /// <param name="value">
        /// 检查值 。
        /// </param>
        /// <returns><see langword="true"/> 如果教诲是分量在我们的应尽的责任范围内, <see langword="false"/> 否则。</returns>
        bool InRange(uint value);

        /// <summary>
        /// 返回一个数值,表示 <paramref name="grainId"/> 是否在这个环范围内。
        /// </summary>
        /// <param name="grainId">要检查的值。</param>
        /// <returns><see langword="true"/> 如果教诲是分量在我们的应尽的责任范围内, <see langword="false"/> 否则。</returns>
        public sealed bool InRange(GrainId grainId) => InRange(grainId.GetUniformHashCode());
    }

    // 这是只供不同范围的执行使用的内部接口。
    internal interface IRingRangeInternal : IRingRange
    {
        double RangePercentage();
    }

    /// <summary>
    /// 代表一个单一的毗连区域,围绕虚拟环圈,用 <see cref="uint"/> 值来识别环上各点。
    /// </summary>
    /// <seealso cref="Orleans.Runtime.IRingRange" />
    public interface ISingleRange : IRingRange
    {
        /// <summary>
        /// 获得范围范围内的排他性下限。
        /// </summary>
        uint Begin { get; }

        /// <summary>
        /// 获得范围上方的包容性。
        /// </summary>
        uint End { get; }
    }

    [Serializable, GenerateSerializer, Immutable]
    internal sealed class SingleRange : IRingRangeInternal, IEquatable<SingleRange>, ISingleRange, ISpanFormattable
    {
        [Id(0)]
        private readonly uint begin;

        [Id(1)]
        private readonly uint end;

        /// <summary>
        /// 专 专
        /// </summary>
        public uint Begin { get { return begin; } }

        /// <summary>
        /// 包容性
        /// </summary>
        public uint End { get { return end; } }

        public SingleRange(uint begin, uint end)
        {
            this.begin = begin;
            this.end = end;
        }

        /// <summary>
        /// n 的选中值是( begin, end) 的元素,同时记住区域在环上
        /// </summary>
        /// <param name="n"></param>
        /// <returns>真实的 n 在( begin, end) 中, 否则是假</returns>
        public bool InRange(uint n)
        {
            uint num = n;
            if (begin < end)
            {
                return num > begin && num <= end;
            }
            // 开始 > 结束
            return num > begin || num <= end;
        }

        public long RangeSize()
        {
            if (begin < end)
            {
                return end - begin;
            }
            return RangeFactory.RING_SIZE - (begin - end);
        }

        public double RangePercentage() => RangeSize() * (100.0 / RangeFactory.RING_SIZE);

        public bool Equals(SingleRange? other) => other != null && begin == other.begin && end == other.end;

        public override bool Equals(object? obj) => Equals(obj as SingleRange);

        public override int GetHashCode() => HashCode.Combine(GetType(), begin, end);

        public override string ToString() => begin == 0 && end == 0 ? "<(0 0], Size=x100000000, %Ring=100%>" : $"{this}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            return begin == 0 && end == 0
                ? destination.TryWrite($"<(0 0], Size=x100000000, %Ring=100%>", out charsWritten)
                : destination.TryWrite($"<(x{begin:X8} x{end:X8}], Size=x{RangeSize():X8}, %Ring={RangePercentage():0.000}%>", out charsWritten);
        }

        internal bool Overlaps(SingleRange other) => Equals(other) || InRange(other.begin) || other.InRange(begin);

        internal SingleRange Merge(SingleRange other)
        {
            if ((begin | end) == 0 || (other.begin | other.end) == 0)
            {
                return RangeFactory.FullRange;
            }

            if (Equals(other))
            {
                return this;
            }

            if (InRange(other.begin))
            {
                return MergeEnds(other);
            }

            if (other.InRange(begin))
            {
                return other.MergeEnds(this);
            }

            throw new InvalidOperationException("Ranges don't overlap");
        }

        // 其它区域,则根据它结束的位置合并
        private SingleRange MergeEnds(SingleRange other)
        {
            if (begin == other.end)
            {
                return RangeFactory.FullRange;
            }

            if (!InRange(other.end))
            {
                return new SingleRange(begin, other.end);
            }

            if (other.InRange(begin))
            {
                return RangeFactory.FullRange;
            }

            return this;
        }
    }

    /// <summary>
    /// 创建 <see cref="IRingRange" /> 值的工具类。
    /// </summary>
    public static class RangeFactory
    {
        /// <summary>
        /// 戒指的大小。
        /// </summary>
        public const long RING_SIZE = (long)uint.MaxValue + 1;

        /// <summary>
        /// 表示空区。
        /// </summary>
        private static readonly GeneralMultiRange EmptyRange = new(new());
        /// <summary>
        /// 代表全套服务。
        /// </summary>
        internal static readonly SingleRange FullRange = new(0, 0);

        /// <summary>
        /// 创建全域。
        /// </summary>
        /// <returns>IRingRange.</returns>
        public static IRingRange CreateFullRange() => FullRange;

        /// <summary>
        /// 创建一个新的 <see cref="IRingRange"/> ,代表排他性下限、 <paramref name="begin"/>和包容性上限、 <paramref name="end"/>之间的数值。
        /// </summary>
        /// <param name="begin">排他性下限约束</param>
        /// <param name="end">包含式上约束。</param>
        /// <returns>一个新的 <see cref="IRingRange"/> 表示排他性下限之间、 <paramref name="begin"/>和包容性的上约束, <paramref name="end"/>.</returns>
        public static IRingRange CreateRange(uint begin, uint end) => new SingleRange(begin, end);

        /// <summary>
        /// 创建一个新的 <see cref="IRingRange"/> ,代表所有提供范围的联盟。
        /// </summary>
        /// <param name="inRanges">范围。</param>
        /// <returns>一个新的 <see cref="IRingRange"/> 代表所有规定范围的联盟。</returns>
        public static IRingRange CreateRange(List<IRingRange> inRanges) => inRanges.Count switch
        {
            0 => EmptyRange,
            1 => inRanges[0],
            _ => GeneralMultiRange.Create(inRanges)
        };

        /// <summary>
        /// 从所提供的范围中创建平均分隔的子区域,并返回该范围的1个子区域。
        /// </summary>
        /// <param name="range">范围。</param>
        /// <param name="numSubRanges">子区域的数目。</param>
        /// <param name="mySubRangeIndex">返回的子序列索引。</param>
        /// <returns>所确定的子范围。</returns>
        internal static IRingRange GetEquallyDividedSubRange(IRingRange range, int numSubRanges, int mySubRangeIndex)
            => EquallyDividedMultiRange.GetEquallyDividedSubRange(range, numSubRanges, mySubRangeIndex);

        /// <summary>
        /// 获取以提供范围表示的毗连子区域。
        /// </summary>
        /// <param name="range">范围。</param>
        /// <returns>以规定范围表示的毗连子区域。</returns>
        public static IEnumerable<ISingleRange> GetSubRanges(IRingRange range) => range switch
        {
            ISingleRange single => new[] { single },
            GeneralMultiRange m => m.Ranges,
            _ => throw new NotSupportedException(),
        };
    }

    [Serializable, GenerateSerializer, Immutable]
    internal sealed class GeneralMultiRange : IRingRangeInternal, ISpanFormattable
    {
        [Id(0)]
        private readonly List<SingleRange> ranges;

        [Id(1)]
        private readonly long rangeSize;

        internal List<SingleRange> Ranges => ranges;

        internal GeneralMultiRange(List<SingleRange> ranges)
        {
            Debug.Assert(ranges.Count != 1);
            this.ranges = ranges;
            foreach (var r in ranges)
                rangeSize += r.RangeSize();
        }

        internal static IRingRange Create(List<IRingRange> inRanges)
        {
            var ranges = inRanges.ConvertAll(r => (SingleRange)r);
            return HasOverlaps() ? Compact() : new GeneralMultiRange(ranges);

            bool HasOverlaps()
            {
                var last = ranges[0];
                for (var i = 1; i < ranges.Count; i++)
                {
                    if (last.Overlaps(last = ranges[i])) return true;
                }

                return false;
            }

            IRingRange Compact()
            {
                var lastIdx = 0;
                var last = ranges[0];
                for (var i = 1; i < ranges.Count; i++)
                {
                    var r = ranges[i];
                    if (last.Overlaps(r)) ranges[lastIdx] = last = last.Merge(r);
                    else ranges[++lastIdx] = last = r;
                }
                if (lastIdx == 0) return last;
                ranges.RemoveRange(++lastIdx, ranges.Count - lastIdx);
                return new GeneralMultiRange(ranges);
            }
        }

        public bool InRange(uint n)
        {
            foreach (var s in ranges)
            {
                if (s.InRange(n)) return true;
            }
            return false;
        }

        public double RangePercentage() => rangeSize * (100.0 / RangeFactory.RING_SIZE);

        public override string ToString() => ranges.Count == 0 ? "Empty MultiRange" : $"{this}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            return ranges.Count == 0
                ? destination.TryWrite($"Empty MultiRange", out charsWritten)
                : destination.TryWrite($"<MultiRange: Size=x{rangeSize:X8}, %Ring={RangePercentage():0.000}%>", out charsWritten);
        }
    }

    internal static class EquallyDividedMultiRange
    {
        private static SingleRange GetEquallyDividedSubRange(SingleRange singleRange, int numSubRanges, int mySubRangeIndex)
        {
            var rangeSize = singleRange.RangeSize();
            uint portion = (uint)(rangeSize / numSubRanges);
            uint remainder = (uint)(rangeSize - portion * numSubRanges);
            uint start = singleRange.Begin;
            for (int i = 0; i < numSubRanges; i++)
            {
                // ( 开始, 结束)
                uint end = unchecked(start + portion);
                // 我要它故意溢出,这样会做对的
                if (remainder > 0)
                {
                    end++;
                    remainder--;
                }
                if (i == mySubRangeIndex)
                    return new SingleRange(start, end);
                start = end; // nextStart
            }
            throw new ArgumentException("The subrange index must identify one of the divided ranges.", nameof(mySubRangeIndex));
        }

        // 需要一个范围,然后将其分为 numSubRanges 等同范围,并在 mySubRangeIndex返回子范围。
        public static IRingRange GetEquallyDividedSubRange(IRingRange range, int numSubRanges, int mySubRangeIndex)
        {
            if (numSubRanges <= 0) throw new ArgumentOutOfRangeException(nameof(numSubRanges));
            if ((uint)mySubRangeIndex >= (uint)numSubRanges) throw new ArgumentOutOfRangeException(nameof(mySubRangeIndex));

            if (numSubRanges == 1) return range;

            switch (range)
            {
                case SingleRange singleRange:
                    return GetEquallyDividedSubRange(singleRange, numSubRanges, mySubRangeIndex);

                case GeneralMultiRange multiRange:
                    switch (multiRange.Ranges.Count)
                    {
                        case 0: return multiRange;
                        default:
                            // 将每个单项幅度在多个范围内取出,并将每个范围分成相同的子幅度。
                            var singlesForThisIndex = new List<SingleRange>(multiRange.Ranges.Count);
                            foreach (var singleRange in multiRange.Ranges)
                                singlesForThisIndex.Add(GetEquallyDividedSubRange(singleRange, numSubRanges, mySubRangeIndex));
                            return new GeneralMultiRange(singlesForThisIndex);
                    }

                default: throw new ArgumentOutOfRangeException(nameof(range));
            }
        }
    }
}
