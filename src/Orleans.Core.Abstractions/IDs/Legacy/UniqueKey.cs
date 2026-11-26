using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示遗留 Orleans grain 标识编码所用的复合密钥。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    [SuppressReferenceTracking]
    [JsonConverter(typeof(UniqueKeyJsonConverter))]
    public sealed class UniqueKey : IComparable<UniqueKey>, IEquatable<UniqueKey>
    {
        /// <summary>
        /// 编码成 UniqueKeys的型号 id值
        /// </summary>
        public enum Category : byte
        {
            /// <summary>
            /// 未指定类别 。
            /// </summary>
            None = 0,

            /// <summary>
            /// 系统目标识别码
            /// </summary>
            SystemTarget = 1,

            /// <summary>
            /// 一个系统 grain 标识符。
            /// </summary>
            SystemGrain = 2,

            /// <summary>
            /// 一个 grain 标识符。
            /// </summary>
            Grain = 3,

            /// <summary>
            /// 客户识别符。
            /// </summary>
            Client = 4,

            /// <summary>
            /// 带有密钥扩展名的 grain 标识符。
            /// </summary>
            KeyExtGrain = 6,
            // 7是 GeoClient

            /// <summary>
            /// 带有密钥扩展名的系统目标识别符 。
            /// </summary>
            KeyExtSystemTarget = 8,
        }

        /// <summary>
        /// 获得主键的前64位
        /// </summary>
        [Id(0)]
        public ulong N0 { get; private set; }

        /// <summary>
        /// 获得主键的第二64位
        /// </summary>
        [Id(1)]
        public ulong N1 { get; private set; }

        /// <summary>
        /// 获得包装类别和类型代码数据。
        /// </summary>
        [Id(2)]
        public ulong TypeCodeData { get; private set; }

        /// <summary>
        /// 获得可选主密钥扩展名 。
        /// </summary>
        [Id(3)]
        public string? KeyExt { get; private set; }

        [NonSerialized]
        private uint uniformHashCache;

        /// <summary>
        /// 获得32位元的遗留型号代码。
        /// </summary>
        public int BaseTypeCode => (int)TypeCodeData;

        /// <summary>
        /// 获取 <see cref="TypeCodeData"/>编码的识别符号类别。
        /// </summary>
        public Category IdCategory => GetCategory(TypeCodeData);

        /// <summary>
        /// 获得一个值,显示主密钥是否编码为 <see cref="long"/>。
        /// </summary>
        public bool IsLongKey => N0 == 0;

        /// <summary>
        /// 获得一个值, 显示此密钥是否代表系统目标 。
        /// </summary>
        public bool IsSystemTargetKey
            => IsSystemTarget(IdCategory);

        private static bool IsSystemTarget(Category category)
            => category == Category.SystemTarget || category == Category.KeyExtSystemTarget;

        /// <summary>
        /// 获得一个值,显示此密钥是否有主密钥扩展名。
        /// </summary>
        public bool HasKeyExt => IsKeyExt(IdCategory);

        private static bool IsKeyExt(Category category)
            => category == Category.KeyExtGrain || category == Category.KeyExtSystemTarget;

        internal static readonly UniqueKey Empty = new UniqueKey();

        internal static UniqueKey Parse(ReadOnlySpan<char> input) => ParseCore(input, trim: true);

        internal static UniqueKey ParseCanonical(ReadOnlySpan<char> input) => ParseCore(input, trim: false);

        private static UniqueKey ParseCore(ReadOnlySpan<char> input, bool trim)
        {
            const int minimumValidKeyLength = 48;
            if (trim)
            {
                input = input.Trim();
            }

            if (input.Length >= minimumValidKeyLength)
            {
                var n0 = ulong.Parse(input[..16].ToString(), NumberStyles.AllowHexSpecifier);
                var n1 = ulong.Parse(input.Slice(16, 16).ToString(), NumberStyles.AllowHexSpecifier);
                var typeCodeData = ulong.Parse(input.Slice(32, 16).ToString(), NumberStyles.AllowHexSpecifier);
                string? keyExt = null;
                if (input.Length > minimumValidKeyLength)
                {
                    if (input[48] != '+') throw new InvalidDataException("UniqueKey hex string missing + separator.");
                    keyExt = input[49..].ToString();
                }

                return NewKey(n0, n1, typeCodeData, keyExt);
            }

            // 最后,为了方便起见, 我们试图使用 UID 语法来分析字符串。 这是按单位需要的
            // 但我不知道是否生产需要
            return NewKey(Guid.Parse(input.ToString()));
        }

        internal static UniqueKey NewKey(ulong n0, ulong n1, Category category, long typeData, string? keyExt)
            => NewKey(n0, n1, GetTypeCodeData(category, typeData), keyExt);

        internal static UniqueKey NewKey(long longKey, Category category = Category.None, long typeData = 0, string? keyExt = null)
        {
            ThrowIfIsSystemTargetKey(category);

            var key = NewKey(GetTypeCodeData(category, typeData), keyExt);
            key.N1 = (ulong)longKey;
            return key;
        }

        /// <summary>
        /// 创建带有随机生成的 GUID 主密钥的密钥。
        /// </summary>
        /// <returns>一个新的独有密钥。</returns>
        public static UniqueKey NewKey() => new UniqueKey { Guid = Guid.NewGuid() };

        internal static UniqueKey NewKey(Guid guid) => new UniqueKey { Guid = guid };

        internal static UniqueKey NewKey(Guid guid, Category category = Category.None, long typeData = 0, string? keyExt = null)
        {
            ThrowIfIsSystemTargetKey(category);

            var key = NewKey(GetTypeCodeData(category, typeData), keyExt);
            key.Guid = guid;
            return key;
        }

        internal static UniqueKey NewEmptySystemTargetKey(long typeData)
            => new UniqueKey { TypeCodeData = GetTypeCodeData(Category.SystemTarget, typeData) };

        /// <summary>
        /// 创建系统目标键,使用图形用户界面数据主键。
        /// </summary>
        /// <param name="guid">主键。</param>
        /// <param name="typeData">系统目标类型数据。</param>
        /// <returns>新系统目标键。</returns>
        public static UniqueKey NewSystemTargetKey(Guid guid, long typeData)
            => new UniqueKey { Guid = guid, TypeCodeData = GetTypeCodeData(Category.SystemTarget, typeData) };

        /// <summary>
        /// 从系统标识符创建系统目标键。
        /// </summary>
        /// <param name="systemId">系统标识符。</param>
        /// <returns>新系统目标键。</returns>
        public static UniqueKey NewSystemTargetKey(short systemId)
            => new UniqueKey { N1 = (ulong)systemId, TypeCodeData = GetTypeCodeData(Category.SystemTarget) };

        /// <summary>
        /// 创建有整数主键的 grain 服务密钥。
        /// </summary>
        /// <param name="key">主键。</param>
        /// <param name="typeData">下 grain 服务型数据。</param>
        /// <returns>一个新的 grain 服务关键字。</returns>
        public static UniqueKey NewGrainServiceKey(short key, long typeData)
            => new UniqueKey { N1 = (ulong)key, TypeCodeData = GetTypeCodeData(Category.SystemTarget, typeData) };

        /// <summary>
        /// 创建带有字符串主键扩展的 grain 服务密钥。
        /// </summary>
        /// <param name="key">主键扩展。</param>
        /// <param name="typeData">下 grain 服务型数据。</param>
        /// <returns>一个新的 grain 服务关键字。</returns>
        public static UniqueKey NewGrainServiceKey(string key, long typeData)
            => NewKey(GetTypeCodeData(Category.KeyExtSystemTarget, typeData), key);

        internal static UniqueKey NewKey(ulong n0, ulong n1, ulong typeCodeData, string? keyExt)
        {
            var key = NewKey(typeCodeData, keyExt);
            key.N0 = n0;
            key.N1 = n1;
            return key;
        }

        private static UniqueKey NewKey(ulong typeCodeData, string? keyExt)
        {
            if (IsKeyExt(GetCategory(typeCodeData)))
            {
                if (string.IsNullOrWhiteSpace(keyExt))
                    throw keyExt is null ? new ArgumentNullException(nameof(keyExt)) : throw new ArgumentException("Extended key is empty or white space.", nameof(keyExt));
            }
            else if (keyExt != null) throw new ArgumentException("Only key extended grains can specify a non-null key extension.");
            return new UniqueKey { TypeCodeData = typeCodeData, KeyExt = keyExt };
        }

        private void ThrowIfIsNotLong()
        {
            if (!IsLongKey)
                throw new InvalidOperationException("this key cannot be interpreted as a long value");
        }

        private static void ThrowIfIsSystemTargetKey(Category category)
        {
            if (IsSystemTarget(category))
                throw new ArgumentException(
                    "This overload of NewKey cannot be used to construct an instance of UniqueKey containing a SystemTarget id.");
        }

        private void ThrowIfHasKeyExt(string methodName)
        {
            if (KeyExt != null)
                throw new InvalidOperationException(
                    string.Format(
                        "This overload of {0} cannot be used if the grain uses the primary key extension feature.",
                        methodName));
        }

        /// <summary>
        /// 获取整数主键及其可选的密钥扩展。
        /// </summary>
        /// <param name="extendedKey">当此方法返回时, 此方法包含密钥扩展名, 或没有关键扩展名的 <see langword="null"/> 。</param>
        /// <returns>主键整数。</returns>
        public long PrimaryKeyToLong(out string? extendedKey)
        {
            ThrowIfIsNotLong();

            extendedKey = this.KeyExt;
            return unchecked((long)N1);
        }

        /// <summary>
        /// 获取一个密钥的整数主键,不附加。
        /// </summary>
        /// <returns>主键整数。</returns>
        public long PrimaryKeyToLong()
        {
            ThrowIfIsNotLong();
            ThrowIfHasKeyExt("UniqueKey.PrimaryKeyToLong");
            return (long)N1;
        }

        /// <summary>
        /// 获取 GUID 主密钥及其可选密钥扩展名。
        /// </summary>
        /// <param name="extendedKey">当此方法返回时, 此方法包含密钥扩展名, 或没有关键扩展名的 <see langword="null"/> 。</param>
        /// <returns>图形用户界面数据主键。</returns>
        public Guid PrimaryKeyToGuid(out string? extendedKey)
        {
            extendedKey = this.KeyExt;
            return Guid;
        }

        /// <summary>
        /// 获取一个密钥的图形界面数据主键,不附加。
        /// </summary>
        /// <returns>图形用户界面数据主键。</returns>
        public Guid PrimaryKeyToGuid()
        {
            ThrowIfHasKeyExt("UniqueKey.PrimaryKeyToGuid");
            return Guid;
        }

        /// <inheritdoc />
        public override bool Equals(object? o) => o is UniqueKey key && Equals(key);

        // 我们真的希望平等 尽可能快, 最低成本, 尽可能接近本地。
        // 没有函数呼叫,没有拳击,内嵌。
        /// <inheritdoc />
        public bool Equals(UniqueKey? other)
        {
            return other is not null
                   && N0 == other.N0
                   && N1 == other.N1
                   && TypeCodeData == other.TypeCodeData
                   && (KeyExt is null || KeyExt == other.KeyExt);
        }

        // 我们真的希望 CompareTo 尽快,尽可能地以最低成本接近本地人。
        // 没有函数呼叫,没有拳击,内嵌。
        /// <inheritdoc />
        public int CompareTo(UniqueKey? other)
        {
            if (other is null) return 1;

            return TypeCodeData < other.TypeCodeData ? -1
               : TypeCodeData > other.TypeCodeData ? 1
               : N0 < other.N0 ? -1
               : N0 > other.N0 ? 1
               : N1 < other.N1 ? -1
               : N1 > other.N1 ? 1
               : KeyExt == null ? 0
               : string.CompareOrdinal(KeyExt, other.KeyExt);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return unchecked((int)GetUniformHashCode());
        }

        internal uint GetUniformHashCode()
        {
            // 禁用此 ReSharper 警告; hashCache 是逻辑上只读的变量, 因此在 GetHashCode 中访问它们是安全的 。
            // ReSharper disable NonReadonlyFieldInGetHashCode
            if (uniformHashCache == 0)
            {
                if (KeyExt != null)
                {
                    uniformHashCache = StableHash.ComputeHash(this.ToByteArray());
                }
                else
                {
                    Span<byte> data = stackalloc byte[24];
                    BinaryPrimitives.WriteUInt64LittleEndian(data, TypeCodeData);
                    BinaryPrimitives.WriteUInt64LittleEndian(data[8..], N0);
                    BinaryPrimitives.WriteUInt64LittleEndian(data[16..], N1);
                    uniformHashCache = StableHash.ComputeHash(data);
                }
            }
            return uniformHashCache;
            // ReSharper restore NonReadonlyFieldInGetHashCode
        }

        /// <summary>
        /// 如果 KeyExt 不存在,返回结构
        /// |8 字节|8 字节|8 字节|4 字节|，共 28 字节。
        /// 如果存在 KeyExt ,则添加额外的 KeyExt 字节长度
        /// </summary>
        /// <returns></returns>
        internal ReadOnlySpan<byte> ToByteArray()
        {
            var extBytes = this.KeyExt != null ? Encoding.UTF8.GetBytes(KeyExt) : null;
            var extBytesLength = extBytes?.Length ?? 0;
            var sizeWithoutExtBytes = sizeof(ulong) * 3 + sizeof(int);

            var spanBytes = new byte[sizeWithoutExtBytes + extBytesLength].AsSpan();

            BinaryPrimitives.WriteUInt64LittleEndian(spanBytes, N0);
            BinaryPrimitives.WriteUInt64LittleEndian(spanBytes.Slice(8, 8), N1);
            BinaryPrimitives.WriteUInt64LittleEndian(spanBytes.Slice(16, 8), TypeCodeData);

            const int offset = sizeof(ulong) * 3;
            // 复制 KeyExt
            if (extBytes != null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(spanBytes.Slice(offset, sizeof(int)), extBytesLength);
                extBytes.CopyTo(spanBytes[(offset + sizeof(int))..]);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(spanBytes.Slice(offset, sizeof(int)), -1);
            }

            return spanBytes;
        }

        private unsafe Guid Guid
        {
            get
            {
                if (BitConverter.IsLittleEndian && sizeof(Guid) == 2 * sizeof(ulong))
                {
                    Guid value;
                    ((ulong*)&value)[0] = N0;
                    ((ulong*)&value)[1] = N1;
                    return value;
                }
                return new Guid((uint)N0, (ushort)(N0 >> 32), (ushort)(N0 >> 48), (byte)N1, (byte)(N1 >> 8), (byte)(N1 >> 16), (byte)(N1 >> 24), (byte)(N1 >> 32), (byte)(N1 >> 40), (byte)(N1 >> 48), (byte)(N1 >> 56));
            }
            set
            {
                if (BitConverter.IsLittleEndian && sizeof(Guid) == 2 * sizeof(ulong))
                {
                    N0 = ((ulong*)&value)[0];
                    N1 = ((ulong*)&value)[1];
                }
                else
                {
                    var guid = value.ToByteArray().AsSpan();
                    N0 = BinaryPrimitives.ReadUInt64LittleEndian(guid);
                    N1 = BinaryPrimitives.ReadUInt64LittleEndian(guid[8..]);
                }
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToHexString();
        }

        internal string ToHexString()
        {
            const string format = "{0:x16}{1:x16}{2:x16}";
            return KeyExt is null ? string.Format(format, N0, N1, TypeCodeData)
                : string.Format(format + "+{3}", N0, N1, TypeCodeData, KeyExt);
        }

        internal string ToGrainKeyString()
        {
            string keyString;
            if (HasKeyExt)
            {
                string? extension;
                keyString = IsLongKey ? PrimaryKeyToLong(out extension).ToString() : PrimaryKeyToGuid(out extension).ToString();
                keyString = $"{keyString}+{extension ?? string.Empty}";
            }
            else
            {
                keyString = this.IsLongKey ? PrimaryKeyToLong().ToString() : this.PrimaryKeyToGuid().ToString();
            }
            return keyString;
        }

        internal static Category GetCategory(ulong typeCodeData)
        {
            return (Category)((typeCodeData >> 56) & 0xFF);
        }

        private static ulong GetTypeCodeData(Category category, long typeData = 0) => ((ulong)category << 56) + ((ulong)typeData & 0x00FFFFFFFFFFFFFF);
    }

    /// <summary>
    /// 换算功能 <see cref="UniqueKey"/> 与从这些天起 JSON 代表。
    /// </summary>
    public sealed class UniqueKeyJsonConverter : JsonConverter<UniqueKey>
    {
        private const int MaxBufferSize = 256;

        /// <inheritdoc />
        public override UniqueKey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Span<char> buffer = stackalloc char[MaxBufferSize];
            return GetUniqueKey(ref reader, buffer) ?? throw new JsonException($"Could not deserialize {nameof(UniqueKey)}.");
        }

        private static UniqueKey? GetUniqueKey(ref Utf8JsonReader reader, scoped Span<char> buffer)
        {
            if (reader.TokenType is not JsonTokenType.String and not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Could not deserialize {nameof(UniqueKey)}.");
            }

            if (reader.HasValueSequence)
            {
                var valueLength = checked((int)reader.ValueSequence.Length);
                if (valueLength < buffer.Length)
                {
                    var written = reader.CopyString(buffer);
                    return UniqueKey.ParseCanonical(buffer[..written]);
                }
            }
            else
            {
                if (reader.ValueSpan.Length < buffer.Length)
                {
                    var written = reader.CopyString(buffer);
                    return UniqueKey.ParseCanonical(buffer[..written]);
                }
            }

            var str = reader.GetString();
            return str is null ? null : UniqueKey.ParseCanonical(str);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 是 <see langword="null"/>。</exception>
        public override void Write(Utf8JsonWriter writer, UniqueKey value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteStringValue(value.ToHexString());
        }

        /// <inheritdoc />
        public override UniqueKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Span<char> buffer = stackalloc char[MaxBufferSize];
            return GetUniqueKey(ref reader, buffer) ?? throw new JsonException("Failed to parse UniqueKey from property name.");
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 是 <see langword="null"/>。</exception>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] UniqueKey value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WritePropertyName(value.ToHexString());
        }
    }
}
