using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 使用应用提供的 <see cref="HierarchicalKey"/> 标识一条消息。
/// </summary>
/// <remarks>
/// 接收方依据此标识进行消息去重，因此应用在重试发送时复用它。
/// 相等性判断、哈希计算、解析和格式化均使用键的规范转义路径。
/// </remarks>
[Immutable, Alias("Orleans.Durable.Messaging.MessageId")]
[JsonConverter(typeof(MessageIdJsonConverter))]
public readonly struct MessageId : ISpanFormattable, IEquatable<MessageId>, IParsable<MessageId>, ISpanParsable<MessageId>
{
    /// <summary>从层次键创建消息标识。</summary>
    /// <param name="key">消息的应用标识。</param>
    public MessageId(HierarchicalKey key) => Key = key;

    /// <summary>获取用于标识消息的层次键。</summary>
    public HierarchicalKey Key { get; }

    /// <summary>获取此值是否尚未设置。</summary>
    public bool IsDefault => Key.IsDefault;

    /// <summary>从字面量段按根节点优先的顺序创建消息标识。</summary>
    /// <param name="segments">非空字面量段。</param>
    /// <returns>消息标识。</returns>
    /// <exception cref="ArgumentNullException">某个段为 null。</exception>
    /// <exception cref="ArgumentException">输入为空、某个段为空，或某个段包含未配对的代理项。</exception>
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

    /// <summary>比较消息标识是否相等。</summary>
    /// <param name="left">第一个标识。</param>
    /// <param name="right">第二个标识。</param>
    /// <returns>标识是否相等。</returns>
    public static bool operator ==(MessageId left, MessageId right) => left.Equals(right);

    /// <summary>比较消息标识是否不相等。</summary>
    /// <param name="left">第一个标识。</param>
    /// <param name="right">第二个标识。</param>
    /// <returns>标识是否不同。</returns>
    public static bool operator !=(MessageId left, MessageId right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => Key.ToString();

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => Key.ToString();

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        Key.TryFormat(destination, out charsWritten, format, provider);

    /// <summary>将消息标识序列化为经过规范转义的 JSON 字符串。</summary>
    public sealed class MessageIdJsonConverter : JsonConverter<MessageId>
    {
        /// <summary>创建消息标识 JSON 转换器。</summary>
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
