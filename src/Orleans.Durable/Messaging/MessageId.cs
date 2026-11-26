using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 以应用程序供应方式 <see cref="HierarchicalKey"/>指明一条电文。
/// </summary>
/// <remarks>
/// 接收器通过此身份复制信件, 所以在重试发送时应用再使用它 。
/// 平等、散列、解析和格式化使用钥匙的卡通道。
/// </remarks>
[Immutable, Alias("Orleans.Durable.Messaging.MessageId")]
[JsonConverter(typeof(MessageIdJsonConverter))]
public readonly struct MessageId : ISpanFormattable, IEquatable<MessageId>, IParsable<MessageId>, ISpanParsable<MessageId>
{
    /// <summary>从等级键创建信件身份。</summary>
    /// <param name="key">电文的应用身份。</param>
    public MessageId(HierarchicalKey key) => Key = key;

    /// <summary>获取识别电文的等级键。</summary>
    public HierarchicalKey Key { get; }

    /// <summary>获取是否该值未设定。</summary>
    public bool IsDefault => Key.IsDefault;

    /// <summary>从根一顺序的字面段创建信件身份。</summary>
    /// <param name="segments">非空字区段。</param>
    /// <returns>电文身份。</returns>
    /// <exception cref="ArgumentNullException">段无效。</exception>
    /// <exception cref="ArgumentException">输入或一个段为空,或者一个段含有未受偏移的代孕。</exception>
    public static MessageId Create(params ReadOnlySpan<string> segments) => new(HierarchicalKey.Create(segments));

    /// <inheritdoc/>
    public static MessageId Parse(string s, IFormatProvider? provider = null) => new(HierarchicalKey.Parse(s, provider));

    /// <inheritdoc/>
    public static MessageId Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) => new(HierarchicalKey.Parse(s, provider));

    /// <inheritdoc/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out MessageId result)
    {
        var parsed = HierarchicalKey.TryParse(s, provider, out var key);
        result = new(key);
        return parsed;
    }

    /// <inheritdoc/>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out MessageId result)
    {
        var parsed = HierarchicalKey.TryParse(s, provider, out var key);
        result = new(key);
        return parsed;
    }

    /// <inheritdoc/>
    public bool Equals(MessageId other) => Key.Equals(other.Key);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MessageId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Key.GetHashCode();

    /// <summary>比较信件身份。</summary>
    /// <param name="left">第一个身份。</param>
    /// <param name="right">第二个身份。</param>
    /// <returns>身份是否相等。</returns>
    public static bool operator ==(MessageId left, MessageId right) => left.Equals(right);

    /// <summary>比较不平等的信息身份。</summary>
    /// <param name="left">第一个身份。</param>
    /// <param name="right">第二个身份。</param>
    /// <returns>身份是否不同。</returns>
    public static bool operator !=(MessageId left, MessageId right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => Key.ToString();

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => Key.ToString();

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        Key.TryFormat(destination, out charsWritten, format, provider);

    /// <summary>将信件身份序列成可控的逃出 JSON 字符串。</summary>
    public sealed class MessageIdJsonConverter : JsonConverter<MessageId>
    {
        /// <summary>创建信件身份 JSON 转换器。</summary>
        public MessageIdJsonConverter() { }

        /// <inheritdoc/>
        public override MessageId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return default;
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName
                && TryParse(reader.GetString(), null, out var result)) return result;
            throw new JsonException("A message identity must be a canonical escaped path.");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, MessageId value, JsonSerializerOptions options)
        {
            if (value.IsDefault) writer.WriteNullValue();
            else writer.WriteStringValue(value.ToString());
        }

        /// <inheritdoc/>
        public override MessageId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Read(ref reader, typeToConvert, options);

        /// <inheritdoc/>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, MessageId value, JsonSerializerOptions options)
        {
            if (value.IsDefault) throw new JsonException("A message identity dictionary key must be set.");
            writer.WritePropertyName(value.ToString());
        }
    }
}
