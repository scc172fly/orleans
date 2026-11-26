using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>确定一个等级制度内的一项持久任务。</summary>
/// <remarks>
/// 每个部分都是非空的、完善的 UTF-16 文本, 所以每个标识编码, 如 UTF-8 和 JSON,
/// System.Text.Json 将识别符号序列成其逃脱的等级路径,通过
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

    /// <summary>获得一个值, 显示该标识符是否为空。</summary>
    public bool IsDefault => _key is null;

    /// <summary>从一个逻辑段创建根标识符。</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="rootSegment"/> 是 <see langword="null"/>, 空的, 或含有未受影响的 UTF-16 替代方 。
    /// </exception>
    public static TaskId CreateRoot(string rootSegment) => new(HierarchicalKey.FromSegment(rootSegment));

    /// <summary>通过附加一个逻辑段创建子标识符。</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="segment"/> 是 <see langword="null"/>, 空的, 或含有未受影响的 UTF-16 替代方 。
    /// </exception>
    public TaskId Child(string segment)
    {
        if (_key is null)
        {
            throw new InvalidOperationException("A child identifier requires a non-empty parent.");
        }

        return new(_key.AppendSegment(segment));
    }

    /// <summary>获得树根的父标识符或 <see cref="None"/> 。</summary>
    public TaskId Parent() => _key?.Parent is { } parent ? new(parent) : None;

    /// <summary>获取此标识符的未覆盖末段。</summary>
    internal string LastSegment => _key?.LastSegment ?? throw new InvalidOperationException("An empty task identifier has no segments.");

    /// <summary>
    /// 返回是否返回该非空身份识别特征符是
    /// <paramref name="other"/>. <see cref="None"/> 不属于等级和返回的层次
    /// <see langword="false"/>.
    /// </summary>
    public bool IsAncestorOf(TaskId other) => _key?.IsAncestorOf(other._key) is true;

    /// <summary>
    /// 返回是否返回,不论该非空白标识符是
    /// <paramref name="other"/>. <see cref="None"/> 不属于等级和返回的层次
    /// <see langword="false"/>.
    /// </summary>
    public bool IsDescendantOf(TaskId other) => other.IsAncestorOf(this);

    /// <summary>返回是否该标识符是 <paramref name="other"/>的母。</summary>
    public bool IsParentOf(TaskId other) => _key?.IsParentOf(other._key) is true;

    /// <summary>返回是否该标识符是 <paramref name="other"/>的子。</summary>
    public bool IsChildOf(TaskId other) => other.IsParentOf(this);

    /// <summary>解析一条逃逸的等级路径。</summary>
    public static TaskId Parse(string s) => Parse(s, provider: null);

    /// <summary>解析一条逃逸的等级路径。</summary>
    public static TaskId Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Length == 0 ? None : new(HierarchicalKey.Parse(s));
    }

    /// <summary>试图分析一条逃脱的等级道路</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out TaskId result)
        => TryParse(s, provider: null, out result);

    /// <summary>试图分析一条逃脱的等级道路</summary>
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

    /// <summary>解析一条逃逸的等级路径。</summary>
    public static TaskId Parse(ReadOnlySpan<char> s) => Parse(s, provider: null);

    /// <summary>解析一条逃逸的等级路径。</summary>
    public static TaskId Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => s.IsEmpty ? None : new(HierarchicalKey.Parse(s));

    /// <summary>试图分析一条逃脱的等级道路</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out TaskId result)
        => TryParse(s, provider: null, out result);

    /// <summary>试图分析一条逃脱的等级道路</summary>
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

    /// <summary>将此标识符格式为逃避的等级路径。</summary>
    public override string ToString() => _key?.ToString() ?? string.Empty;

    /// <summary>将此标识符格式为逃避的等级路径。</summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>试图将此标识符格式化为 <paramref name="destination"/>。</summary>
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

    /// <summary>返回两个识别符是否相等。</summary>
    public static bool operator ==(TaskId left, TaskId right) => left.Equals(right);

    /// <summary>返回两个识别符是否不同。</summary>
    public static bool operator !=(TaskId left, TaskId right) => !left.Equals(right);

    /// <summary>将识别符格式化为逃避的等级路径。</summary>
    public static explicit operator string(TaskId value) => value.ToString();

    /// <summary>解析一条逃逸的等级路径。</summary>
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
    /// 校验一个段段: 它不是空的、完善格式好的 UTF-16 文本, 所以每个 UTF-8 或 JSON 标识符编码
    /// 而不是用U+FFFD替代未受控制代孕,并合并不同的识别资料。
    /// </summary>
    private static void ValidateSegment(string segment, [CallerArgumentExpression(nameof(segment))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment, paramName);
        if (!IsWellFormedUtf16(segment))
        {
            throw new ArgumentException("A task identifier segment must be well-formed UTF-16 text, without unpaired surrogates.", paramName);
        }
    }

    /// <summary>返回 <paramref name="value"/> 中每个替代方是否都是高低对数的一部分。</summary>
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

        // 逃跑者是ASCII,所以路径是完善的UTF-16, 正好是每个部分都一样。
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
