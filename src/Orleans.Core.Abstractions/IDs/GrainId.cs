using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 标识一个 Grain。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    [JsonConverter(typeof(GrainIdJsonConverter))]
    public readonly struct GrainId : IEquatable<GrainId>, IComparable<GrainId>, ISerializable, ISpanFormattable, ISpanParsable<GrainId>
    {
        [Id(0)]
        private readonly GrainType _type;

        [Id(1)]
        private readonly IdSpan _key;

        /// <summary>
        /// 创建新的 <see cref="GrainType"/> 实例。
        /// </summary>
        public GrainId(GrainType type, IdSpan key)
        {
            _type = type;
            _key = key;
        }

        /// <summary>
        /// 创建新的 <see cref="GrainType"/> 实例。
        /// </summary>
        private GrainId(SerializationInfo info, StreamingContext context)
        {
            _type = new GrainType(IdSpan.UnsafeCreate((byte[]?)info.GetValue("tv", typeof(byte[])), info.GetInt32("th")));
            _key = IdSpan.UnsafeCreate((byte[]?)info.GetValue("kv", typeof(byte[])), info.GetInt32("kh"));
        }

        /// <summary>
        /// 获取 Grain 类型。
        /// </summary>
        public GrainType Type => _type;

        /// <summary>
        /// 获取 Grain 键。
        /// </summary>
        public IdSpan Key => _key;

        /// <summary>
        /// 创建新的 <see cref="GrainType"/> 实例。
        /// </summary>
        public static GrainId Create(string type, string key) => Create(GrainType.Create(type), key);

        /// <summary>
        /// 创建新的 <see cref="GrainType"/> 实例。
        /// </summary>
        public static GrainId Create(GrainType type, string key)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(key);
            return new GrainId(type, IdSpan.Create(key));
        }

        /// <summary>
        /// 创建新的 <see cref="GrainType"/> 实例。
        /// </summary>
        public static GrainId Create(GrainType type, IdSpan key) => new GrainId(type, key);

        /// <summary>
        /// 从 Span 解析 <see cref="GrainId"/>。
        /// </summary>
        public static GrainId Parse(ReadOnlySpan<char> value, IFormatProvider? provider = null)
        {
            if (!TryParse(value, provider, out var result))
            {
                ThrowInvalidGrainId(value);

                static void ThrowInvalidGrainId(ReadOnlySpan<char> value) => throw new ArgumentException($"Unable to parse \"{value}\" as a grain id");
            }

            return result;
        }

        /// <summary>
        /// 尝试从 Span 解析 <see cref="GrainId"/>。
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示成功解析有效的 <see cref="GrainId"/>，否则返回 <see langword="false"/>。</returns>
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out GrainId result)
        {
            int i;
            if ((i = value.IndexOf('/')) < 0)
            {
                result = default;
                return false;
            }

            var typeSpan = value[0..i];
            var type = new byte[Encoding.UTF8.GetByteCount(typeSpan)];
            Encoding.UTF8.GetBytes(typeSpan, type);

            var idSpan = value[(i + 1)..];
            var id = new byte[Encoding.UTF8.GetByteCount(idSpan)];
            Encoding.UTF8.GetBytes(idSpan, id);

            result = new(new GrainType(type), new IdSpan(id));
            return true;
        }

        /// <summary>
        /// 从字符串解析 <see cref="GrainId"/>。
        /// </summary>
        public static GrainId Parse(string value)
            => Parse(value.AsSpan(), null);

        /// <summary>
        /// 从字符串解析 <see cref="GrainId"/>。
        /// </summary>
        public static GrainId Parse(string value, IFormatProvider? provider = null)
            => Parse(value.AsSpan(), provider);

        /// <summary>
        /// 尝试从字符串解析 <see cref="GrainId"/>。
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示成功解析有效的 <see cref="GrainId"/>，否则返回 <see langword="false"/>。</returns>
        public static bool TryParse(string? value, out GrainId result)
            => TryParse(value.AsSpan(), null, out result);

        /// <summary>
        /// 尝试从字符串解析 <see cref="GrainId"/>。
        /// </summary>
        /// <returns>返回 <see langword="true"/> 表示成功解析有效的 <see cref="GrainId"/>，否则返回 <see langword="false"/>。</returns>
        public static bool TryParse(string? value, IFormatProvider? provider, out GrainId result)
            => TryParse(value.AsSpan(), provider, out result);

        /// <summary>
        /// 此实例为默认值时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        public bool IsDefault => _type.IsDefault && _key.IsDefault;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is GrainId id && Equals(id);

        /// <inheritdoc/>
        public bool Equals(GrainId other) => _key.Equals(other._key) && _type.Equals(other._type);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(_type, _key);

        /// <summary>
        /// 为 Grain ID 生成均匀分布且稳定的哈希码。
        /// </summary>
        public uint GetUniformHashCode()
        {
            // 对于给定的 ID，此值必须稳定，并且在集群所有节点上相同。
            // HashCode.Combine 目前无法保证其输入对应的结果稳定。
            return _type.GetUniformHashCode() * 31 + _key.GetUniformHashCode();
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="info"/> 为 <see langword="null"/>。</exception>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);
            info.AddValue("tv", GrainType.UnsafeGetArray(_type));
            info.AddValue("th", _type.GetHashCode());
            info.AddValue("kv", IdSpan.UnsafeGetArray(_key));
            info.AddValue("kh", _key.GetHashCode());
        }

        /// <inheritdoc/>
        public int CompareTo(GrainId other)
        {
            var typeComparison = _type.CompareTo(other._type);
            if (typeComparison != 0) return typeComparison;

            return _key.CompareTo(other._key);
        }

        /// <summary>
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(GrainId left, GrainId right) => left.Equals(right);

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(GrainId left, GrainId right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"{_type}/{_key}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"{_type}/{_key}", out charsWritten);
    }

    /// <summary>
    /// 提供 <see cref="GrainId"/> 与 JSON 字符串之间的转换功能。
    /// </summary>
    public sealed class GrainIdJsonConverter : JsonConverter<GrainId>
    {
        /// <inheritdoc />
        public override GrainId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var valueLength = reader.HasValueSequence
                ? checked((int)reader.ValueSequence.Length)
                : reader.ValueSpan.Length;

            Span<char> buf = valueLength <= 128
                ? (stackalloc char[128])[..valueLength]
                : new char[valueLength];

            var written = reader.CopyString(buf);
            buf = buf[..written];

            return GrainId.Parse(buf);
        }

        /// <inheritdoc />
        public override GrainId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Read(ref reader, typeToConvert, options);

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 为 <see langword="null"/>。</exception>
        public override void Write(Utf8JsonWriter writer, GrainId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            WriteGrainId(writer, value, isPropertyName: false);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 为 <see langword="null"/>。</exception>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] GrainId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            WriteGrainId(writer, value, isPropertyName: true);
        }

        private static void WriteGrainId(Utf8JsonWriter writer, GrainId value, bool isPropertyName)
        {
            var type = value.Type.AsSpan();
            var key = value.Key.AsSpan();
            Span<byte> buf = stackalloc byte[type.Length + key.Length + 1];

            type.CopyTo(buf);
            buf[type.Length] = (byte)'/';
            key.CopyTo(buf[(type.Length + 1)..]);

            if (isPropertyName)
            {
                writer.WritePropertyName(buf);
            }
            else
            {
                writer.WriteStringValue(buf);
            }
        }
    }
}
