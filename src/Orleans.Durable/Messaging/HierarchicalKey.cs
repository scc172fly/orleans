using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 由非空层次段组成的不可变应用标识，按序数规则比较。
/// </summary>
/// <remarks>
/// Create 构造字面量段，并且对斜杠和反斜杠字符恰好转义一次。
/// Parse 读取规范化且已转义的路径。键封装其规范化字符串；相等性比较和哈希计算均使用该字符串。应用程序在重试之间保持标识不变。
/// 每个键均为格式正确的 UTF-16：代理字符只以高位与低位代理项成对出现，因此不同的键保有不同的序列化形式。
/// </remarks>
[Immutable, Alias("Orleans.Durable.Messaging.HierarchicalKey")]
[JsonConverter(typeof(HierarchicalKeyJsonConverter))]
public readonly struct HierarchicalKey : ISpanFormattable, IEquatable<HierarchicalKey>, IParsable<HierarchicalKey>, ISpanParsable<HierarchicalKey>
{
    /// <summary>规范化路径段内使用的转义字符。</summary>
    public const char EscapeCharacter = '\\';

    /// <summary>规范化路径段之间的分隔符。</summary>
    public const char SegmentSeparator = '/';

    private const char HighSurrogateStart = '\uD800';
    private const char LowSurrogateEnd = '\uDFFF';

    private readonly string? _value;

    private HierarchicalKey(string canonical) => _value = canonical;

    /// <summary>将层次标识序列化为经过规范转义的 JSON 字符串。</summary>
    public sealed class HierarchicalKeyJsonConverter : JsonConverter<HierarchicalKey>
    {
        /// <summary>创建层次标识 JSON 转换器。</summary>
        public HierarchicalKeyJsonConverter() { }

        /// <inheritdoc/>
        public override HierarchicalKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return default;
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName
                && TryParse(reader.GetString(), null, out var result)) return result;
            throw new JsonException("A hierarchical identity must be a canonical escaped path.");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, HierarchicalKey value, JsonSerializerOptions options)
        {
            if (value.IsDefault) writer.WriteNullValue();
            else writer.WriteStringValue(value.ToString());
        }

        /// <inheritdoc/>
        public override HierarchicalKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Read(ref reader, typeToConvert, options);

        /// <inheritdoc/>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, HierarchicalKey value, JsonSerializerOptions options)
        {
            if (value.IsDefault) throw new JsonException("A hierarchical dictionary key must be set.");
            writer.WritePropertyName(value.ToString());
        }
    }

    /// <summary>获取此值是否尚未设置。</summary>
    public bool IsDefault => _value is null;

    /// <summary>获取规范化路径的长度，以 UTF-16 字符为单位。</summary>
    public int Length => _value?.Length ?? 0;

    /// <summary>创建一个字面量段，对斜杠和反斜杠字符进行转义。</summary>
    /// <param name="value">非空字面量段。</param>
    /// <returns>该段的标识。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> 为空或包含未配对的代理项。</exception>
    public static HierarchicalKey Create(string value)
    {
        ValidateSegment(value);
        return new(Escape(value));
    }

    /// <summary>从字面量段按根节点优先的顺序创建层次结构。</summary>
    /// <param name="values">非空字面量段。</param>
    /// <returns>拥有自身不可变底层字符串的扁平规范化标识。</returns>
    /// <exception cref="ArgumentNullException">某个段为 null。</exception>
    /// <exception cref="ArgumentException">输入为空、某个段为空，或某个段包含未配对的代理项。</exception>
    public static HierarchicalKey Create(params ReadOnlySpan<string> values)
    {
        if (values.IsEmpty)
        {
            throw new ArgumentException("Values must not be empty.", nameof(values));
        }
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            ValidateSegment(value);
            if (builder.Length > 0) builder.Append(SegmentSeparator);
            AppendEscaped(builder, value);
        }
        return new(builder.ToString());
    }

    /// <summary>追加一个字面量子段。</summary>
    /// <param name="value">非空字面量子段。</param>
    /// <returns>子标识。</returns>
    /// <exception cref="InvalidOperationException">此键尚未设置。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> 为空或包含未配对的代理项。</exception>
    public HierarchicalKey CreateChildKey(string value)
    {
        EnsureSet();
        ValidateSegment(value);
        return new(string.Concat(_value, "/", Escape(value)));
    }

    /// <summary>将此层次结构与已构造的后缀层次结构组合。</summary>
    /// <param name="suffix">已构造的后缀。</param>
    /// <returns>保留两条路径段边界的扁平拼接标识。</returns>
    /// <exception cref="InvalidOperationException">此键尚未设置。</exception>
    /// <exception cref="ArgumentException"><paramref name="suffix"/> 尚未设置。</exception>
    public HierarchicalKey Append(HierarchicalKey suffix)
    {
        EnsureSet();
        if (suffix.IsDefault) throw new ArgumentException("The suffix must not be unset.", nameof(suffix));
        return new(string.Concat(_value, "/", suffix._value));
    }

    /// <summary>获取直接父级；若为根键或未设置的键，则返回 null。</summary>
    /// <returns>此键包含多个段时的父级标识。</returns>
    public HierarchicalKey? GetParent()
    {
        if (_value is null) return null;
        var lastSeparator = LastSeparator(_value);
        return lastSeparator < 0 ? null : new HierarchicalKey(_value[..lastSeparator]);
    }

    /// <summary>判断此键是否为另一个键的直接子级。</summary>
    /// <param name="other">可能的父级。</param>
    /// <returns>是否恰好有一个附加段。</returns>
    public bool IsChildOf(HierarchicalKey other) => other.IsParentOf(this);

    /// <summary>判断此键是否为另一个键的直接父级。</summary>
    /// <param name="other">可能的子级。</param>
    /// <returns>另一个键是否恰好比此键多一个段。</returns>
    public bool IsParentOf(HierarchicalKey other) => IsAncestorOf(other) && LastSeparator(other._value!) == Length;

    /// <summary>判断此键是否等于另一个键或为其祖先。</summary>
    /// <param name="other">要检查的标识。</param>
    /// <returns>完整前缀是否由按序数比较相等的各段组成。未设置的键返回 false。</returns>
    public bool IsAncestorOf(HierarchicalKey other) => !IsDefault && !other.IsDefault
        && (Equals(other) || other.Length > Length && other._value![Length] == SegmentSeparator
            && other._value.StartsWith(_value!, StringComparison.Ordinal));

    /// <inheritdoc/>
    public static HierarchicalKey Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParse(s, provider, out var result) ? result : throw new FormatException("The value is not a valid canonical hierarchical key.");
    }

    /// <inheritdoc/>
    public static HierarchicalKey Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException("The value is not a valid canonical hierarchical key.");

    /// <inheritdoc/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out HierarchicalKey result)
    {
        if (s is not null && IsCanonical(s))
        {
            result = new(s);
            return true;
        }
        result = default;
        return false;
    }

    /// <inheritdoc/>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out HierarchicalKey result)
    {
        if (IsCanonical(s))
        {
            result = new(new string(s));
            return true;
        }
        result = default;
        return false;
    }

    /// <inheritdoc/>
    public bool Equals(HierarchicalKey other) => string.Equals(_value, other._value, StringComparison.Ordinal);


    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is HierarchicalKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    /// <summary>按序数规则比较完整的键值。</summary>
    /// <param name="left">第一个标识。</param>
    /// <param name="right">第二个标识。</param>
    /// <returns>标识是否相等。</returns>
    public static bool operator ==(HierarchicalKey left, HierarchicalKey right) => left.Equals(right);

    /// <summary>按序数规则比较完整的键值是否不相等。</summary>
    /// <param name="left">第一个标识。</param>
    /// <param name="right">第二个标识。</param>
    /// <returns>标识是否不同。</returns>
    public static bool operator !=(HierarchicalKey left, HierarchicalKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (ToString().AsSpan().TryCopyTo(destination))
        {
            charsWritten = Length;
            return true;
        }
        charsWritten = 0;
        return false;
    }

    /// <summary>按从根开始的顺序枚举已转义的规范化路径段 Span。</summary>
    /// <returns>无需分配内存的段枚举器。</returns>
    public SegmentEnumerator GetEnumerator() => new(ToString().AsSpan());

    private void EnsureSet()
    {
        if (IsDefault) throw new InvalidOperationException("The key must not be unset.");
    }

    private static void ValidateSegment(string value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, paramName);
        if (!IsWellFormedUtf16(value)) throw new ArgumentException("The value must not contain unpaired surrogate characters.", paramName);
    }

    // Orleans 字符串序列化会替换未配对的代理项，因此若允许这些代理项，不同的键可能在传输中发生碰撞。
    internal static bool IsWellFormedUtf16(ReadOnlySpan<char> value)
    {
        var i = value.IndexOfAnyInRange(HighSurrogateStart, LowSurrogateEnd);
        if (i < 0) return true;
        for (; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i == value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny(EscapeCharacter, SegmentSeparator) < 0) return value;
        var builder = new StringBuilder(value.Length);
        AppendEscaped(builder, value);
        return builder.ToString();
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            if (character is EscapeCharacter or SegmentSeparator) builder.Append(EscapeCharacter);
            builder.Append(character);
        }
    }

    private static int LastSeparator(ReadOnlySpan<char> path)
    {
        var lastSeparator = -1;
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] == EscapeCharacter) i++;
            else if (path[i] == SegmentSeparator) lastSeparator = i;
        }
        return lastSeparator;
    }

    private static bool IsCanonical(ReadOnlySpan<char> path)
    {
        var segmentLength = 0;
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] == EscapeCharacter)
            {
                if (++i == path.Length || path[i] is not (EscapeCharacter or SegmentSeparator)) return false;
            }
            else if (path[i] == SegmentSeparator)
            {
                if (segmentLength == 0) return false;
                segmentLength = 0;
                continue;
            }
            else if (char.IsSurrogate(path[i]))
            {
                if (!char.IsHighSurrogate(path[i]) || ++i == path.Length || !char.IsLowSurrogate(path[i])) return false;
            }
            segmentLength++;
        }
        return segmentLength != 0;
    }

    /// <summary>枚举不可变规范化键的借用 Span。</summary>
    public ref struct SegmentEnumerator
    {
        private readonly ReadOnlySpan<char> _path;
        private int _next;

        internal SegmentEnumerator(ReadOnlySpan<char> path) => _path = path;

        /// <summary>获取当前已转义的规范化路径段。</summary>
        public ReadOnlySpan<char> Current { get; private set; }

        /// <summary>移至下一个段。</summary>
        /// <returns>是否存在可用的段。</returns>
        public bool MoveNext()
        {
            if (_next >= _path.Length)
            {
                Current = default;
                return false;
            }
            var start = _next;
            for (; _next < _path.Length; _next++)
            {
                if (_path[_next] == EscapeCharacter) _next++;
                else if (_path[_next] == SegmentSeparator) break;
            }
            Current = _path[start.._next];
            _next++;
            return true;
        }
    }
}
