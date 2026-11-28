using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示使用 Orleans 旧版 Grain 标识编码的标识符。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class LegacyGrainId : IEquatable<LegacyGrainId>, IComparable<LegacyGrainId>
    {
        private static readonly Interner<UniqueKey, LegacyGrainId> grainIdInternCache = new Interner<UniqueKey, LegacyGrainId>(InternerConstants.SIZE_LARGE);
        private static readonly Interner<UniqueKey, byte[]> grainTypeInternCache = new Interner<UniqueKey, byte[]>();
        private static readonly Interner<UniqueKey, byte[]> grainKeyInternCache = new Interner<UniqueKey, byte[]>();
        private static readonly ReadOnlyMemory<byte> ClientPrefixBytes = Encoding.UTF8.GetBytes(GrainTypePrefix.ClientPrefix + ".");

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        [DataMember]
        [Id(0)]
        internal readonly UniqueKey Key;

        /// <summary>
        /// 获取此标识符中编码的类别。
        /// </summary>
        public UniqueKey.Category Category => Key.IdCategory;

        /// <summary>
        /// 获取一个值，指示此标识符是否表示系统目标。
        /// </summary>
        public bool IsSystemTarget => Key.IsSystemTargetKey;

        /// <summary>
        /// 获取一个值，指示此标识符是否表示 Grain。
        /// </summary>
        public bool IsGrain => Category == UniqueKey.Category.Grain || Category == UniqueKey.Category.KeyExtGrain;

        /// <summary>
        /// 获取一个值，指示此标识符是否表示客户端。
        /// </summary>
        public bool IsClient => Category == UniqueKey.Category.Client;

        internal LegacyGrainId(UniqueKey key)
        {
            this.Key = key;
        }

        /// <summary>
        /// 将旧版 Grain 标识符转换为其 <see cref="GrainId"/> 表示形式。
        /// </summary>
        /// <param name="legacy">要转换的旧版 Grain 标识符。</param>
        /// <returns>等效的 <see cref="GrainId"/>。</returns>
        public static implicit operator GrainId(LegacyGrainId legacy) => legacy.ToGrainId();

        /// <summary>
        /// 创建具有随机生成的主键的 Grain 标识符。
        /// </summary>
        /// <returns>新的旧版 Grain 标识符。</returns>
        public static LegacyGrainId NewId()
        {
            return FindOrCreateGrainId(UniqueKey.NewKey(Guid.NewGuid(), UniqueKey.Category.Grain));
        }

        /// <summary>
        /// 创建具有随机生成的主键的客户端标识符。
        /// </summary>
        /// <returns>新的旧版客户端标识符。</returns>
        public static LegacyGrainId NewClientId()
        {
            return NewClientId(Guid.NewGuid());
        }

        internal static LegacyGrainId NewClientId(Guid id)
        {
            return FindOrCreateGrainId(UniqueKey.NewKey(id, UniqueKey.Category.Client, 0));
        }

        internal static LegacyGrainId GetGrainId(UniqueKey key)
        {
            return FindOrCreateGrainId(key);
        }

        // 仅供测试使用。
        internal static LegacyGrainId GetGrainIdForTesting(Guid guid)
        {
            return FindOrCreateGrainId(UniqueKey.NewKey(guid));
        }

        internal static LegacyGrainId GetSystemTargetGrainId(long typeData)
        {
            return FindOrCreateGrainId(UniqueKey.NewEmptySystemTargetKey(typeData));
        }

        internal static GrainType GetGrainType(long typeCode, bool isKeyExt)
        {
            return GetGrainType(isKeyExt
                ? UniqueKey.NewKey(0, UniqueKey.Category.KeyExtGrain, typeCode, "keyext")
                : UniqueKey.NewKey(0, UniqueKey.Category.Grain, typeCode));
        }

        internal static LegacyGrainId GetGrainId(long typeCode, long primaryKey, string? keyExt = null)
        {
            return FindOrCreateGrainId(UniqueKey.NewKey(primaryKey,
                keyExt == null ? UniqueKey.Category.Grain : UniqueKey.Category.KeyExtGrain,
                typeCode, keyExt));
        }

        internal static LegacyGrainId GetGrainId(long typeCode, Guid primaryKey, string? keyExt = null)
        {
            return FindOrCreateGrainId(UniqueKey.NewKey(primaryKey,
                keyExt == null ? UniqueKey.Category.Grain : UniqueKey.Category.KeyExtGrain,
                typeCode, keyExt));
        }

        internal static LegacyGrainId GetGrainId(long typeCode, string primaryKey)
        {
            return FindOrCreateGrainId(UniqueKey.NewKey(0L,
                UniqueKey.Category.KeyExtGrain,
                typeCode, primaryKey));
        }

        internal static LegacyGrainId GetGrainServiceGrainId(short id, int typeData)
        {
            return FindOrCreateGrainId(UniqueKey.NewGrainServiceKey(id, typeData));
        }

        internal static LegacyGrainId GetGrainServiceGrainId(int typeData, string systemGrainId)
        {
            return FindOrCreateGrainId(UniqueKey.NewGrainServiceKey(systemGrainId, typeData));
        }

        /// <summary>
        /// 获取 GUID 主键。
        /// </summary>
        public Guid PrimaryKey
        {
            get { return GetPrimaryKey(); }
        }

        /// <summary>
        /// 获取整数主键。
        /// </summary>
        public long PrimaryKeyLong
        {
            get { return GetPrimaryKeyLong(); }
        }

        /// <summary>
        /// 获取字符串主键扩展；如果此标识符没有键扩展，则返回空字符串。
        /// </summary>
        public string PrimaryKeyString
        {
            get { return GetPrimaryKeyString(); }
        }

        /// <summary>
        /// 获取此标识符的详细字符串表示形式。
        /// </summary>
        public string IdentityString
        {
            get { return ToDetailedString(); }
        }

        /// <summary>
        /// 获取一个值，指示主键是否编码为 <see cref="long"/>。
        /// </summary>
        public bool IsLongKey
        {
            get { return Key.IsLongKey; }
        }

        /// <summary>
        /// 获取整数主键及其可选的键扩展。
        /// </summary>
        /// <param name="keyExt">此方法返回时，包含键扩展；若不存在，则为 <see langword="null"/>。</param>
        /// <returns>整数主键。</returns>
        public long GetPrimaryKeyLong(out string? keyExt)
        {
            return Key.PrimaryKeyToLong(out keyExt);
        }

        internal long GetPrimaryKeyLong()
        {
            return Key.PrimaryKeyToLong();
        }

        /// <summary>
        /// 获取 GUID 主键及其可选的键扩展。
        /// </summary>
        /// <param name="keyExt">此方法返回时，包含键扩展；若不存在，则为 <see langword="null"/>。</param>
        /// <returns>GUID 主键。</returns>
        public Guid GetPrimaryKey(out string? keyExt)
        {
            return Key.PrimaryKeyToGuid(out keyExt);
        }

        internal Guid GetPrimaryKey()
        {
            return Key.PrimaryKeyToGuid();
        }

        internal string GetPrimaryKeyString()
        {
            _ = GetPrimaryKey(out string? key);
            return key ?? string.Empty;
        }

        /// <summary>
        /// 获取旧版 Grain 类型码。
        /// </summary>
        public int TypeCode => Key.BaseTypeCode;

        private static GrainType GetGrainType(UniqueKey key)
        {
            return new GrainType(grainTypeInternCache.FindOrCreate(key, key =>
            {
                var prefixBytes = key.IsSystemTargetKey ? GrainTypePrefix.SystemTargetPrefixBytes
                    : key.IdCategory == UniqueKey.Category.Client ? ClientPrefixBytes
                    : GrainTypePrefix.LegacyGrainPrefixBytes;

                return CreateGrainType(prefixBytes, key.TypeCodeData);
            }));
        }

        private static byte[] CreateGrainType(ReadOnlyMemory<byte> prefixBytes, ulong typeCode)
        {
            var prefix = prefixBytes.Span;
            var buf = new byte[prefix.Length + 16];
            prefix.CopyTo(buf);
            Utf8Formatter.TryFormat(typeCode, buf.AsSpan(prefix.Length), out var len, new StandardFormat('X', 16));
            Debug.Assert(len == 16);
            return buf;
        }

        /// <summary>
        /// 创建旧版 Grain 类型码的 <see cref="GrainType"/> 表示形式。
        /// </summary>
        /// <param name="typeCode">旧版 Grain 类型码。</param>
        /// <returns>对应的 Grain 类型。</returns>
        public static GrainType CreateGrainTypeForGrain(int typeCode)
        {
            return new GrainType(CreateGrainType(GrainTypePrefix.LegacyGrainPrefixBytes, (ulong)typeCode));
        }

        /// <summary>
        /// 创建旧版系统目标类型码的 <see cref="GrainType"/> 表示形式。
        /// </summary>
        /// <param name="typeCode">旧版系统目标类型码。</param>
        /// <returns>对应的 Grain 类型。</returns>
        public static GrainType CreateGrainTypeForSystemTarget(int typeCode)
        {
            return new GrainType(CreateGrainType(GrainTypePrefix.SystemTargetPrefixBytes, (ulong)typeCode));
        }

        private IdSpan GetGrainKey()
        {
            return new IdSpan(grainKeyInternCache.FindOrCreate(Key, k => Encoding.UTF8.GetBytes($"{k.N0:X16}{k.N1:X16}{(k.HasKeyExt ? "+" : null)}{k.KeyExt}")));
        }

        /// <summary>
        /// 将此旧版标识符转换为其 <see cref="GrainId"/> 表示形式。
        /// </summary>
        /// <returns>等效的 <see cref="GrainId"/>。</returns>
        public GrainId ToGrainId()
        {
            return new GrainId(GetGrainType(Key), this.GetGrainKey());
        }

        /// <summary>
        /// 尝试转换使用旧版 Grain 标识符编码的 <see cref="GrainId"/>。
        /// </summary>
        /// <param name="id">要转换的 Grain 标识符。</param>
        /// <param name="legacyId">此方法返回 <see langword="true"/> 时，包含转换后的旧版标识符。</param>
        /// <returns>返回 <see langword="true"/> 表示 <paramref name="id"/> 使用旧版编码；否则返回 <see langword="false"/>。</returns>
        public static bool TryConvertFromGrainId(GrainId id, [NotNullWhen(true)] out LegacyGrainId? legacyId)
        {
            legacyId = FromGrainIdInternal(id);
            return legacyId is not null;
        }

        /// <summary>
        /// 转换使用旧版 Grain 标识符编码的 <see cref="GrainId"/>。
        /// </summary>
        /// <param name="id">要转换的 Grain 标识符。</param>
        /// <returns>转换后的旧版标识符。</returns>
        public static LegacyGrainId FromGrainId(GrainId id)
        {
            return FromGrainIdInternal(id) ?? ThrowNotLegacyGrainId(id);
        }

        private static LegacyGrainId? FromGrainIdInternal(GrainId id)
        {
            var typeSpan = id.Type.AsSpan();
            if (typeSpan.Length != GrainTypePrefix.LegacyGrainPrefix.Length + 16)
                return null;
            if (!typeSpan.StartsWith(GrainTypePrefix.LegacyGrainPrefixBytes.Span))
                return null;

            typeSpan = typeSpan.Slice(GrainTypePrefix.LegacyGrainPrefix.Length, 16);
            if (!Utf8Parser.TryParse(typeSpan, out ulong typeCodeData, out var len, 'X') || len < 16)
                return null;

            string? keyExt = null;
            var keySpan = id.Key.Value.Span;
            if (keySpan.Length < 32) return null;

            if (!Utf8Parser.TryParse(keySpan[..16], out ulong n0, out len, 'X') || len < 16)
                return null;

            if (!Utf8Parser.TryParse(keySpan.Slice(16, 16), out ulong n1, out len, 'X') || len < 16)
                return null;

            if (keySpan.Length > 32)
            {
                if (keySpan[32] != '+') return null;
                keyExt = Encoding.UTF8.GetString(keySpan[33..]);
            }

            return FindOrCreateGrainId(UniqueKey.NewKey(n0, n1, typeCodeData, keyExt));
        }

        private static LegacyGrainId ThrowNotLegacyGrainId(GrainId id)
        {
            throw new InvalidOperationException($"Cannot convert non-legacy id {id} into legacy id");
        }

        private static LegacyGrainId FindOrCreateGrainId(UniqueKey key)
        {
            return grainIdInternCache.FindOrCreate(key, k => new LegacyGrainId(k));
        }

        /// <inheritdoc />
        public bool Equals(LegacyGrainId? other)
        {
            return other != null && Key.Equals(other.Key);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            var o = obj as LegacyGrainId;
            return o != null && Key.Equals(o.Key);
        }

        // 避免编译器警告：类实现 Equals(...) 时不应缺少 GetHashCode() 方法。
        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }

        /// <summary>
        /// 基于 Jenkins 哈希函数获取此 Grain 的均匀分布哈希码。
        /// 注意：哈希码可能为正数或负数。
        /// </summary>
        /// <returns>此 LegacyGrainId 的哈希码。</returns>
        public uint GetUniformHashCode()
        {
            return Key.GetUniformHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToStringImpl(false);
        }

        // 与 ToString 相同，但包含完整的主键和类型码。
        internal string ToDetailedString()
        {
            return ToStringImpl(true);
        }

        // 与 ToString 相同，但包含完整的主键和类型码。
        private string ToStringImpl(bool detailed)
        {
            // TODO 获取系统/目标 Grain 的名称及 Grain 类型的名称。

            var keyString = Key.ToString();
            // 此处应取得 n1 的低有效半部分，并附加键扩展作为后缀。
            string idString = keyString;
            if (!detailed)
            {
                if (keyString.Length >= 48)
                    idString = keyString.Substring(24, 8) + keyString[48..];
                else
                    idString = keyString.Substring(24, 8);
            }

            string fullString;
            switch (Category)
            {
                case UniqueKey.Category.Grain:
                case UniqueKey.Category.KeyExtGrain:
                    var typeString = TypeCode.ToString("X");
                    if (!detailed) typeString = typeString[Math.Max(0, typeString.Length - 8)..];
                    fullString = $"*grn/{typeString}/{idString}";
                    break;
                case UniqueKey.Category.Client:
                    fullString = $"*cli/{idString}";
                    break;
                case UniqueKey.Category.SystemTarget:
                case UniqueKey.Category.KeyExtSystemTarget:
                    fullString = $"*stg/{Key.N1}/{idString}";
                    break;
                case UniqueKey.Category.SystemGrain:
                    fullString = $"*sgn/{Key.PrimaryKeyToGuid()}/{idString}";
                    break;
                default:
                    fullString = "???/" + idString;
                    break;
            }
            return detailed ? string.Format("{0}-0x{1, 8:X8}", fullString, GetUniformHashCode()) : fullString;
        }

        /// <summary>
        /// 判断类型是否使用某个旧版 Grain 键接口。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <returns>如果类型实现旧版 Grain 键接口，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool IsLegacyGrainType(Type type)
        {
            return typeof(IGrainWithGuidKey).IsAssignableFrom(type)
                || typeof(IGrainWithIntegerKey).IsAssignableFrom(type)
                || typeof(IGrainWithStringKey).IsAssignableFrom(type)
                || typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(type)
                || typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(type);
        }

        /// <summary>
        /// 判断类型是否使用旧版复合 Grain 键接口。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <returns>如果类型实现旧版复合 Grain 键接口，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool IsLegacyKeyExtGrainType(Type type)
        {
            return typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(type)
                || typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(type);
        }

        /// <summary>
        /// 以标准字符串形式返回此 LegacyGrainId，以便之后由 <c>FromParsableString</c> 方法使用。
        /// </summary>
        /// <returns>采用标准字符串格式的 LegacyGrainId。</returns>
        internal string ToParsableString()
        {
            // 注意：此函数必须是 FromParsableString 的逆操作，并且数据必须能够可靠地往返转换。

            return Key.ToHexString();
        }

        /// <summary>
        /// 通过解析由 <c>ToParsableString</c> 方法返回的标准格式字符串创建新的 LegacyGrainId 对象。
        /// </summary>
        /// <param name="grainId">包含要解析的 LegacyGrainId 信息的字符串。</param>
        /// <returns>根据输入数据创建的新 LegacyGrainId 对象。</returns>
        internal static LegacyGrainId FromParsableString(string grainId)
        {
            return FromParsableString(grainId.AsSpan());
        }

        /// <summary>
        /// 通过解析由 <c>ToParsableString</c> 方法返回的标准格式字符串创建新的 LegacyGrainId 对象。
        /// </summary>
        /// <param name="grainId">包含要解析的 LegacyGrainId 信息的字符串。</param>
        /// <returns>根据输入数据创建的新 LegacyGrainId 对象。</returns>
        internal static LegacyGrainId FromParsableString(ReadOnlySpan<char> grainId)
        {
            // 注意：此函数必须是 ToParsableString 的逆操作，并且数据必须能够可靠地往返转换。

            var key = UniqueKey.Parse(grainId);
            return FindOrCreateGrainId(key);
        }

        /// <summary>
        /// 获取按指定值取模后的非负哈希码。
        /// </summary>
        /// <param name="umod">模数。</param>
        /// <returns>取值范围从 0 到 <paramref name="umod"/> 减 1 的哈希码。</returns>
        public uint GetHashCode_Modulo(uint umod)
        {
            int key = Key.GetHashCode();
            int mod = (int)umod;
            key = ((key % mod) + mod) % mod; // 此时键应该为非负值，因此使用 checked 进行检查。
            return checked((uint)key);
        }

        /// <inheritdoc />
        public int CompareTo(LegacyGrainId? other)
        {
            if (other is null) return 1; // null 小于任何非 null 实例。
            return Key.CompareTo(other.Key);
        }
    }
}
