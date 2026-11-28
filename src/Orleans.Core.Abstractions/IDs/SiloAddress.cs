using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 封装 Silo 地址详细信息的数据类。
    /// </summary>
    [Serializable, Immutable]
    [JsonConverter(typeof(SiloAddressConverter))]
    [DebuggerDisplay("SiloAddress {ToString()}")]
    [SuppressReferenceTracking]
    public sealed class SiloAddress :
        IEquatable<SiloAddress>,
        IComparable<SiloAddress>,
        ISpanFormattable,
        IParsable<SiloAddress>,
        IUtf8SpanParsable<SiloAddress>
    {
        [NonSerialized]
        private int hashCode;

        [NonSerialized]
        private bool hashCodeSet;

        [NonSerialized]
        private ImmutableArray<uint> uniformHashCache;

        /// <summary>
        /// 获取端点。
        /// </summary>
        [Id(0)]
        public IPEndPoint Endpoint { get; }

        /// <summary>
        /// 获取世代编号。
        /// </summary>
        [Id(1)]
        public int Generation { get; }

        [NonSerialized]
        private byte[]? utf8;

        private const char SEPARATOR = '@';

        private static readonly object LastGenerationLock = new();
        private static long LastGeneration = 0;
        private static readonly long epoch = new DateTime(2022, 1, 1).Ticks;

        private static readonly Interner<(IPAddress Address, int Port, int Generation), SiloAddress> siloAddressInterningCache = new(InternerConstants.SIZE_MEDIUM);

        /// <summary>获取表示空 <see cref="SiloAddress"/> 的特殊常量值。</summary>
        public static SiloAddress Zero { get; } = New(new IPAddress(0), 0, 0);

        /// <summary>
        /// 用于根据指定的 IP 端点地址及 Silo 世代编号创建新 SiloAddress 的工厂。
        /// </summary>
        /// <param name="ep">Silo 的 IP 端点地址。</param>
        /// <param name="gen">Silo 的世代编号。</param>
        /// <returns>使用指定地址及 Silo 世代编号初始化的 SiloAddress 对象。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ep"/> 为 <see langword="null"/>。</exception>
        public static SiloAddress New(IPEndPoint ep, int gen)
        {
            ArgumentNullException.ThrowIfNull(ep);
            return siloAddressInterningCache.FindOrCreate((ep.Address, ep.Port, gen),
                // 规范化端点。
                (k, ep) => k.Address.IsIPv4MappedToIPv6 ? New(k.Address.MapToIPv4(), k.Port, k.Generation) : new(ep, k.Generation), ep);
        }

        /// <summary>
        /// 用于根据指定的 IP 端点地址及 Silo 世代编号创建新 SiloAddress 的工厂。
        /// </summary>
        /// <param name="address">Silo 的 IP 地址。</param>
        /// <param name="port">端口号。</param>
        /// <param name="generation">Silo 的世代编号。</param>
        /// <returns>使用指定地址及 Silo 世代编号初始化的 SiloAddress 对象。</returns>
        public static SiloAddress New(IPAddress address, int port, int generation)
        {
            return siloAddressInterningCache.FindOrCreate((address, port, generation),
                // 规范化端点。
                k => k.Address.IsIPv4MappedToIPv6 ? New(k.Address.MapToIPv4(), k.Port, k.Generation) : new(new(k.Address, k.Port), k.Generation));
        }

        /// <summary>
        /// 初始化 <see cref="SiloAddress"/> 类的新实例。
        /// </summary>
        /// <param name="endpoint">端点。</param>
        /// <param name="generation">世代编号。</param>
        private SiloAddress(IPEndPoint endpoint, int generation)
        {
            Endpoint = endpoint;
            Generation = generation;
        }

        /// <summary>
        /// 获取一个值，指示此实例是否表示客户端（而非服务器）。
        /// </summary>
        public bool IsClient { get { return Generation < 0; } }

        /// <summary>分配新的 Silo 世代编号。</summary>
        /// <returns>新的 Silo 世代编号。</returns>
        public static int AllocateNewGeneration()
        {
            long elapsed = (DateTime.UtcNow.Ticks - epoch) / TimeSpan.TicksPerSecond;

            // 对于在 1 秒内重启 Silo 的测试，需要确保世代编号始终递增，
            // 因为重启的 Silo 必须具有唯一的世代编号。
            lock (LastGenerationLock)
            {
                LastGeneration = elapsed = Math.Max(elapsed, LastGeneration + 1);
            }

            return unchecked((int)elapsed); // 使用 unchecked 截断低 32 位之外的所有位。
        }

        internal static int GetGenerationRetirementFloor(DateTime utcNow, TimeSpan retirementAge)
        {
            var elapsedTicks = utcNow.Ticks - epoch;
            if (retirementAge.Ticks >= elapsedTicks)
            {
                return 0;
            }

            // 世代编号标识整秒。向上取整，使排他下限涵盖
            // 严格早于两整秒之间某个截止时间的世代编号。
            return (int)Math.Min(
                (elapsedTicks - retirementAge.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond,
                int.MaxValue);
        }

        /// <summary>
        /// 以标准字符串形式返回此 SiloAddress，以便之后由 <c>FromParsableString</c> 方法使用。
        /// </summary>
        /// <returns>采用标准字符串格式的 SiloAddress。</returns>
        public string ToParsableString()
        {
            // 此方法必须是 FromParsableString 的逆操作，并且在部署中的所有 Silo 上保持一致。
            // 原则上，除非 SiloAddress 的数据内容发生改变，否则此方法永远不应变更。
            if (utf8 != null) return Encoding.UTF8.GetString(utf8);
            return $"{new SpanFormattableIPAddress(Endpoint.Address)}:{Endpoint.Port}@{Generation}";
        }

        /// <summary>
        /// 返回此实例的 UTF-8 编码表示形式，类型为字节数组。
        /// </summary>
        /// <returns>此实例的 UTF-8 编码表示形式，类型为字节数组。</returns>
        internal byte[] ToUtf8String()
        {
            if (utf8 is null)
            {
                Span<char> chars = stackalloc char[45];
                var addr = Endpoint.Address.TryFormat(chars, out var len) ? chars[..len] : Endpoint.Address.ToString().AsSpan();
                var size = Encoding.UTF8.GetByteCount(addr);

                // 分配足够空间用于：address + ':' + port + '@' + generation。
                Span<byte> buf = stackalloc byte[size + 1 + 11 + 1 + 11];
                size = Encoding.UTF8.GetBytes(addr, buf);

                buf[size++] = (byte)':';
                var success = Utf8Formatter.TryFormat(Endpoint.Port, buf[size..], out len);
                Debug.Assert(success);
                Debug.Assert(len > 0);
                Debug.Assert(len <= 11);
                size += len;

                buf[size++] = (byte)SEPARATOR;
                success = Utf8Formatter.TryFormat(Generation, buf[size..], out len);
                Debug.Assert(success);
                Debug.Assert(len > 0);
                Debug.Assert(len <= 11);
                size += len;

                utf8 = buf[..size].ToArray();
            }

            return utf8;
        }

        /// <summary>
        /// 通过解析由 <c>ToParsableString</c> 方法返回的标准格式字符串创建新的 SiloAddress 对象。
        /// </summary>
        /// <param name="addr">包含要解析的 SiloAddress 信息的字符串。</param>
        /// <returns>根据输入数据创建的新 SiloAddress 对象。</returns>
        public static SiloAddress FromParsableString(string addr)
            => Parse(addr, null);

        /// <summary>
        /// 解析 <see cref="SiloAddress"/>，输入为 <see cref="ToParsableString"/> 返回的标准格式字符串。
        /// </summary>
        /// <param name="value">包含要解析的 <see cref="SiloAddress"/> 信息的字符串。</param>
        /// <returns>从输入数据中解析出的 <see cref="SiloAddress"/>。</returns>
        public static SiloAddress Parse(string value)
            => Parse(value, null);

        /// <summary>
        /// 解析 <see cref="SiloAddress"/>，输入为 <see cref="ToParsableString"/> 返回的标准格式字符串。
        /// </summary>
        /// <param name="value">包含要解析的 <see cref="SiloAddress"/> 信息的字符串。</param>
        /// <param name="provider">提供区域性相关格式信息的对象。此参数会被忽略。</param>
        /// <returns>从输入数据中解析出的 <see cref="SiloAddress"/>。</returns>
        public static SiloAddress Parse(string value, IFormatProvider? provider = null)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!TryParse(value, provider, out var result))
            {
                throw new FormatException("Invalid string SiloAddress: " + value);
            }

            return result;
        }

        /// <summary>
        /// 尝试解析 <see cref="SiloAddress"/>，输入为 <see cref="ToParsableString"/> 返回的标准格式字符串。
        /// </summary>
        /// <param name="value">包含要解析的 <see cref="SiloAddress"/> 信息的字符串。</param>
        /// <param name="result">解析得到的 <see cref="SiloAddress"/>；若解析失败，则为 <see langword="null"/>。</param>
        /// <returns>如果解析成功，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out SiloAddress? result)
            => TryParse(value, null, out result);

        /// <summary>
        /// 尝试解析 <see cref="SiloAddress"/>，输入为 <see cref="ToParsableString"/> 返回的标准格式字符串。
        /// </summary>
        /// <param name="value">包含要解析的 <see cref="SiloAddress"/> 信息的字符串。</param>
        /// <param name="provider">提供区域性相关格式信息的对象。此参数会被忽略。</param>
        /// <param name="result">解析得到的 <see cref="SiloAddress"/>；若解析失败，则为 <see langword="null"/>。</param>
        /// <returns>如果解析成功，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryParse([NotNullWhen(true)] string? value, IFormatProvider? provider, [NotNullWhen(true)] out SiloAddress? result)
        {
            if (value is null)
            {
                result = null;
                return false;
            }

            return TryParse(value.AsSpan(), out result);
        }

        private static bool TryParse(ReadOnlySpan<char> addr, [NotNullWhen(true)] out SiloAddress? result)
        {
            // 此方法必须是 ToParsableString 的逆操作，并且在部署中的所有 Silo 上保持一致。
            // 原则上，除非 SiloAddress 的数据内容发生改变，否则此方法永远不应变更。

            // 首先是 IPEndpoint，然后是 '@'，最后是世代编号。
            var atSign = addr.LastIndexOf(SEPARATOR);
            if (atSign < 0)
            {
                result = null;
                return false;
            }

            // IPEndpoint 依次为主机、':' 和端口。
            var endpoint = addr[..atSign];
            var lastColon = endpoint.LastIndexOf(':');
            if (lastColon < 0
                || !IPAddress.TryParse(endpoint[..lastColon], out var host)
                || !int.TryParse(endpoint[(lastColon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                || port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort
                || !int.TryParse(addr[(atSign + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var gen))
            {
                result = null;
                return false;
            }

            result = New(host, port, gen);
            return true;
        }

        /// <summary>
        /// 通过解析由 <c>ToParsableString</c> 方法返回的标准格式字符串创建新的 SiloAddress 对象。
        /// </summary>
        /// <param name="addr">包含要解析的 SiloAddress 信息的字符串。</param>
        /// <returns>根据输入数据创建的新 SiloAddress 对象。</returns>
        public static SiloAddress FromUtf8String(ReadOnlySpan<byte> addr)
            => Parse(addr, null);

        /// <summary>
        /// 从 UTF-8 文本中解析 <see cref="SiloAddress"/>，文本采用 <see cref="ToParsableString"/> 返回的标准格式。
        /// </summary>
        /// <param name="utf8Text">包含要解析的 <see cref="SiloAddress"/> 信息的 UTF-8 文本。</param>
        /// <returns>从输入数据中解析出的 <see cref="SiloAddress"/>。</returns>
        public static SiloAddress Parse(ReadOnlySpan<byte> utf8Text)
            => Parse(utf8Text, null);

        /// <summary>
        /// 从 UTF-8 文本中解析 <see cref="SiloAddress"/>，文本采用 <see cref="ToParsableString"/> 返回的标准格式。
        /// </summary>
        /// <param name="utf8Text">包含要解析的 <see cref="SiloAddress"/> 信息的 UTF-8 文本。</param>
        /// <param name="provider">提供区域性相关格式信息的对象。此参数会被忽略。</param>
        /// <returns>从输入数据中解析出的 <see cref="SiloAddress"/>。</returns>
        public static SiloAddress Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider = null)
        {
            if (!TryParse(utf8Text, provider, out var result))
            {
                ThrowInvalidUtf8SiloAddress(utf8Text);
            }

            return result;
        }

        /// <summary>
        /// 尝试从 UTF-8 文本中解析 <see cref="SiloAddress"/>，文本采用 <see cref="ToParsableString"/> 返回的标准格式。
        /// </summary>
        /// <param name="utf8Text">包含要解析的 <see cref="SiloAddress"/> 信息的 UTF-8 文本。</param>
        /// <param name="result">解析得到的 <see cref="SiloAddress"/>；若解析失败，则为 <see langword="null"/>。</param>
        /// <returns>如果解析成功，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out SiloAddress? result)
            => TryParse(utf8Text, null, out result);

        /// <summary>
        /// 尝试从 UTF-8 文本中解析 <see cref="SiloAddress"/>，文本采用 <see cref="ToParsableString"/> 返回的标准格式。
        /// </summary>
        /// <param name="utf8Text">包含要解析的 <see cref="SiloAddress"/> 信息的 UTF-8 文本。</param>
        /// <param name="provider">提供区域性相关格式信息的对象。此参数会被忽略。</param>
        /// <param name="result">解析得到的 <see cref="SiloAddress"/>；若解析失败，则为 <see langword="null"/>。</param>
        /// <returns>如果解析成功，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [NotNullWhen(true)] out SiloAddress? result)
        {
            // 此方法必须是 ToParsableString 的逆操作，并且在部署中的所有 Silo 上保持一致。
            // 原则上，除非 SiloAddress 的数据内容发生改变，否则此方法永远不应变更。

            // 首先是 IPEndpoint，然后是 '@'，最后是世代编号。
            var atSign = utf8Text.LastIndexOf((byte)SEPARATOR);
            if (atSign < 0)
            {
                result = null;
                return false;
            }

            // IPEndpoint 依次为主机、':' 和端口。
            var endpointSlice = utf8Text[..atSign];
            int lastColon = endpointSlice.LastIndexOf((byte)':');
            if (lastColon < 0)
            {
                result = null;
                return false;
            }

            var ipSlice = endpointSlice[..lastColon];
            Span<char> buf = stackalloc char[45];
            var hostString = Encoding.UTF8.GetCharCount(ipSlice) is int len && len <= buf.Length
                ? buf[..Encoding.UTF8.GetChars(ipSlice, buf)]
                : Encoding.UTF8.GetString(ipSlice).AsSpan();
            if (!IPAddress.TryParse(hostString, out var host))
            {
                result = null;
                return false;
            }

            var portSlice = endpointSlice[(lastColon + 1)..];
            if (!Utf8Parser.TryParse(portSlice, out int port, out len)
                || len != portSlice.Length
                || port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            {
                result = null;
                return false;
            }

            var genSlice = utf8Text[(atSign + 1)..];
            if (!Utf8Parser.TryParse(genSlice, out int generation, out len) || len != genSlice.Length)
            {
                result = null;
                return false;
            }

            result = New(host, port, generation);
            return true;
        }

        [DoesNotReturn]
        private static void ThrowInvalidUtf8SiloAddress(ReadOnlySpan<byte> addr)
            => throw new FormatException("Invalid string SiloAddress: " + Encoding.UTF8.GetString(addr));

        /// <summary>
        /// 返回此 SiloAddress 的长字符串表示形式。
        /// </summary>
        /// <remarks>
        /// 注意：此字符串值与 <see cref="FromParsableString"/> 方法不兼容，请使用 <see cref="ToParsableString"/> 方法实现该目的。
        /// </remarks>
        /// <returns>此 SiloAddress 的字符串表示形式。</returns>
        public override string ToString() => $"{this}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => format == "H" ? ToStringWithHashCode() : ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            if (!destination.TryWrite($"{(IsClient ? 'C' : 'S')}{new SpanFormattableIPEndPoint(Endpoint)}:{Generation}", out charsWritten))
                return false;

            if (format.Length == 1 && format[0] == 'H')
            {
                if (!destination[charsWritten..].TryWrite($"/x{GetConsistentHashCode():X8}", out var len))
                {
                    charsWritten = 0;
                    return false;
                }

                charsWritten += len;
            }

            return true;
        }

        /// <summary>
        /// 返回此 SiloAddress 的长字符串表示形式，包括其一致性哈希值。
        /// </summary>
        /// <remarks>
        /// 注意：此字符串值与 <c>FromParsableString</c> 方法不兼容，请使用 <c>ToParsableString</c> 方法实现该目的。
        /// </remarks>
        /// <returns>此 SiloAddress 的字符串表示形式。</returns>
        public string ToStringWithHashCode() => $"{this:H}";

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as SiloAddress);

        /// <inheritdoc />
        public override int GetHashCode() => Endpoint.GetHashCode() ^ Generation;

        /// <summary>判断两个 Silo 地址是否具有相同的端点及世代编号。</summary>
        /// <param name="left">第一个地址。</param>
        /// <param name="right">第二个地址。</param>
        /// <returns>如果两个地址均为 null 或表示同一个 Silo，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(SiloAddress? left, SiloAddress? right)
            => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

        /// <summary>判断两个 Silo 地址的端点或世代编号是否不同。</summary>
        /// <param name="left">第一个地址。</param>
        /// <param name="right">第二个地址。</param>
        /// <returns>如果两个地址不同，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(SiloAddress? left, SiloAddress? right) => !(left == right);

        /// <summary>返回此 Silo 地址的一致性哈希值。</summary>
        /// <returns>此 Silo 地址的一致性哈希值。</returns>
        public int GetConsistentHashCode() => hashCodeSet ? hashCode : CalculateConsistentHashCode();

        /// <summary>返回此 Silo 地址的一致性哈希值。</summary>
        /// <returns>此 Silo 地址的一致性哈希值。</returns>
        internal int GetConsistentHashCode(int seed)
        {
            var tmp = (0, 0L, 0L, 0L); // 使用固定大小的缓冲区避免 stackalloc 开销。
            var buf = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref tmp, 1))[..28];

            Endpoint.Address.TryWriteBytes(buf, out var len);
            Debug.Assert(len is 4 or 16);

            BinaryPrimitives.WriteInt32LittleEndian(buf[16..], Endpoint.Port);
            BinaryPrimitives.WriteInt32LittleEndian(buf[20..], Generation);
            BinaryPrimitives.WriteInt32LittleEndian(buf[24..], seed);

            return (int)StableHash.ComputeHash(buf);
        }

        private int CalculateConsistentHashCode()
        {
            var tmp = (0L, 0L, 0L); // 使用固定大小的缓冲区避免 stackalloc 开销。
            var buf = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref tmp, 1))[..24];

            Endpoint.Address.TryWriteBytes(buf, out var len);
            Debug.Assert(len is 4 or 16);

            BinaryPrimitives.WriteInt32LittleEndian(buf[16..], Endpoint.Port);
            BinaryPrimitives.WriteInt32LittleEndian(buf[20..], Generation);

            hashCode = (int)StableHash.ComputeHash(buf);
            hashCodeSet = true;
            return hashCode;
        }

        internal void InternalSetConsistentHashCode(int hashCode)
        {
            this.hashCode = hashCode;
            this.hashCodeSet = true;
        }

        /// <summary>
        /// 返回此实例的均匀分布哈希码变体的不可变集合。
        /// </summary>
        /// <param name="numHashes">要返回的哈希码数量。</param>
        /// <returns>此实例的均匀分布哈希码变体的不可变集合。</returns>
        /// <remarks>
        /// 结果会被缓存，请求相同数量哈希码的调用共享其底层存储。
        /// </remarks>
        public ImmutableArray<uint> GetUniformHashCodes(int numHashes)
        {
            var cache = uniformHashCache;
            if (!cache.IsDefault && cache.Length == numHashes) return cache;
            return uniformHashCache = GetUniformHashCodesImpl(numHashes);
        }

        private ImmutableArray<uint> GetUniformHashCodesImpl(int numHashes)
        {
            Span<byte> bytes = stackalloc byte[16 + sizeof(int) + sizeof(int) + sizeof(int)]; // ip + port + generation + extraBit

            // 端点 IP 地址。
            var address = Endpoint.Address;
            if (address.AddressFamily == AddressFamily.InterNetwork) // IPv4
            {
#pragma warning disable CS0618 // Type or member is obsolete
                BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], (int)address.Address);
#pragma warning restore CS0618
                bytes[..12].Clear();
            }
            else // IPv6
            {
                address.TryWriteBytes(bytes, out var len);
                Debug.Assert(len == 16);
            }
            var offset = 16;
            // 端口。
            BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], Endpoint.Port);
            offset += sizeof(int);
            // 世代编号。
            BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], Generation);
            offset += sizeof(int);

            var hashes = new uint[numHashes];
            for (int extraBit = 0; extraBit < numHashes; extraBit++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], extraBit);
                hashes[extraBit] = StableHash.ComputeHash(bytes);
            }

            return ImmutableCollectionsMarshal.AsImmutableArray(hashes);
        }

        /// <summary>
        /// 两个 Silo 地址相等，或者其中一个的世代编号为 0 时，两者匹配。
        /// </summary>
        /// <param name="other">要与此 SiloAddress 比较的另一个 SiloAddress。</param>
        /// <returns>返回 <c>true</c> 表示两个 SiloAddress 被视为匹配，即它们相等或其中一个的世代编号为 0。</returns>
        internal bool Matches([NotNullWhen(true)] SiloAddress? other)
        {
            return other is not null && Endpoint.Address.Equals(other.Endpoint.Address) && Endpoint.Port == other.Endpoint.Port &&
                (Generation == other.Generation || Generation == 0 || other.Generation == 0);
        }

        /// <inheritdoc/>
        public bool Equals([NotNullWhen(true)] SiloAddress? other)
            => other is not null && Generation == other.Generation && Endpoint.Address.Equals(other.Endpoint.Address) && Endpoint.Port == other.Endpoint.Port;

        /// <summary>
        /// 如果提供的值与此值表示同一逻辑服务器，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="other">
        /// 另一个实例。
        /// </param>
        /// <returns>
        /// 如果提供的值与此值表示同一逻辑服务器，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </returns>
        internal bool IsSameLogicalSilo([NotNullWhen(true)] SiloAddress? other)
            => other is not null && Endpoint.Address.Equals(other.Endpoint.Address) && Endpoint.Port == other.Endpoint.Port;

        /// <summary>
        /// 如果提供的值与此值表示同一逻辑服务器，且为此服务器的后继实例，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="other">
        /// 另一个实例，或者传入 <see langword="null"/> 以返回 <see langword="false"/>。
        /// </param>
        /// <returns>
        /// 如果提供的值与此值表示同一逻辑服务器，且为此服务器的后继实例，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </returns>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "IsSameLogicalSilo returns false for null, short-circuiting the dereference and preserving the null-tolerant predicate contract.")]
        public bool IsSuccessorOf(SiloAddress other) => IsSameLogicalSilo(other) && other.Generation > 0 && Generation > other.Generation;

        /// <summary>
        /// 如果提供的值与此值表示同一逻辑服务器，且为此服务器的前任实例，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="other">
        /// 另一个实例，或者传入 <see langword="null"/> 以返回 <see langword="false"/>。
        /// </param>
        /// <returns>
        /// 如果提供的值与此值表示同一逻辑服务器，且为此服务器的前任实例，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </returns>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "IsSameLogicalSilo returns false for null, short-circuiting the dereference and preserving the null-tolerant predicate contract.")]
        public bool IsPredecessorOf(SiloAddress other) => IsSameLogicalSilo(other) && Generation > 0 && Generation < other.Generation;

        /// <inheritdoc/>
        public int CompareTo(SiloAddress? other)
        {
            if (other is null) return 1;
            // 首先比较 Generation，这是一种开销低、速度快且避免分配的比较方式，
            // 同时具有语义意义：较旧的 Silo（Generation 较小）会在比较排序中排在前面。
            // 只有在 Generation 相等时，才继续比较端口和 IPAddress（比较成本更高）。
            // 或者，也可以比较 ConsistentHashCode 或 UniformHashCode。
            int comp = Generation.CompareTo(other.Generation);
            if (comp != 0) return comp;

            comp = Endpoint.Port.CompareTo(other.Endpoint.Port);
            if (comp != 0) return comp;

            return IPAddressComparer.Instance.Compare(Endpoint.Address, other.Endpoint.Address);
        }
    }

    /// <summary>
    /// 提供 <see cref="SiloAddress"/> 实例与其 JSON 表示形式之间的转换功能。
    /// </summary>
    public sealed class SiloAddressConverter : JsonConverter<SiloAddress>
    {
        /// <inheritdoc />
        public override SiloAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => SiloAddress.FromParsableString(reader.GetString() ?? throw new JsonException($"Could not deserialize {nameof(SiloAddress)}."));

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 为 <see langword="null"/>。</exception>
        public override void Write(Utf8JsonWriter writer, SiloAddress value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteStringValue(value.ToUtf8String());
        }

        /// <inheritdoc />
        public override SiloAddress ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => SiloAddress.FromParsableString(reader.GetString()!);

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 为 <see langword="null"/>。</exception>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] SiloAddress value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WritePropertyName(value.ToUtf8String());
        }
    }
}
