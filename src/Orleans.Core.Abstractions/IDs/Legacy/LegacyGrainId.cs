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
    /// 使用遗留 Orleans grain 身份编码代表标识符。
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
        /// 获取此标识符编码的分类。
        /// </summary>
        public UniqueKey.Category Category => Key.IdCategory;

        /// <summary>
        /// 获得一个值, 显示此标识符是否代表系统目标 。
        /// </summary>
        public bool IsSystemTarget => Key.IsSystemTargetKey;

        /// <summary>
        /// 获得一个值,以显示该标识符是否代表 grain。
        /// </summary>
        public bool IsGrain => Category == UniqueKey.Category.Grain || Category == UniqueKey.Category.KeyExtGrain;

        /// <summary>
        /// 获得一个值, 显示此标识符是否代表客户端 。
        /// </summary>
        public bool IsClient => Category == UniqueKey.Category.Client;

        internal LegacyGrainId(UniqueKey key)
        {
            this.Key = key;
        }

        /// <summary>
        /// 将遗留 grain 标识符转换为 <see cref="GrainId"/> 表示。
        /// </summary>
        /// <param name="legacy">要转换的遗留 grain 标识符 。</param>
        /// <returns>等同 <see cref="GrainId"/>.</returns>
        public static implicit operator GrainId(LegacyGrainId legacy) => legacy.ToGrainId();

        /// <summary>
        /// 创建 grain 标识符,并随机生成主密钥。
        /// </summary>
        /// <returns>新遗留 grain 标识符。</returns>
        public static LegacyGrainId NewId()
        {
            return FindOrCreateGrainId(UniqueKey.NewKey(Guid.NewGuid(), UniqueKey.Category.Grain));
        }

        /// <summary>
        /// 创建客户端标识符,并随机生成主密钥。
        /// </summary>
        /// <returns>一个新的遗留客户端识别符号。</returns>
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

        // 仅用于测试。
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
        /// 获取 GUID 主密钥 。
        /// </summary>
        public Guid PrimaryKey
        {
            get { return GetPrimaryKey(); }
        }

        /// <summary>
        /// 获得整数主键。
        /// </summary>
        public long PrimaryKeyLong
        {
            get { return GetPrimaryKeyLong(); }
        }

        /// <summary>
        /// 获取字符串主密钥扩展名,或者如果此标识符没有密钥扩展名,则获得空字符串。
        /// </summary>
        public string PrimaryKeyString
        {
            get { return GetPrimaryKeyString(); }
        }

        /// <summary>
        /// 获取此标识符的详细字符串代表。
        /// </summary>
        public string IdentityString
        {
            get { return ToDetailedString(); }
        }

        /// <summary>
        /// 获得一个值,显示主密钥是否编码为 <see cref="long"/>。
        /// </summary>
        public bool IsLongKey
        {
            get { return Key.IsLongKey; }
        }

        /// <summary>
        /// 获取整数主键及其可选的密钥扩展。
        /// </summary>
        /// <param name="keyExt">当此方法返回时, 此方法包含密钥扩展名, 或没有关键扩展名的 <see langword="null"/> 。</param>
        /// <returns>主键整数。</returns>
        public long GetPrimaryKeyLong(out string? keyExt)
        {
            return Key.PrimaryKeyToLong(out keyExt);
        }

        internal long GetPrimaryKeyLong()
        {
            return Key.PrimaryKeyToLong();
        }

        /// <summary>
        /// 获取 GUID 主密钥及其可选密钥扩展名。
        /// </summary>
        /// <param name="keyExt">当此方法返回时, 此方法包含密钥扩展名, 或没有关键扩展名的 <see langword="null"/> 。</param>
        /// <returns>图形用户界面数据主键。</returns>
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
        /// 获取遗留 grain 类型代码。
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
        /// 创建 <see cref="GrainType"/> 代表遗产 grain 代码 。
        /// </summary>
        /// <param name="typeCode">遗留 grain 类型代码。</param>
        /// <returns>对应对应 grain 类型。</returns>
        public static GrainType CreateGrainTypeForGrain(int typeCode)
        {
            return new GrainType(CreateGrainType(GrainTypePrefix.LegacyGrainPrefixBytes, (ulong)typeCode));
        }

        /// <summary>
        /// 创建遗留系统目标型号的 <see cref="GrainType"/> 表示方式。
        /// </summary>
        /// <param name="typeCode">遗留系统目标类型代码。</param>
        /// <returns>对应对应 grain 类型。</returns>
        public static GrainType CreateGrainTypeForSystemTarget(int typeCode)
        {
            return new GrainType(CreateGrainType(GrainTypePrefix.SystemTargetPrefixBytes, (ulong)typeCode));
        }

        private IdSpan GetGrainKey()
        {
            return new IdSpan(grainKeyInternCache.FindOrCreate(Key, k => Encoding.UTF8.GetBytes($"{k.N0:X16}{k.N1:X16}{(k.HasKeyExt ? "+" : null)}{k.KeyExt}")));
        }

        /// <summary>
        /// 将此遗留标识符转换为 <see cref="GrainId"/> 表示 。
        /// </summary>
        /// <returns>等同 <see cref="GrainId"/>.</returns>
        public GrainId ToGrainId()
        {
            return new GrainId(GetGrainType(Key), this.GetGrainKey());
        }

        /// <summary>
        /// 试图转换 <see cref="GrainId"/> 使用遗留 grain 标识符编码。
        /// </summary>
        /// <param name="id">要转换的 grain 标识符。</param>
        /// <param name="legacyId">当此方法返回 <see langword="true"/>时, 包含转换成的遗留标识符。</param>
        /// <returns><see langword="true"/> 如果 <paramref name="id"/> 使用遗留编码;否则, <see langword="false"/>。</returns>
        public static bool TryConvertFromGrainId(GrainId id, [NotNullWhen(true)] out LegacyGrainId? legacyId)
        {
            legacyId = FromGrainIdInternal(id);
            return legacyId is not null;
        }

        /// <summary>
        /// 转换 <see cref="GrainId"/> 使用遗留 grain 标识符编码。
        /// </summary>
        /// <param name="id">要转换的 grain 标识符。</param>
        /// <returns>转换的遗留标识符。</returns>
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

        // 保持汇编器的快乐状态 - 它不喜欢没有 GetHashCode() 方法而有等(...) 的类
        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }

        /// <summary>
        /// 根据詹金斯·哈希函数,获得此 grain的分布统一的散列数代码值。
        /// 注:散数代码值可以是正值或非正值。
        /// </summary>
        /// <returns>这个 LegacyGrainId[ 的散数代码</returns>
        public uint GetUniformHashCode()
        {
            return Key.GetUniformHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToStringImpl(false);
        }

        // 与 ToString相同,仅完全主键和型号代码
        internal string ToDetailedString()
        {
            return ToStringImpl(true);
        }

        // 与 ToString相同,仅完全主键和型号代码
        private string ToStringImpl(bool detailed)
        {
            // TODO 获取系统/目标 grain + grain 类型名称的系统/目标的名称

            var keyString = Key.ToString();
            // 它将抓住n1中最小的半个 N1, 并用密钥扩展名将其后缀。
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
        /// 确定类型是否使用遗留 grain 密钥界面。
        /// </summary>
        /// <param name="type">检查的类型。</param>
        /// <returns><see langword="true"/> 如果类型执行遗留的 grain 密钥界面; 否则, <see langword="false"/>.</returns>
        public static bool IsLegacyGrainType(Type type)
        {
            return typeof(IGrainWithGuidKey).IsAssignableFrom(type)
                || typeof(IGrainWithIntegerKey).IsAssignableFrom(type)
                || typeof(IGrainWithStringKey).IsAssignableFrom(type)
                || typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(type)
                || typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(type);
        }

        /// <summary>
        /// 确定类型是否使用遗留化合物 grain 密钥界面。
        /// </summary>
        /// <param name="type">检查的类型。</param>
        /// <returns><see langword="true"/> 如果类型安装了遗留化合物 grain 密钥接口;否则, <see langword="false"/>。</returns>
        public static bool IsLegacyKeyExtGrainType(Type type)
        {
            return typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(type)
                || typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(type);
        }

        /// <summary>
        /// 将此 LegacyGrainId 以标准字符串形式返回,适合以后使用 <c>FromParsableString</c> 方法。
        /// </summary>
        /// <returns>LegacyGrainId 以标准字符串格式。</returns>
        internal string ToParsableString()
        {
            // 注:此函数必须是 FromParsableString的“反向”,数据必须可靠地圆行。

            return Key.ToHexString();
        }

        /// <summary>
        /// 以标准格式解析字符串创建新的 LegacyGrainId 对象,该格式从 <c>ToParsableString</c> 方法返回。
        /// </summary>
        /// <param name="grainId">字符串中含有要解析的 LegacyGrainId 信息。</param>
        /// <returns>新建输入数据创建的 LegacyGrainId 对象。</returns>
        internal static LegacyGrainId FromParsableString(string grainId)
        {
            return FromParsableString(grainId.AsSpan());
        }

        /// <summary>
        /// 以标准格式解析字符串创建新的 LegacyGrainId 对象,该格式从 <c>ToParsableString</c> 方法返回。
        /// </summary>
        /// <param name="grainId">字符串中含有要解析的 LegacyGrainId 信息。</param>
        /// <returns>新建输入数据创建的 LegacyGrainId 对象。</returns>
        internal static LegacyGrainId FromParsableString(ReadOnlySpan<char> grainId)
        {
            // 注:此函数必须是 ToParsableString的“反向”,数据必须可靠地圆行。

            var key = UniqueKey.Parse(grainId);
            return FindOrCreateGrainId(key);
        }

        /// <summary>
        /// 获得非负性的散列代码,减少指定值的模末。
        /// </summary>
        /// <param name="umod">模量。</param>
        /// <returns>散列代码在零至 <paramref name="umod"/> 减一之间。</returns>
        public uint GetHashCode_Modulo(uint umod)
        {
            int key = Key.GetHashCode();
            int mod = (int)umod;
            key = ((key % mod) + mod) % mod; // 键应该为正。所以要用选中的来表示正确 。
            return checked((uint)key);
        }

        /// <inheritdoc />
        public int CompareTo(LegacyGrainId? other)
        {
            if (other is null) return 1; // 内无小于任何非null 实例
            return Key.CompareTo(other.Key);
        }
    }
}
