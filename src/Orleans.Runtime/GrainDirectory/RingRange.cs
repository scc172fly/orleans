using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Orleans.Runtime.GrainDirectory;

/// <summary>
/// 表示环上由零个或多个值组成的连续范围。
/// </summary>
[GenerateSerializer, Immutable, Alias(nameof(RingRange))]
internal readonly struct RingRange : IEquatable<RingRange>, ISpanFormattable, IComparable<uint>
{
    // 范围的起点（不包含该点）。
    // 注意：_start == _end == 1 是表示完整范围的特殊值。
    [Id(0)]
    private readonly uint _start;

    // 范围的终点（包含该点）。
    // 注意：_start == _end == 1 是表示完整范围的特殊值。
    [Id(1)]
    private readonly uint _end;

    public bool IsEmpty => _start == _end && _start == 0;

    public bool IsFull => _start == _end && _start != 0;

    // 此范围是否包含 uint.MaxValue。
    internal bool IsWrapped => _start >= _end && _start != 0;

    public static RingRange Full { get; } = new(1, 1);

    public static RingRange Empty { get; } = new(0, 0);

    public uint Start => IsFull ? 0 : _start;

    public uint End => IsFull ? 0 : _end;

    private RingRange(uint start, uint end)
    {
        _start = start == end && start > 1 ? 1 : start;
        _end = start == end && start > 1 ? 1 : end;
    }

    // 仅供内部使用。
    internal static RingRange Create(uint start, uint end) => new(start, end);

    /// <summary>
    /// 创建表示单个点的范围。
    /// </summary>
    /// <param name="point">要包含在范围中的点。</param>
    /// <returns>仅包含 <paramref name="point"/> 的范围。</returns>
    public static RingRange FromPoint(uint point) => new(unchecked(point - 1), point);

    /// <summary>
    /// 获取范围覆盖的点数，取值从零到完整环的 2 的 32 次方。
    /// </summary>
    public ulong Size
    {
        get
        {
            if (_start == _end)
            {
                // 空范围。
                if (_start == 0) return 0;

                // 完整范围。
                return (ulong)uint.MaxValue + 1;
            }

            // 普通范围。
            if (_end > _start) return _end - _start;

            // 环绕范围。
            return (ulong)uint.MaxValue - _start + _end + 1;
        }
    }

    public int CompareTo(uint point)
    {
        if (Contains(point))
        {
            return 0;
        }

        var start = Start;
        if (IsWrapped)
        {
            // Start > End（环绕情况）
            if (point <= start)
            {
                // 范围起点在 N 之后（range > N）。
                return -1;
            }

            // n > _end
            // 范围起点和终点都在 N 之前（range < N）。
            return 1;
        }

        if (point <= start)
        {
            // 范围起点在 N 之后（range > N）。
            return 1;
        }

        // n > _end
        // 范围起点和终点都在 N 之前（range < N）。
        return -1;
    }

    /// <summary>
    /// 检查 n 是否属于 (Start, End]，同时考虑范围位于环上。
    /// </summary>
    /// <returns>如果 n 属于 (Start, End]，则为 true；否则为 false。</returns>
    internal bool Contains(GrainId grainId) => Contains(grainId.GetUniformHashCode());

    /// <summary>
    /// 检查 n 是否属于 (Start, End]，同时考虑范围位于环上。
    /// </summary>
    /// <param name="point"></param>
    /// <returns>如果 n 属于 (Start, End]，则为 true；否则为 false。</returns>
    public bool Contains(uint point)
    {
        if (IsEmpty)
        {
            return false;
        }

        var num = point;
        if (Start < End)
        {
            return num > Start && num <= End;
        }

        // Start > End
        return num > Start || num <= End;
    }

    public float SizePercent => Size * (100.0f / Full.Size);

    public bool Equals(RingRange other) => _start == other._start && _end == other._end;

    public override bool Equals(object? obj) => obj is RingRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_start, _end);

    public override string ToString() => $"{this}";

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        return IsEmpty
            ? destination.TryWrite($"(0, 0) 0.00%", out charsWritten)
            : IsFull
                ? destination.TryWrite($"(0, 0] (100.00%)", out charsWritten)
                : destination.TryWrite($"(0x{Start:X8}, 0x{End:X8}] ({SizePercent:0.00}%)", out charsWritten);
    }

    public bool Intersects(RingRange other) => !IsEmpty && !other.IsEmpty && (Equals(other) || Contains(other.End) || other.Contains(End));

    internal RingRange Complement()
    {
        if (IsEmpty)
        {
            return Full;
        }

        if (IsFull)
        {
            return Empty;
        }

        return new RingRange(End, Start);
    }

    internal IEnumerable<RingRange> Intersections(RingRange other)
    {
        if (!Intersects(other))
        {
            // 无交集。
            yield break;
        }

        if (IsFull)
        {
            // 只有一个交集，即另一个范围。
            yield return other;
        }
        else if (other.IsFull)
        {
            yield return this;
        }
        else if (IsWrapped && other.IsWrapped)
        {
            yield return new RingRange(Math.Max(Start, other.Start), Math.Min(End, other.End));

            if (Start < other.Start)
            {
                if (Start < other.End)
                {
                    yield return new RingRange(Start, other.End);
                }
            }
            else if (other.Start < Start && other.Start < End)
            {
                yield return new RingRange(other.Start, End);
            }
        }
        else if (IsWrapped ^ other.IsWrapped)
        {
            var wrapped = IsWrapped ? this : other;
            var normal = IsWrapped ? other : this;
            var (normalStart, normalEnd) = (normal.Start, normal.End);
            var (wrappedStart, wrappedEnd) = (wrapped.Start, wrapped.End);

            // 普通范围与环绕范围之间可能存在两个交集。
            //         low         high
            // ...---NB====WE----WB====NE----...

            // 低值侧的交集。
            if (wrappedEnd > normalStart)
            {
                // ---NB====WE---
                yield return new RingRange(normalStart, Math.Min(normalEnd, wrappedEnd));
            }

            // 高值侧的交集。
            if (wrappedStart < normalEnd)
            {
                // ---WB====NE---
                yield return new RingRange(Math.Max(normalStart, wrappedStart), normalEnd);
            }
        }
        else
        {
            yield return new RingRange(Math.Max(Start, other.Start), Math.Min(End, other.End));
        }
    }

    // 获取集合差：位于当前范围内但不位于 other 范围内的子范围。
    internal IEnumerable<RingRange> Difference(RingRange other)
    {
        // 新增部分为当前范围与旧范围的补集之间的交集。
        foreach (var addition in Intersections(other.Complement()))
        {
            Debug.Assert(!addition.Intersects(other));
            Debug.Assert(addition.Intersects(this));
            yield return addition;
        }
    }

    public static bool operator ==(RingRange left, RingRange right) => left.Equals(right);

    public static bool operator !=(RingRange left, RingRange right) => !(left == right);
}
