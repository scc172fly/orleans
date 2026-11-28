using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Orleans.Runtime.Utilities;

namespace Orleans.Runtime.GrainDirectory;

// 互不重叠的范围组成的只读有序集合。
[GenerateSerializer, Immutable, Alias(nameof(RingRangeCollection))]
internal readonly struct RingRangeCollection : IEquatable<RingRangeCollection>, ISpanFormattable, IEnumerable<RingRange>
{
    public RingRangeCollection(ImmutableArray<RingRange> ranges)
    {
#if DEBUG
        Debug.Assert(!ranges.IsDefault);

        // 范围必须按顺序排列，且彼此不得重叠。
        for (var i = 1; i < ranges.Length; i++)
        {
            var prev = ranges[i - 1];
            var curr = ranges[i];
            Debug.Assert(!curr.IsEmpty);
            Debug.Assert(!prev.Intersects(curr));
            Debug.Assert(curr.Start >= prev.Start);
        }

        if (ranges.Length > 1)
        {
            Debug.Assert(!ranges[0].Intersects(ranges[^1]));
        }
#endif
        Ranges = ranges;
    }

    public static RingRangeCollection Create<TCollection>(TCollection ranges) where TCollection : ICollection<RingRange>
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var result = ImmutableArray.CreateBuilder<RingRange>(ranges.Count);
        foreach (var range in ranges)
        {
            if (range.IsEmpty)
            {
                continue;
            }

            result.AddRange(range);
        }

        result.Sort((l, r) => l.Start.CompareTo(r.Start));
        return new(result.ToImmutable());
    }

    public static RingRangeCollection Empty { get; } = new([]);

    [Id(0)]
    public ImmutableArray<RingRange> Ranges { get; }

    public bool IsDefault => Ranges.IsDefault;

    public bool IsEmpty => Ranges.IsDefaultOrEmpty || Ranges.All(static r => r.IsEmpty);

    public bool IsFull => Size == RingRange.Full.Size;

    /// <summary>
    /// 获取互不重叠的范围所覆盖的点的总数，取值从零到 2 的 32 次方。
    /// </summary>
    public ulong Size
    {
        get
        {
            if (Ranges.IsDefaultOrEmpty)
            {
                return 0;
            }

            ulong sum = 0;
            foreach (var range in Ranges)
            {
                sum += range.Size;
            }

            return sum;
        }
    }

    public float SizePercent => Size * (100.0f / RingRange.Full.Size);

    public bool Contains(GrainId grainId) => Contains(grainId.GetUniformHashCode());

    public bool Contains(uint value)
    {
        if (Ranges.IsDefaultOrEmpty)
        {
            return false;
        }

        return SearchAlgorithms.RingRangeBinarySearch(
            Ranges.Length,
            Ranges,
            static (ranges, index) => ranges[index],
            value) >= 0;
    }

    public bool Intersects(RingRange other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return false;
        }

        if (Contains(other.End))
        {
            return true;
        }

        foreach (var range in Ranges)
        {
            if (other.Contains(range.End))
            {
                return true;
            }
        }

        return false;
    }

    public bool Intersects(RingRangeCollection other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return false;
        }

        foreach (var range in Ranges)
        {
            if (other.Contains(range.End))
            {
                return true;
            }
        }

        foreach (var otherRange in other.Ranges)
        {
            if (Contains(otherRange.End))
            {
                return true;
            }
        }

        return false;
    }

    public RingRangeCollection Difference(RingRangeCollection previous)
    {
        // left 中的范围不得相互重叠。
        // right 中的范围不得相互重叠。
        // left 和 right 中对应范围的起点相同。
        // Ranges 和 previous.Ranges 中的范围数均为零或已配置的范围数量，
        // 即如果两个集合都不为空，则它们的范围数量相同。
        var currentLength = Ranges.IsDefault ? 0 : Ranges.Length;
        var previousLength = previous.Ranges.IsDefault ? 0 : previous.Ranges.Length;

        if (currentLength == previousLength)
        {
            var result = ImmutableArray.CreateBuilder<RingRange>(currentLength);
            for (var i = 0; i < currentLength; i++)
            {
                var c = Ranges[i];
                var p = previous.Ranges[i];
                Debug.Assert(c.Start == p.Start);
                if (c.Size > p.Size)
                {
                    result.Add(RingRange.Create(p.End, c.End));
                }
            }

            // 如果最后一个范围是环绕范围，而其扩展出来的新增部分并不环绕，则将该新增部分移到最前面。
            // 这样可以保持排序顺序。
            if (result.Count > 1 && result[^1].Start < result[^2].Start)
            {
                var last = result[^1];
                result.RemoveAt(result.Count - 1);
                result.Insert(0, last);
            }

            return new(result.ToImmutable());
        }
        else
        {
            if (currentLength > previousLength)
            {
                Debug.Assert(previousLength == 0);
                return this;
            }
            else
            {
                Debug.Assert(currentLength == 0 || previousLength == 0);
                return Empty;
            }
        }
    }

    public bool Equals(RingRangeCollection other)
    {
        if (IsEmpty && other.IsEmpty)
        {
            return true;
        }

        if (IsEmpty ^ other.IsEmpty)
        {
            return false;
        }

        return Ranges.SequenceEqual(other.Ranges);
    }

    public static bool operator ==(RingRangeCollection left, RingRangeCollection right) => left.Equals(right);

    public static bool operator !=(RingRangeCollection left, RingRangeCollection right) => !(left == right);

    public override bool Equals(object? obj) => obj is RingRangeCollection range && Equals(range);

    public override int GetHashCode()
    {
        if (IsEmpty)
        {
            return 0;
        }

        var result = new HashCode();
        result.Add(Ranges.Length);
        foreach (var range in Ranges)
        {
            result.Add(range);
        }

        return result.ToHashCode();
    }

    public ImmutableArray<RingRange>.Enumerator GetEnumerator() => Ranges.IsDefault ? ImmutableArray<RingRange>.Empty.GetEnumerator() : Ranges.GetEnumerator();

    public override string ToString() => $"{this}";
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => destination.TryWrite($"({(Ranges.IsDefault ? 0 : Ranges.Length)} subranges), {SizePercent:0.00}%", out charsWritten);
    IEnumerator<RingRange> IEnumerable<RingRange>.GetEnumerator() => Ranges.IsDefault ? Enumerable.Empty<RingRange>().GetEnumerator() : ((IEnumerable<RingRange>)Ranges).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<RingRange>)this).GetEnumerator();
}
