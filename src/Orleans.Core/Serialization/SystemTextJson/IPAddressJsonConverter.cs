#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Serialization
{
    /// <summary>
    /// 换算功能 <see cref="IPAddress"/> 与从这些天起 JSON 代表。
    /// </summary>
    public sealed class IPAddressJsonConverter : JsonConverter<IPAddress>
    {
        private const int MaxAddressSize = 65; // 最大字节或字符堆叠位置,取自 IPAddressParser.MaxIPv6StringLength

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 是 <see langword="null"/>。</exception>
        public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            WriteCore(writer, value, options, false);
        }

        private void WriteCore(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options, bool writeAsPropertyName)
        {
            Span<byte> buf = stackalloc byte[MaxAddressSize];
            if (value.TryFormat(buf, out var bytesWritten))
            {
                if (writeAsPropertyName)
                {
                    writer.WritePropertyName(buf[..bytesWritten]);
                }
                else
                {
                    writer.WriteStringValue(buf[..bytesWritten]);
                }
            }
            else
            {
                if (writeAsPropertyName)
                {
                    writer.WritePropertyName(value.ToString());
                }
                else
                {
                    writer.WriteStringValue(value.ToString());
                }
            }
        }

        /// <inheritdoc />
        public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(ref reader);

        private static IPAddress Parse(ref Utf8JsonReader reader)
        {
            if (reader.TokenType is not JsonTokenType.String and not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Could not deserialize {nameof(IPAddress)}.");
            }

            var valueLength = reader.HasValueSequence
                ? checked((int)reader.ValueSequence.Length)
                : reader.ValueSpan.Length;

            if (valueLength <= MaxAddressSize)
            {
                Span<char> chars = stackalloc char[MaxAddressSize];
                var written = reader.CopyString(chars);
                return IPAddress.Parse(chars[..written]);
            }
            else
            {
                return IPAddress.Parse(reader.GetString() ?? throw new JsonException($"Could not deserialize {nameof(IPAddress)}."));
            }
        }

        /// <inheritdoc />
        public override IPAddress ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(ref reader);

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 是 <see langword="null"/>。</exception>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] IPAddress value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            WriteCore(writer, value, options, true);
        }
    }
}
