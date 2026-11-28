using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 基于 <see cref="Guid"/> 的唯一标识符。
    /// </summary>
    [Serializable]
    [Immutable]
    [GenerateSerializer]
    [JsonConverter(typeof(GuidIdConverter))]
    public sealed class GuidId : IEquatable<GuidId>, IComparable<GuidId>, ISerializable
    {
        private static readonly Interner<Guid, GuidId> guidIdInternCache = new Interner<Guid, GuidId>(InternerConstants.SIZE_LARGE);

        /// <summary>
        /// 底层 <see cref="Guid"/>。
        /// </summary>
        [Id(0)]
        public readonly Guid Guid;

        /// <summary>
        /// 初始化 <see cref="GuidId"/> 类的新实例。
        /// </summary>
        /// <param name="guid">
        /// 底层 <see cref="Guid"/>。
        /// </param>
        private GuidId(Guid guid)
        {
            this.Guid = guid;
        }

        /// <summary>
        /// 返回新的随机生成的 <see cref="GuidId"/>。
        /// </summary>
        /// <returns>新的随机生成的 <see cref="GuidId"/>。</returns>
        public static GuidId GetNewGuidId()
        {
            return FindOrCreateGuidId(Guid.NewGuid());
        }

        /// <summary>
        /// 返回一个 <see cref="GuidId"/> 实例，其对应提供的 <see cref="Guid"/>。
        /// </summary>
        /// <param name="guid">GUID。</param>
        /// <returns><see cref="GuidId"/> 实例，其对应提供的 <see cref="Guid"/>。</returns>
        public static GuidId GetGuidId(Guid guid)
        {
            return FindOrCreateGuidId(guid);
        }

        /// <summary>
        /// 返回一个 <see cref="GuidId"/> 实例，其对应提供的 <see cref="Guid"/>。
        /// </summary>
        /// <param name="guid"><see cref="Guid"/>。</param>
        /// <returns><see cref="GuidId"/> 实例，其对应提供的 <see cref="Guid"/>。</returns>
        private static GuidId FindOrCreateGuidId(Guid guid)
        {
            return guidIdInternCache.FindOrCreate(guid, g => new GuidId(g));
        }

        /// <inheritdoc />
        public int CompareTo(GuidId? other) => other is null ? 1 : Guid.CompareTo(other.Guid);

        /// <inheritdoc />
        public bool Equals(GuidId? other) => other is not null && Guid.Equals(other.Guid);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as GuidId);

        /// <inheritdoc />
        public override int GetHashCode() => Guid.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => Guid.ToString();

        /// <summary>
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(GuidId? left, GuidId? right) => ReferenceEquals(left, right) || (left?.Equals(right) ?? false);

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(GuidId? left, GuidId? right) => !(left == right);

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="info"/> 为 <see langword="null"/>。</exception>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);
            info.AddValue("Guid", Guid, typeof(Guid));
        }

        private GuidId(SerializationInfo info, StreamingContext context)
        {
            // ! 这是较旧的模式，与可空引用类型不兼容。
            Guid = (Guid)info.GetValue("Guid", typeof(Guid))!;
        }
    }

    /// <summary>
    /// 提供 <see cref="GuidId"/> 实例与其 JSON 表示形式之间的转换功能。
    /// </summary>
    public sealed class GuidIdConverter : JsonConverter<GuidId>
    {
        /// <inheritdoc />
        public override GuidId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadCore(ref reader);

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 为 <see langword="null"/>。</exception>
        public override void Write(Utf8JsonWriter writer, GuidId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            Span<byte> buffer = stackalloc byte[32];
            if (!value.Guid.TryFormat(buffer, out var written, "N"))
            {
                throw new JsonException($"Could not serialize {nameof(GuidId)}.");
            }

            writer.WriteStringValue(buffer[..written]);
        }

        /// <inheritdoc />
        public override GuidId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadCore(ref reader);

        private static GuidId ReadCore(ref Utf8JsonReader reader)
        {
            if (reader.TokenType is not JsonTokenType.String and not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Could not deserialize {nameof(GuidId)}.");
            }

            Span<char> buffer = !reader.HasValueSequence || reader.ValueSequence.Length <= 32
                                ? stackalloc char[32]
                                : new char[checked((int)reader.ValueSequence.Length)];

            var read = reader.CopyString(buffer);
            if (!Guid.TryParseExact(buffer[..read], "N", out var value))
            {
                throw new JsonException($"Could not deserialize {nameof(GuidId)}.");
            }

            return GuidId.GetGuidId(value);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 为 <see langword="null"/>。</exception>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] GuidId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            Span<byte> buffer = stackalloc byte[32];
            if (value.Guid.TryFormat(buffer, out var written, "N"))
            {
                writer.WritePropertyName(buffer[..written]);
            }
            else
            {
                throw new JsonException($"Could not serialize {nameof(GuidId)}.");
            }
        }
    }
}
