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
    /// 唯一能识别 grain 激活的标识 。
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
        /// <param name="key">激活代号。</param>
        public ActivationId(Guid key) => Key = key;

        /// <summary>
        /// 获得一个值,以显示此实例是否为默认实例。
        /// </summary>
        public bool IsDefault => Key == default;

        /// <summary>
        /// 返回新的随机激活代号。
        /// </summary>
        /// <returns>一个新的随机激活代号。</returns>
        public static ActivationId NewId() => new(Guid.NewGuid());

        /// <summary>
        /// 返回从所提供的 grain id中确定和可复制的激活代号。
        /// </summary>
        /// <param name="grain">下 grain id. 数据。</param>
        /// <returns>启动代号,该代号是从提供 grain id中确定和可复制计算的。</returns>
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
        /// 返回此激活代号的字符串表示,该代号可以由 <see cref="FromParsableString"/>解析。
        /// </summary>
        /// <returns>此激活代号的字符串表示, 可由 <see cref="FromParsableString"/>解析 。</returns>
        public string ToParsableString() => ToString();

        /// <summary>
        /// 分析使用 <see cref="ToParsableString"/>创建的激活代号的字符串表示。
        /// </summary>
        /// <param name="activationId">激活id的字符串表示。</param>
        /// <returns>激活代号。</returns>
        public static ActivationId FromParsableString(string activationId)
        {
            var span = activationId.AsSpan();
            return span.Length == 33 && span[0] == '@' ? new(Guid.ParseExact(span[1..], "N")) : throw new FormatException($"Invalid activation id: {activationId}");
        }

        /// <summary>
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(ActivationId left, ActivationId right) => left.Equals(right);

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(ActivationId left, ActivationId right) => !(left == right);
    }

    /// <summary>
    /// 换算功能 <see cref="ActivationId"/> 与从这些天起 JSON 代表。
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
