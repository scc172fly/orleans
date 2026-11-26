using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Streams;

namespace Orleans.Runtime
{
    /// <summary>
    /// 确定供应商内部的一条溪流
    /// </summary>
    [Immutable]
    [Serializable]
    [GenerateSerializer]
    [JsonConverter(typeof(StreamIdJsonConverter))]
    public readonly struct StreamId : IEquatable<StreamId>, IComparable<StreamId>, ISerializable, ISpanFormattable, IUtf8SpanFormattable
    {
        [Id(0)]
        private readonly byte[] fullKey;

        [Id(1)]
        private readonly ushort keyIndex;

        [Id(2)]
        private readonly int hash;

        /// <summary>
        /// 拿到了全部钥匙
        /// </summary>
        /// <value>整键。</value>
        public ReadOnlyMemory<byte> FullKey => fullKey;

        /// <summary>
        /// 获得命名空间。
        /// </summary>
        /// <value>命名空间。</value>
        public ReadOnlyMemory<byte> Namespace => fullKey.AsMemory(0, this.keyIndex);

        /// <summary>
        /// 拿到钥匙
        /// </summary>
        /// <value>密钥。</value>
        public ReadOnlyMemory<byte> Key => fullKey.AsMemory(this.keyIndex);

        private StreamId(byte[] fullKey, ushort keyIndex, int hash)
        {
            this.fullKey = fullKey;
            this.keyIndex = keyIndex;
            this.hash = hash;
        }

        internal StreamId(byte[] fullKey, ushort keyIndex)
            : this(fullKey, keyIndex, (int)StableHash.ComputeHash(fullKey))
        {
        }

        private StreamId(SerializationInfo info, StreamingContext context)
        {
            fullKey = (byte[])info.GetValue("fk", typeof(byte[]))!;
            this.keyIndex = info.GetUInt16("ki");
            this.hash = info.GetInt32("fh");
        }

        /// <summary>
        /// 初始化 <see cref="StreamId"/> 结构的新实例。
        /// </summary>
        /// <param name="ns">命名空间。</param>
        /// <param name="key">密钥。</param>
        public static StreamId Create(ReadOnlySpan<byte> ns, ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty)
                throw new ArgumentNullException(nameof(key));

            if (!ns.IsEmpty)
            {
                var fullKeysBytes = new byte[ns.Length + key.Length];
                ns.CopyTo(fullKeysBytes.AsSpan());
                key.CopyTo(fullKeysBytes.AsSpan(ns.Length));
                return new(fullKeysBytes, (ushort)ns.Length);
            }
            else
            {
                return new(key.ToArray(), 0);
            }
        }

        /// <summary>
        /// 初始化 <see cref="StreamId"/> 结构的新实例。
        /// </summary>
        /// <param name="ns">命名空间。</param>
        /// <param name="key">密钥。</param>
        public static StreamId Create(string? ns, Guid key)
        {
            if (ns is null)
            {
                var buf = new byte[32];
                Utf8Formatter.TryFormat(key, buf, out var len, 'N');
                Debug.Assert(len == 32);
                return new StreamId(buf, 0);
            }
            else
            {
                var nsLen = Encoding.UTF8.GetByteCount(ns);
                var buf = new byte[nsLen + 32];
                Encoding.UTF8.GetBytes(ns, 0, ns.Length, buf, 0);
                Utf8Formatter.TryFormat(key, buf.AsSpan(nsLen), out var len, 'N');
                Debug.Assert(len == 32);
                return new StreamId(buf, (ushort)nsLen);
            }
        }

        /// <summary>
        /// 初始化 <see cref="StreamId"/> 结构的新实例。
        /// </summary>
        /// <param name="ns">命名空间。</param>
        /// <param name="key">密钥。</param>
        public static StreamId Create(string? ns, long key) => Create(ns, key.ToString());

        /// <summary>
        /// 初始化 <see cref="StreamId"/> 结构的新实例。
        /// </summary>
        /// <param name="ns">命名空间。</param>
        /// <param name="key">密钥。</param>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> 是 <see langword="null"/>.</exception>
        public static StreamId Create(string? ns, string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (ns is null)
                return new StreamId(Encoding.UTF8.GetBytes(key), 0);

            var nsLen = Encoding.UTF8.GetByteCount(ns);
            var keyLen = Encoding.UTF8.GetByteCount(key);
            var buf = new byte[nsLen + keyLen];
            Encoding.UTF8.GetBytes(ns, 0, ns.Length, buf, 0);
            Encoding.UTF8.GetBytes(key, 0, key.Length, buf, nsLen);
            return new StreamId(buf, (ushort)nsLen);
        }

        /// <summary>
        /// 初始化 <see cref="StreamId"/> 结构的新实例。
        /// </summary>
        /// <param name="streamIdentity">流体身份。</param>
        /// <exception cref="ArgumentNullException"><paramref name="streamIdentity"/> 是 <see langword="null"/>.</exception>
        public static StreamId Create(IStreamIdentity streamIdentity)
        {
            ArgumentNullException.ThrowIfNull(streamIdentity);
            return Create(streamIdentity.Namespace, streamIdentity.Guid);
        }

        /// <inheritdoc/>
        public int CompareTo(StreamId other) => fullKey.AsSpan().SequenceCompareTo(other.fullKey);

        /// <inheritdoc/>
        public bool Equals(StreamId other) => fullKey.AsSpan().SequenceEqual(other.fullKey);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is StreamId other ? this.Equals(other) : false;

        /// <summary>
        /// 比较了两个 <see cref="StreamId"/> 平等实例。
        /// </summary>
        /// <param name="s1">第一个流身份。</param>
        /// <param name="s2">第二个流身份。</param>
        /// <returns>操作员的结果。</returns>
        public static bool operator ==(StreamId s1, StreamId s2) => s1.Equals(s2);

        /// <summary>
        /// 比较了两个 <see cref="StreamId"/> 平等实例。
        /// </summary>
        /// <param name="s1">第一个流身份。</param>
        /// <param name="s2">第二个流身份。</param>
        /// <returns>操作员的结果。</returns>
        public static bool operator !=(StreamId s1, StreamId s2) => !s2.Equals(s1);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="info"/> 是 <see langword="null"/>.</exception>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);
            info.AddValue("fk", fullKey);
            info.AddValue("ki", this.keyIndex);
            info.AddValue("fh", this.hash);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{this}";
        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            var len = Encoding.UTF8.GetCharCount(fullKey);
            if (keyIndex == 0)
            {
                // QualifiedStreamId.ToString() 被用作酒吧/次会合 grain 密钥,因此保留其历史形式。
                if (destination.Length >= len + 5)
                {
                    "null/".CopyTo(destination);
                    charsWritten = Encoding.UTF8.GetChars(fullKey, destination[5..]) + 5;
                    return true;
                }
            }
            else if (destination.Length > len)
            {
                len = Encoding.UTF8.GetChars(fullKey.AsSpan(0, keyIndex), destination);
                destination[len++] = '/';
                charsWritten = Encoding.UTF8.GetChars(fullKey.AsSpan(keyIndex), destination[len..]) + len;
                return true;
            }

            charsWritten = 0;
            return false;
        }

        bool IUtf8SpanFormattable.TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            if (keyIndex == 0)
            {
                if (utf8Destination.Length >= fullKey.Length + 5)
                {
                    "null/"u8.CopyTo(utf8Destination);
                    fullKey.CopyTo(utf8Destination[5..]);
                    bytesWritten = fullKey.Length + 5;
                    return true;
                }
            }
            else if (utf8Destination.Length > fullKey.Length)
            {
                fullKey[..keyIndex].CopyTo(utf8Destination);
                utf8Destination[keyIndex] = (byte)'/';
                fullKey[keyIndex..].CopyTo(utf8Destination[(keyIndex + 1)..]);
                bytesWritten = fullKey.Length + 1;
                return true;
            }

            bytesWritten = 0;
            return false;
        }

        /// <summary>
        /// 专业a <see cref="StreamId"/> 实例 <see cref="string"/>.
        /// </summary>
        /// <param name="value">UTF-8 编码值。</param>
        /// <returns>解析流身份。</returns>
        public static StreamId Parse(ReadOnlySpan<byte> value)
        {
            var i = value.IndexOf((byte)'/');
            if (i < 0)
            {
                throw new ArgumentException($"Unable to parse \"{Encoding.UTF8.GetString(value)}\" as a stream id");
            }

            return Create(value[..i], value[(i + 1)..]);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => this.hash;

        internal uint GetUniformHashCode() => (uint)hash;

        internal uint GetKeyIndex() => keyIndex;

        /// <summary>
        /// 返回此实例的 <see cref="Key"/> 组件,作为字符串。
        /// </summary>
        /// <returns>此实例的关键组成部分。</returns>
        public string GetKeyAsString() => Encoding.UTF8.GetString(fullKey, keyIndex, fullKey.Length - keyIndex);

        /// <summary>
        /// 返回此实例的 <see cref="Namespace"/> 组件,作为字符串。
        /// </summary>
        /// <returns>此实例的命名空间组件。</returns>
        public string? GetNamespace() => keyIndex == 0 ? null : Encoding.UTF8.GetString(fullKey, 0, keyIndex);

        internal IdSpan GetKeyIdSpan() => keyIndex == 0 ? IdSpan.UnsafeCreate(fullKey, hash) : new(fullKey.AsSpan(keyIndex).ToArray());
    }

    /// <summary>
    /// 换算功能 <see cref="StreamId"/> 与从这些天起 JSON 代表。
    /// </summary>
    public sealed class StreamIdJsonConverter : JsonConverter<StreamId>
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <inheritdoc/>
        public override StreamId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray || !reader.Read())
            {
                throw new JsonException($"Could not deserialize {nameof(StreamId)}.");
            }

            var streamNamespace = reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                _ => throw new JsonException($"Could not deserialize {nameof(StreamId)}."),
            };

            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Could not deserialize {nameof(StreamId)}.");
            }

            var key = reader.GetString();
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException($"Could not deserialize {nameof(StreamId)}.");
            }

            return Create(hasNamespace: true, streamNamespace, key);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 是 <see langword="null"/>.</exception>
        public override void Write(Utf8JsonWriter writer, StreamId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value == default || value.Key.IsEmpty)
            {
                throw new JsonException($"Could not serialize {nameof(StreamId)}.");
            }

            writer.WriteStartArray();
            if (!value.Namespace.IsEmpty)
            {
                writer.WriteStringValue(Decode(value.Namespace.Span));
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteStringValue(Decode(value.Key.Span));
            writer.WriteEndArray();
        }

        /// <inheritdoc/>
        public override StreamId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString() ?? throw new JsonException("Failed to parse StreamId from property name.");
            return ParsePropertyName(value);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 是 <see langword="null"/>.</exception>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] StreamId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WritePropertyName(FormatPropertyName(value));
        }

        internal static string FormatPropertyName(StreamId value)
        {
            if (value == default || value.Key.IsEmpty)
            {
                throw new JsonException($"Could not serialize {nameof(StreamId)}.");
            }

            var streamNamespace = value.Namespace.IsEmpty ? string.Empty : Decode(value.Namespace.Span);
            return string.Concat(
                streamNamespace.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                streamNamespace,
                Decode(value.Key.Span));
        }

        internal static StreamId ParsePropertyName(string value)
        {
            var encoded = value.AsSpan();
            var separator = encoded.IndexOf(':');
            if (separator <= 0
                || !int.TryParse(encoded[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var namespaceLength))
            {
                throw new JsonException("Failed to parse StreamId from property name.");
            }

            var components = encoded[(separator + 1)..];
            if (namespaceLength < 0 || namespaceLength > components.Length)
            {
                throw new JsonException("Failed to parse StreamId from property name.");
            }

            var streamNamespace = namespaceLength == 0 ? null : components[..namespaceLength].ToString();
            var key = components[namespaceLength..].ToString();
            return Create(hasNamespace: true, streamNamespace, key);
        }

        private static StreamId Create(bool hasNamespace, string? streamNamespace, string? key)
        {
            if (!hasNamespace || string.IsNullOrEmpty(key))
            {
                throw new JsonException($"Could not deserialize {nameof(StreamId)}.");
            }

            try
            {
                var namespaceBytes = streamNamespace is null ? Array.Empty<byte>() : StrictUtf8.GetBytes(streamNamespace);
                if (namespaceBytes.Length > ushort.MaxValue)
                {
                    throw new JsonException($"{nameof(StreamId)} namespaces cannot exceed {ushort.MaxValue} UTF-8 bytes.");
                }

                return StreamId.Create(namespaceBytes, StrictUtf8.GetBytes(key));
            }
            catch (EncoderFallbackException exception)
            {
                throw new JsonException($"{nameof(StreamId)} components must contain valid UTF-8.", exception);
            }
        }

        private static string Decode(ReadOnlySpan<byte> value)
        {
            try
            {
                return StrictUtf8.GetString(value);
            }
            catch (DecoderFallbackException exception)
            {
                throw new JsonException($"{nameof(StreamId)} components must contain valid UTF-8.", exception);
            }
        }
    }
}
