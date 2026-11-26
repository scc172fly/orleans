using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Orleans.Runtime.GrainDirectory;

/// <summary>
/// 代表环上连续值范围为零或以上的。
/// </summary>
[GenerateSerializer, Immutable, Alias(nameof(RingRange))]
internal readonly struct RingRange : IEquatable<RingRange>, ISpanFormattable, IComparable<uint>
{
    // 区域的唯一起点。
    //  注意,启动时间 _sart =_end = 1 用作代表整个范围的特别价值。
    [Id(0)]
    private readonly uint _start;

    // 范围包含的终点。
    //  注意,启动时间 _sart =_end = 1 用作代表整个范围的特别价值。
    [Id(1)]
    private readonly uint _end;

    public bool IsEmpty => _start == _end && _start == 0;

    public bool IsFull => _start == _end && _start != 0;

    // 范围是否包含 uint.MaxValue。
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
    /// 创建代表单一点的区域。
    /// </summary>
    /// <param name="point">范围包括的点。</param>
    /// <returns>一个范围仅包括 <paramref name="point"/>。</returns>
    public static RingRange FromPoint(uint point) => new(unchecked(point - 1), point);

    /// <summary>
    /// 获取范围覆盖的点数，从 0 到整个环的 2^32。
    /// </summary>
    public ulong Size
    {
        get
        {
            if (_start == _end)
            {
                // 空空
                if (_start == 0) return 0;

                // 完整
                return (ulong)uint.MaxValue + 1;
            }

            // 普通普通
            if (_end > _start) return _end - _start;

            // 包装包装
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
            // 开始 > 结束( 整件整件)
            if (point <= start)
            {
                // N(范围 > N)后开始的距离
                return -1;
            }

            // n > _end
            // 距离在 N 前开始结束( 范围 < N)
            return 1;
        }

        if (point <= start)
        {
            // N(范围 > N)后开始的距离
            return 1;
        }

        // n > _end
        // 距离在 N 前开始结束( 范围 < N)
        return -1;
    }

    /// <summary>
    /// 检查 n 是 (Start, End) 的元素是否为 n,同时记住区域在环上
    /// </summary>
    /// <returns>真实的, 如果 n在( 启动、 结束) 中, 否则是假</returns>
    internal bool Contains(GrainId grainId) => Contains(grainId.GetUniformHashCode());

    /// <summary>
    /// n 的选中勾中, n 是( Start, end) 的元素,同时记住区域在环上
    /// </summary>
    /// <param name="point"></param>
    /// <returns>真实的, 如果 n在( 启动、 结束) 中, 否则是假</returns>
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

        // 开始 > > 结束
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
            // 没有交叉点
            yield break;
        }

        if (IsFull)
        {
            // 一个交叉点,另一个范围
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

            // 可能有两个交叉点, 在正常和包装的距离之间。
            //         低
            // ...---NB====WE----WB====NE----...

            // 下侧交叉口
            if (wrappedEnd > normalStart)
            {
                // - 尼库瓦
                yield return new RingRange(normalStart, Math.Min(normalEnd, wrappedEnd));
            }

            // 中间交叉在高处。
            if (wrappedStart < normalEnd)
            {
                // - 博文尼... - 来
                yield return new RingRange(Math.Max(normalStart, wrappedStart), normalEnd);
            }
        }
        else
        {
            yield return new RingRange(Math.Max(Start, other.Start), Math.Min(End, other.End));
        }
    }

    // 获取设定的差数:在此范围内但不属于“其他”范围的子范围。
    internal IEnumerable<RingRange> Difference(RingRange other)
    {
        // 添加的值是此范围与前一范围的反向之间的交叉点。
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
