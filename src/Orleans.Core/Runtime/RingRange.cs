using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示虚拟环上的一个范围或一组范围，其中环上的各个点使用 <see cref="uint"/> 值标识。
    /// </summary>
    public interface IRingRange
    {
        /// <summary>
        /// 返回指示 <paramref name="value"/> 是否位于此环范围内的值。
        /// </summary>
        /// <param name="value">
        /// 要检查的值。
        /// </param>
        /// <returns><see langword="true"/> 表示提醒器位于本节点的负责范围内，否则为 <see langword="false"/>。</returns>
        bool InRange(uint value);

        /// <summary>
        /// 返回指示 <paramref name="grainId"/> 是否位于此环范围内的值。
        /// </summary>
        /// <param name="grainId">要检查的值。</param>
        /// <returns><see langword="true"/> 表示提醒器位于本节点的负责范围内，否则为 <see langword="false"/>。</returns>
        public sealed bool InRange(GrainId grainId) => InRange(grainId.GetUniformHashCode());
    }

    // 仅供不同范围实现使用的内部接口。
    internal interface IRingRangeInternal : IRingRange
    {
        double RangePercentage();
    }

    /// <summary>
    /// 表示虚拟环上的单个连续范围，其中环上的各个点使用 <see cref="uint"/> 值标识。
    /// </summary>
    /// <seealso cref="Orleans.Runtime.IRingRange" />
    public interface ISingleRange : IRingRange
    {
        /// <summary>
        /// 获取范围的排他下界。
        /// </summary>
        uint Begin { get; }

        /// <summary>
        /// 获取范围的包含性上界。
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
        /// 不包含边界值。
        /// </summary>
        public uint Begin { get { return begin; } }

        /// <summary>
        /// 包含边界值。
        /// </summary>
        public uint End { get { return end; } }

        public SingleRange(uint begin, uint end)
        {
            this.begin = begin;
            this.end = end;
        }

        /// <summary>
        /// 检查 n 是否属于 (Begin, End]，同时考虑范围处于环上的情况。
        /// </summary>
        /// <param name="n"></param>
        /// <returns>n 位于 (Begin, End] 内时返回 true，否则返回 false。</returns>
        public bool InRange(uint n)
        {
            uint num = n;
            if (begin < end)
            {
                return num > begin && num <= end;
            }
            // Begin > End
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

        // 另一个范围的起点位于此范围内部，根据它的终点位置合并范围。
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
    /// 用于创建 <see cref="IRingRange" /> 值的实用工具类。
    /// </summary>
    public static class RangeFactory
    {
        /// <summary>
        /// 环的大小。
        /// </summary>
        public const long RING_SIZE = (long)uint.MaxValue + 1;

        /// <summary>
        /// 表示空范围。
        /// </summary>
        private static readonly GeneralMultiRange EmptyRange = new(new());
        /// <summary>
        /// 表示完整范围。
        /// </summary>
        internal static readonly SingleRange FullRange = new(0, 0);

        /// <summary>
        /// 创建完整范围。
        /// </summary>
        /// <returns>IRingRange。</returns>
        public static IRingRange CreateFullRange() => FullRange;

        /// <summary>
        /// 创建新的 <see cref="IRingRange"/>，表示从排他下界 <paramref name="begin"/> 到包含性上界 <paramref name="end"/> 之间的值。
        /// </summary>
        /// <param name="begin">不包含的下界。</param>
        /// <param name="end">包含的上界。</param>
        /// <returns>一个新的 <see cref="IRingRange"/>，表示从排他下界 <paramref name="begin"/> 到包含性上界 <paramref name="end"/> 之间的值。</returns>
        public static IRingRange CreateRange(uint begin, uint end) => new SingleRange(begin, end);

        /// <summary>
        /// 创建新的 <see cref="IRingRange"/>，表示所有提供范围的并集。
        /// </summary>
        /// <param name="inRanges">各个范围。</param>
        /// <returns>一个新的 <see cref="IRingRange"/>，表示所有提供范围的并集。</returns>
        public static IRingRange CreateRange(List<IRingRange> inRanges) => inRanges.Count switch
        {
            0 => EmptyRange,
            1 => inRanges[0],
            _ => GeneralMultiRange.Create(inRanges)
        };

        /// <summary>
        /// 将提供的范围等分为子范围，并返回其中一个子范围。
        /// </summary>
        /// <param name="range">范围。</param>
        /// <param name="numSubRanges">子范围的数量。</param>
        /// <param name="mySubRangeIndex">要返回的子范围的索引。</param>
        /// <returns>指定的子范围。</returns>
        internal static IRingRange GetEquallyDividedSubRange(IRingRange range, int numSubRanges, int mySubRangeIndex)
            => EquallyDividedMultiRange.GetEquallyDividedSubRange(range, numSubRanges, mySubRangeIndex);

        /// <summary>
        /// 获取所提供范围表示的各个连续子范围。
        /// </summary>
        /// <param name="range">范围。</param>
        /// <returns>所提供范围表示的各个连续子范围。</returns>
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
                // (Begin, End]
                uint end = unchecked(start + portion);
                // 此处故意允许溢出，以得到正确的结果。
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

        // 将一个范围等分为 numSubRanges 个范围，并返回 mySubRangeIndex 处的子范围。
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
                            // 取多范围中的每个单独范围，将它们各自等分为子范围。
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
