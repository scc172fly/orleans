using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 唯一标识一个 Grain 激活实例。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    [JsonConverter(typeof(ActivationIdConverter))]
    public readonly struct ActivationId : IEquatable<ActivationId>, ISpanFormattable
    {
        [DataMember(Order = 0)]
        [Id(0)]
        internal readonly Guid Key;

        /// <summary>
        /// 初始化 <see cref="ActivationId"/> 结构的新实例。
        /// </summary>
        /// <param name="key">激活实例 ID。</param>
        public ActivationId(Guid key) => Key = key;

        /// <summary>
        /// 获取一个值，指示此实例是否为默认实例。
        /// </summary>
        public bool IsDefault => Key == default;

        /// <summary>
        /// 返回一个新的随机激活实例 ID。
        /// </summary>
        /// <returns>新的随机激活实例 ID。</returns>
        public static ActivationId NewId() => new(Guid.NewGuid());

        /// <summary>
        /// 返回根据提供的 Grain ID 以确定性、可复现的方式计算出的激活实例 ID。
        /// </summary>
        /// <param name="grain">Grain ID。</param>
        /// <returns>根据提供的 Grain ID 以确定性、可复现的方式计算出的激活实例 ID。</returns>
        public static ActivationId GetDeterministic(GrainId grain)
        {
            Span<byte> temp = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(temp, grain.Type.GetUniformHashCode());
            BinaryPrimitives.WriteUInt64LittleEndian(temp[8..], grain.Key.GetUniformHashCode());
            var key = new Guid(temp);
            return new ActivationId(key);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is ActivationId other && Key.Equals(other.Key);

        /// <inheritdoc />
        public bool Equals(ActivationId other) => Key.Equals(other.Key);

        /// <inheritdoc />
        public override int GetHashCode() => Key.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => $"@{Key:N}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"@{Key:N}", out charsWritten);

        /// <summary>
        /// 返回此激活实例 ID 的字符串表示形式，可由 <see cref="FromParsableString"/> 解析。
        /// </summary>
        /// <returns>此激活实例 ID 的字符串表示形式，可由 <see cref="FromParsableString"/> 解析。</returns>
        public string ToParsableString() => ToString();

        /// <summary>
        /// 解析使用 <see cref="ToParsableString"/> 创建的激活实例 ID 的字符串表示形式。
        /// </summary>
        /// <param name="activationId">激活实例 ID 的字符串表示形式。</param>
        /// <returns>激活实例 ID。</returns>
        public static ActivationId FromParsableString(string activationId)
        {
            var span = activationId.AsSpan();
            return span.Length == 33 && span[0] == '@' ? new(Guid.ParseExact(span[1..], "N")) : throw new FormatException($"Invalid activation id: {activationId}");
        }

        /// <summary>
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(ActivationId left, ActivationId right) => left.Equals(right);

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(ActivationId left, ActivationId right) => !(left == right);
    }

    /// <summary>
    /// 提供 <see cref="ActivationId"/> 实例与其 JSON 表示形式之间的转换功能。
    /// </summary>
    public sealed class ActivationIdConverter : JsonConverter<ActivationId>
    {
        /// <inheritdoc />
        public override ActivationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ActivationId.FromParsableString(reader.GetString() ?? throw new JsonException($"Could not deserialize {nameof(ActivationId)}."));

        private void WriteCore(Utf8JsonWriter writer, ActivationId value, JsonSerializerOptions options, bool writeAsPropertyName)
        {
            Span<byte> buf = stackalloc byte[33];
            buf[0] = (byte)'@';
            Utf8Formatter.TryFormat(value.Key, buf[1..], out var len, 'N');
            Debug.Assert(len == 32);

            if (writeAsPropertyName)
            {
                writer.WritePropertyName(buf);
            }
            else
            {
                writer.WriteStringValue(buf);
            }
        }
        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ActivationId value, JsonSerializerOptions options)
            => WriteCore(writer, value, options, false);

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] ActivationId value, JsonSerializerOptions options)
            => WriteCore(writer, value, options, true);

        /// <inheritdoc />
        public override ActivationId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Read(ref reader, typeToConvert, options);
    }
}
