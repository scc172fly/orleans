using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 一种由非空等级构成的永恒、常规应用身份。
/// </summary>
/// <remarks>
/// 创建字面区段, 并避免一次的斜线和反斜字符 。
/// 分析读取金刚石脱轨路径。 键盘将它包扎在条金刚石线上; 平等和散列使用该字符串。 应用程序会保护跨串的特性 。
/// 每个键都是完善的UTF-16:代用字符只作为高低的对子出现,因此不同的键保持不同的序列形式。
/// </remarks>
[Immutable, Alias("Orleans.Durable.Messaging.HierarchicalKey")]
[JsonConverter(typeof(HierarchicalKeyJsonConverter))]
public readonly struct HierarchicalKey : ISpanFormattable, IEquatable<HierarchicalKey>, IParsable<HierarchicalKey>, ISpanParsable<HierarchicalKey>
{
    /// <summary>共弦段内使用的逃避字符。</summary>
    public const char EscapeCharacter = '\\';

    /// <summary>峡谷段之间的分隔符。</summary>
    public const char SegmentSeparator = '/';

    private const char HighSurrogateStart = '\uD800';
    private const char LowSurrogateEnd = '\uDFFF';

    private readonly string? _value;

    private HierarchicalKey(string canonical) => _value = canonical;

    /// <summary>序列等级身份为可控逃逸 JSON 字符串。</summary>
    public sealed class HierarchicalKeyJsonConverter : JsonConverter<HierarchicalKey>
    {
        /// <summary>创建等级身份 JSON 转换器。</summary>
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

    /// <summary>获取是否该值未设定。</summary>
    public bool IsDefault => _value is null;

    /// <summary>获得以 UTF-16 字符为主线路径长度。</summary>
    public int Length => _value?.Length ?? 0;

    /// <summary>创建一个字面段, 避开斜线和反斜线字符。</summary>
    /// <param name="value">非空字面段。</param>
    /// <returns>分块身份</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 无效。</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> 是空的或含有一个未受偏移的代孕。</exception>
    public static HierarchicalKey Create(string value)
    {
        ValidateSegment(value);
        return new(Escape(value));
    }

    /// <summary>从根一顺序的字面段创建层次。</summary>
    /// <param name="values">非空字区段。</param>
    /// <returns>一个拥有不可改变的背弦的平板金刚石身份。</returns>
    /// <exception cref="ArgumentNullException">段无效。</exception>
    /// <exception cref="ArgumentException">输入或一个段为空,或者一个段含有未受偏移的代孕。</exception>
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

    /// <summary>附加一个字节子段。</summary>
    /// <param name="value">非空字子子节。</param>
    /// <returns>儿童身份。</returns>
    /// <exception cref="InvalidOperationException">此密钥未设置 。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 无效。</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> 是空的或含有一个未受偏移的代孕。</exception>
    public HierarchicalKey CreateChildKey(string value)
    {
        EnsureSet();
        ValidateSegment(value);
        return new(string.Concat(_value, "/", Escape(value)));
    }

    /// <summary>组成此等级, 并带有已经构建的后缀等级。</summary>
    /// <param name="suffix">构造的后缀。</param>
    /// <returns>一种固定的连接身份 保护两条路段的边界</returns>
    /// <exception cref="InvalidOperationException">此密钥未设置 。</exception>
    /// <exception cref="ArgumentException"><paramref name="suffix"/> 已被解置。</exception>
    public HierarchicalKey Append(HierarchicalKey suffix)
    {
        EnsureSet();
        if (suffix.IsDefault) throw new ArgumentException("The suffix must not be unset.", nameof(suffix));
        return new(string.Concat(_value, "/", suffix._value));
    }

    /// <summary>获取直系父母,或者根或未设密钥的无效。</summary>
    /// <returns>当此键有多个段时父称。</returns>
    public HierarchicalKey? GetParent()
    {
        if (_value is null) return null;
        var lastSeparator = LastSeparator(_value);
        return lastSeparator < 0 ? null : new HierarchicalKey(_value[..lastSeparator]);
    }

    /// <summary>测试此密钥是否是对方的直生子。</summary>
    /// <param name="other">潜在的母体。</param>
    /// <returns>是否还有一小段。</returns>
    public bool IsChildOf(HierarchicalKey other) => other.IsParentOf(this);

    /// <summary>测试此密钥是否为对方的直系父。</summary>
    /// <param name="other">潜在的孩子</param>
    /// <returns>其它键是否将此密钥完全扩展为一个段。</returns>
    public bool IsParentOf(HierarchicalKey other) => IsAncestorOf(other) && LastSeparator(other._value!) == Length;

    /// <summary>测试此密钥是否等于或是一个其他密钥的祖先。</summary>
    /// <param name="other">检查的身份。</param>
    /// <returns>完整前缀是否包含相等的正则区段。 取消设置键返回假 。</returns>
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

    /// <summary>比较完整的正则密钥值。</summary>
    /// <param name="left">第一个身份。</param>
    /// <param name="right">第二个身份。</param>
    /// <returns>身份是否相等。</returns>
    public static bool operator ==(HierarchicalKey left, HierarchicalKey right) => left.Equals(right);

    /// <summary>比较不平等的完整正则关键值。</summary>
    /// <param name="left">第一个身份。</param>
    /// <param name="right">第二个身份。</param>
    /// <returns>身份是否不同。</returns>
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

    /// <summary>逃出的金刚石段数以根第一顺序横穿。</summary>
    /// <returns>一个不分配部分的点数员。</returns>
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

    // Orleans 字符串序列化取代了无偏差的代孕器,所以接受它们会使不同的钥匙在线上相撞。
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

    /// <summary>数字,列出不可变的金刚金字键的借款跨度。</summary>
    public ref struct SegmentEnumerator
    {
        private readonly ReadOnlySpan<char> _path;
        private int _next;

        internal SegmentEnumerator(ReadOnlySpan<char> path) => _path = path;

        /// <summary>获取当前逃逸的金刚石段。</summary>
        public ReadOnlySpan<char> Current { get; private set; }

        /// <summary>下一部分的进度。</summary>
        /// <returns>是否有一个段。</returns>
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
