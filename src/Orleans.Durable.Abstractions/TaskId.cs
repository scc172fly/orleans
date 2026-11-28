using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>标识层次结构中的持久化任务。</summary>
/// <remarks>
/// 每个段均为非空且格式正确的 UTF-16 文本，因此标识符的各种编码（如 UTF-8 和 JSON）
/// 都能进行无损往返转换。System.Text.Json 将标识符序列化为经过转义的层次路径，使用的转换器为
/// <see cref="TaskIdJsonConverter"/>.
/// </remarks>
[JsonConverter(typeof(TaskIdJsonConverter))]
[Alias("Orleans.Durable.Tasks.TaskId")]
public readonly struct TaskId : IEquatable<TaskId>, IParsable<TaskId>, ISpanParsable<TaskId>, ISpanFormattable
{
    private readonly HierarchicalKey? _key;

    private TaskId(HierarchicalKey key) => _key = key;

    /// <summary>获取空标识符。</summary>
    public static TaskId None => default;

    /// <summary>获取一个值，指示此标识符是否为空。</summary>
    public bool IsDefault => _key is null;

    /// <summary>使用一个逻辑段创建根标识符。</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="rootSegment"/> 为 <see langword="null"/>、空字符串，或包含未配对的 UTF-16 代理项。
    /// </exception>
    public static TaskId CreateRoot(string rootSegment) => new(HierarchicalKey.FromSegment(rootSegment));

    /// <summary>通过追加一个逻辑段创建子标识符。</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="segment"/> 为 <see langword="null"/>、空字符串，或包含未配对的 UTF-16 代理项。
    /// </exception>
    public TaskId Child(string segment)
    {
        if (_key is null)
        {
            throw new InvalidOperationException("A child identifier requires a non-empty parent.");
        }

        return new(_key.AppendSegment(segment));
    }

    /// <summary>获取父标识符；若为根标识符，则返回 <see cref="None"/>。</summary>
    public TaskId Parent() => _key?.Parent is { } parent ? new(parent) : None;

    /// <summary>获取此标识符未经转义的最后一个段。</summary>
    internal string LastSegment => _key?.LastSegment ?? throw new InvalidOperationException("An empty task identifier has no segments.");

    /// <summary>
    /// 返回此非空标识符是否是以下标识符的祖先或与其相等：
    /// <paramref name="other"/>。<see cref="None"/> 位于层次结构之外，因此返回
    /// <see langword="false"/>.
    /// </summary>
    public bool IsAncestorOf(TaskId other) => _key?.IsAncestorOf(other._key) is true;

    /// <summary>
    /// 返回此非空标识符是否是以下标识符的后代或与其相等：
    /// <paramref name="other"/>。<see cref="None"/> 位于层次结构之外，因此返回
    /// <see langword="false"/>.
    /// </summary>
    public bool IsDescendantOf(TaskId other) => other.IsAncestorOf(this);

    /// <summary>返回此标识符是否是 <paramref name="other"/> 的父标识符。</summary>
    public bool IsParentOf(TaskId other) => _key?.IsParentOf(other._key) is true;

    /// <summary>返回此标识符是否是 <paramref name="other"/> 的子标识符。</summary>
    public bool IsChildOf(TaskId other) => other.IsParentOf(this);

    /// <summary>解析已转义的层次结构路径。</summary>
    public static TaskId Parse(string s) => Parse(s, provider: null);

    /// <summary>解析已转义的层次结构路径。</summary>
    public static TaskId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Length == 0 ? None : new(HierarchicalKey.Parse(s));
    }

    /// <summary>尝试解析已转义的层次结构路径。</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out TaskId result)
        => TryParse(s, provider: null, out result);

    /// <summary>尝试解析已转义的层次结构路径。</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TaskId result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        if (s.Length == 0)
        {
            result = None;
            return true;
        }

        if (HierarchicalKey.TryParse(s, out var key))
        {
            result = new(key);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>解析已转义的层次结构路径。</summary>
    public static TaskId Parse(ReadOnlySpan<char> s) => Parse(s, provider: null);

    /// <summary>解析已转义的层次结构路径。</summary>
    public static TaskId Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => s.IsEmpty ? None : new(HierarchicalKey.Parse(s));

    /// <summary>尝试解析已转义的层次结构路径。</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out TaskId result)
        => TryParse(s, provider: null, out result);

    /// <summary>尝试解析已转义的层次结构路径。</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TaskId result)
    {
        if (s.IsEmpty)
        {
            result = None;
            return true;
        }

        if (HierarchicalKey.TryParse(s, out var key))
        {
            result = new(key);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>将此标识符格式化为经过转义的层次路径。</summary>
    public override string ToString() => _key?.ToString() ?? string.Empty;

    /// <summary>将此标识符格式化为经过转义的层次路径。</summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>尝试将此标识符格式化并写入 <paramref name="destination"/>。</summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (_key is null)
        {
            charsWritten = 0;
            return true;
        }

        return _key.TryFormat(destination, out charsWritten);
    }

    /// <inheritdoc />
    public bool Equals(TaskId other) => Equals(_key, other._key);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TaskId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _key?.GetHashCode() ?? 0;

    /// <summary>返回两个标识符是否相等。</summary>
    public static bool operator ==(TaskId left, TaskId right) => left.Equals(right);

    /// <summary>返回两个标识符是否不同。</summary>
    public static bool operator !=(TaskId left, TaskId right) => !left.Equals(right);

    /// <summary>将标识符格式化为经过转义的层次路径。</summary>
    public static explicit operator string(TaskId value) => value.ToString();

    /// <summary>解析已转义的层次结构路径。</summary>
    [SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "TaskId implements IParsable and exposes Parse and TryParse as the named alternatives.")]
    public static explicit operator TaskId(string value) => Parse(value);
}

internal sealed class HierarchicalKey : IEquatable<HierarchicalKey>
{
    private const char Separator = '/';
    private const char Escape = '\\';
    private readonly string[] _segments;

    private HierarchicalKey(string[] segments) => _segments = segments;

    public HierarchicalKey? Parent => _segments.Length > 1 ? new(_segments[..^1]) : null;

    public string LastSegment => _segments[^1];

    public static HierarchicalKey FromSegment(string segment)
    {
        ValidateSegment(segment);
        return new([segment]);
    }

    public HierarchicalKey AppendSegment(string segment)
    {
        ValidateSegment(segment);
        return new([.. _segments, segment]);
    }

    /// <summary>
    /// 验证一个段：它必须是非空且格式正确的 UTF-16 文本，这样标识符的每种 UTF-8 或 JSON 编码
    /// 都能进行无损往返转换，而不是将未配对代理项替换为 U+FFFD，进而合并不同的标识符。
    /// </summary>
    private static void ValidateSegment(string segment, [CallerArgumentExpression(nameof(segment))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment, paramName);
        if (!IsWellFormedUtf16(segment))
        {
            throw new ArgumentException("A task identifier segment must be well-formed UTF-16 text, without unpaired surrogates.", paramName);
        }
    }

    /// <summary>返回 <paramref name="value"/> 中每个代理项是否都属于一个高位和低位代理项组成的配对。</summary>
    private static bool IsWellFormedUtf16(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsAncestorOf(HierarchicalKey? other)
        => other is not null
            && _segments.Length <= other._segments.Length
            && _segments.AsSpan().SequenceEqual(other._segments.AsSpan(0, _segments.Length));

    public bool IsParentOf(HierarchicalKey? other)
        => other is not null && other._segments.Length == _segments.Length + 1 && IsAncestorOf(other);

    public static HierarchicalKey Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out var result)
            ? result
            : throw new FormatException("The task identifier is not a valid escaped hierarchical path.");
    }

    public static HierarchicalKey Parse(ReadOnlySpan<char> value)
        => TryParse(value, out var result)
            ? result
            : throw new FormatException("The task identifier is not a valid escaped hierarchical path.");

    public static bool TryParse(
        [NotNullWhen(true)] string? value,
        [NotNullWhen(true)] out HierarchicalKey? result)
        => TryParse(value.AsSpan(), out result);

    public static bool TryParse(ReadOnlySpan<char> value, [NotNullWhen(true)] out HierarchicalKey? result)
    {
        result = null;

        // 转义符是 ASCII 字符，因此当且仅当路径中的每个段均为格式正确的 UTF-16 文本时，该路径才是格式正确的 UTF-16 文本。
        if (value.IsEmpty || !IsWellFormedUtf16(value))
        {
            return false;
        }

        var segments = new List<string>();
        var segment = new System.Text.StringBuilder();
        var escaped = false;
        foreach (var character in value)
        {
            if (escaped)
            {
                if (character is not Separator and not Escape)
                {
                    return false;
                }

                segment.Append(character);
                escaped = false;
            }
            else if (character == Escape)
            {
                escaped = true;
            }
            else if (character == Separator)
            {
                if (segment.Length == 0)
                {
                    return false;
                }

                segments.Add(segment.ToString());
                segment.Clear();
            }
            else
            {
                segment.Append(character);
            }
        }

        if (escaped || segment.Length == 0)
        {
            return false;
        }

        segments.Add(segment.ToString());
        result = new(segments.ToArray());
        return true;
    }

    public override string ToString()
    {
        var length = _segments.Length - 1;
        foreach (var segment in _segments)
        {
            length += segment.Length;
            foreach (var character in segment)
            {
                if (character is Separator or Escape)
                {
                    length++;
                }
            }
        }

        return string.Create(length, this, static (destination, key) =>
        {
            var formatted = key.TryFormat(destination, out var charsWritten);
            Debug.Assert(formatted && charsWritten == destination.Length, "The escaped length matches the formatted path.");
        });
    }

    public bool TryFormat(Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        for (var segmentIndex = 0; segmentIndex < _segments.Length; segmentIndex++)
        {
            if (segmentIndex > 0)
            {
                if (charsWritten >= destination.Length)
                {
                    charsWritten = 0;
                    return false;
                }

                destination[charsWritten++] = Separator;
            }

            foreach (var character in _segments[segmentIndex])
            {
                if (character is Separator or Escape)
                {
                    if (charsWritten >= destination.Length)
                    {
                        charsWritten = 0;
                        return false;
                    }

                    destination[charsWritten++] = Escape;
                }

                if (charsWritten >= destination.Length)
                {
                    charsWritten = 0;
                    return false;
                }

                destination[charsWritten++] = character;
            }
        }

        return true;
    }

    public bool Equals(HierarchicalKey? other)
        => other is not null && _segments.AsSpan().SequenceEqual(other._segments);

    public override bool Equals(object? obj) => obj is HierarchicalKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in _segments)
        {
            hash.Add(segment, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
