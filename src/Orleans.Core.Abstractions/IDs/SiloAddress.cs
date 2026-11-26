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
    /// 数据分类,包含 silo 地址的细节。
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
        /// 得到终点。
        /// </summary>
        [Id(0)]
        public IPEndPoint Endpoint { get; }

        /// <summary>
        /// 获得一代。
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

        /// <summary>获取显示空 <see cref="SiloAddress"/>的特殊常数值。</summary>
        public static SiloAddress Zero { get; } = New(new IPAddress(0), 0, 0);

        /// <summary>
        /// 创建新的 SiloAddresses 工厂,该厂有指定的IP端点地址和 silo 生产号。
        /// </summary>
        /// <param name="ep"> silo的IP端点地址。</param>
        /// <param name="gen">相传数 silo.</param>
        /// <returns>SiloAddress 对象初始化,以指定地址和 silo 生成。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ep"/> 是 <see langword="null"/>.</exception>
        public static SiloAddress New(IPEndPoint ep, int gen)
        {
            ArgumentNullException.ThrowIfNull(ep);
            return siloAddressInterningCache.FindOrCreate((ep.Address, ep.Port, gen),
                // 将终点正常化
                (k, ep) => k.Address.IsIPv4MappedToIPv6 ? New(k.Address.MapToIPv4(), k.Port, k.Generation) : new(ep, k.Generation), ep);
        }

        /// <summary>
        /// 创建新的 SiloAddresses 工厂,该厂有指定的IP端点地址和 silo 生产号。
        /// </summary>
        /// <param name="address">IP 地址 silo.</param>
        /// <param name="port">端口号</param>
        /// <param name="generation">相传数 silo.</param>
        /// <returns>SiloAddress 对象初始化,以指定地址和 silo 生成。</returns>
        public static SiloAddress New(IPAddress address, int port, int generation)
        {
            return siloAddressInterningCache.FindOrCreate((address, port, generation),
                // 将终点正常化
                k => k.Address.IsIPv4MappedToIPv6 ? New(k.Address.MapToIPv4(), k.Port, k.Generation) : new(new(k.Address, k.Port), k.Generation));
        }

        /// <summary>
        /// 初始化 <see cref="SiloAddress"/> 类的新实例。
        /// </summary>
        /// <param name="endpoint">终点。</param>
        /// <param name="generation">一代人</param>
        private SiloAddress(IPEndPoint endpoint, int generation)
        {
            Endpoint = endpoint;
            Generation = generation;
        }

        /// <summary>
        /// 获得一个值, 显示此实例是否代表客户端( 相对于服务器) 。
        /// </summary>
        public bool IsClient { get { return Generation < 0; } }

        /// <summary> 分配新 silo 数。 </summary>
        /// <returns>一个新的 silo 数。</returns>
        public static int AllocateNewGeneration()
        {
            long elapsed = (DateTime.UtcNow.Ticks - epoch) / TimeSpan.TicksPerSecond;

            // 对于在1秒内重新启动 silos 的测试,我们需要确保生成数量不断增长
            // 因为重开 silo 的代数必须是唯一的 。
            lock (LastGenerationLock)
            {
                LastGeneration = elapsed = Math.Max(elapsed, LastGeneration + 1);
            }

            return unchecked((int)elapsed); // 未校验以缩短下32 以上的任何位数
        }

        internal static int GetGenerationRetirementFloor(DateTime utcNow, TimeSpan retirementAge)
        {
            var elapsedTicks = utcNow.Ticks - epoch;
            if (retirementAge.Ticks >= elapsedTicks)
            {
                return 0;
            }

            // 整组,独家楼层包括
            // 几代人比隔秒断开的距离大得多
            return (int)Math.Min(
                (elapsedTicks - retirementAge.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond,
                int.MaxValue);
        }

        /// <summary>
        /// 将此 SiloAddress 以标准字符串形式返回,适合以后使用 <c>FromParsableString</c> 方法。
        /// </summary>
        /// <returns>SiloAddress 以标准字符串格式。</returns>
        public string ToParsableString()
        {
            // 这必须是 FromParsableString的“反向”,在部署中,必须对所有 silos 都相同。
            // 基本上,除非 SiloAddress 的数据内容发生变化,否则这绝不应改变
            if (utf8 != null) return Encoding.UTF8.GetString(utf8);
            return $"{new SpanFormattableIPAddress(Endpoint.Address)}:{Endpoint.Port}@{Generation}";
        }

        /// <summary>
        /// a 返回 UTF8- 编码表示此实例为字节数组。
        /// </summary>
        /// <returns>A UTF8- 编码表示此实例为字节数组。</returns>
        internal byte[] ToUtf8String()
        {
            if (utf8 is null)
            {
                Span<char> chars = stackalloc char[45];
                var addr = Endpoint.Address.TryFormat(chars, out var len) ? chars[..len] : Endpoint.Address.ToString().AsSpan();
                var size = Encoding.UTF8.GetByteCount(addr);

                // 为地址+ ' ' :'+端口+ '@'+一代分配足够空间
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
        /// 以标准格式解析字符串创建新的 SiloAddress 对象,该格式从 <c>ToParsableString</c> 方法返回。
        /// </summary>
        /// <param name="addr">字符串中含有要解析的 SiloAddress 信息。</param>
        /// <returns>新建输入数据创建的 SiloAddress 对象。</returns>
        public static SiloAddress FromParsableString(string addr)
            => Parse(addr, null);

        /// <summary>
        /// 专业a <see cref="SiloAddress"/> 从标准表格中返回的字符串 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="value">字符串中含有要解析的 <see cref="SiloAddress"/> 信息。</param>
        /// <returns>A <see cref="SiloAddress"/> 从输入数据中解析 。</returns>
        public static SiloAddress Parse(string value)
            => Parse(value, null);

        /// <summary>
        /// 专业a <see cref="SiloAddress"/> 从标准表格中返回的字符串 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="value">字符串中含有要解析的 <see cref="SiloAddress"/> 信息。</param>
        /// <param name="provider">对象,该对象提供特定文化的格式信息。此参数被忽略。</param>
        /// <returns>A <see cref="SiloAddress"/> 从输入数据中解析 。</returns>
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
        /// 尝试解析 a <see cref="SiloAddress"/> 从标准表格中返回的字符串 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="value">字符串中含有要解析的 <see cref="SiloAddress"/> 信息。</param>
        /// <param name="result">撕裂的 <see cref="SiloAddress"/>,或 <see langword="null"/> 如果 剖析失败 。</param>
        /// 如果解析成功,则<returns><see langword="true"/> ;否则, <see langword="false"/>。</returns>
        public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out SiloAddress? result)
            => TryParse(value, null, out result);

        /// <summary>
        /// 尝试解析 a <see cref="SiloAddress"/> 从标准表格中返回的字符串 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="value">字符串中含有要解析的 <see cref="SiloAddress"/> 信息。</param>
        /// <param name="provider">对象,该对象提供特定文化的格式信息。此参数被忽略。</param>
        /// <param name="result">撕裂的 <see cref="SiloAddress"/>,或 <see langword="null"/> 如果 剖析失败 。</param>
        /// 如果解析成功,则<returns><see langword="true"/> ;否则, <see langword="false"/>。</returns>
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
            // 这必须是 ToParsableString的“反向”,在部署中,必须对所有 silos 都相同。
            // 基本上,除非 SiloAddress 的数据内容发生变化,否则这绝不应改变

            // 首先是 IPEndpoint; 然后是“ @” ; 然后是一代
            var atSign = addr.LastIndexOf(SEPARATOR);
            if (atSign < 0)
            {
                result = null;
                return false;
            }

            // IPendpoint 是主机, 然后是 ':', 然后是港口
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
        /// 以标准格式解析字符串创建新的 SiloAddress 对象,该格式从 <c>ToParsableString</c> 方法返回。
        /// </summary>
        /// <param name="addr">字符串中含有要解析的 SiloAddress 信息。</param>
        /// <returns>新建输入数据创建的 SiloAddress 对象。</returns>
        public static SiloAddress FromUtf8String(ReadOnlySpan<byte> addr)
            => Parse(addr, null);

        /// <summary>
        /// 专业a <see cref="SiloAddress"/> 从标准表格中UTF-8文本返回 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="utf8Text">UTF-8 文本,其中含有 <see cref="SiloAddress"/> 信息待解。</param>
        /// <returns>A <see cref="SiloAddress"/> 从输入数据中解析 。</returns>
        public static SiloAddress Parse(ReadOnlySpan<byte> utf8Text)
            => Parse(utf8Text, null);

        /// <summary>
        /// 专业a <see cref="SiloAddress"/> 从标准表格中UTF-8文本返回 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="utf8Text">UTF-8 文本,其中含有 <see cref="SiloAddress"/> 信息待解。</param>
        /// <param name="provider">对象,该对象提供特定文化的格式信息。此参数被忽略。</param>
        /// <returns>A <see cref="SiloAddress"/> 从输入数据中解析 。</returns>
        public static SiloAddress Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider = null)
        {
            if (!TryParse(utf8Text, provider, out var result))
            {
                ThrowInvalidUtf8SiloAddress(utf8Text);
            }

            return result;
        }

        /// <summary>
        /// 尝试解析 a <see cref="SiloAddress"/> 从标准表格中UTF-8文本返回 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="utf8Text">UTF-8 文本,其中含有 <see cref="SiloAddress"/> 信息待解。</param>
        /// <param name="result">撕裂的 <see cref="SiloAddress"/>,或 <see langword="null"/> 如果 剖析失败 。</param>
        /// 如果解析成功,则<returns><see langword="true"/> ;否则, <see langword="false"/>。</returns>
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out SiloAddress? result)
            => TryParse(utf8Text, null, out result);

        /// <summary>
        /// 尝试解析 a <see cref="SiloAddress"/> 从标准表格中UTF-8文本返回 <see cref="ToParsableString"/>.
        /// </summary>
        /// <param name="utf8Text">UTF-8 文本,其中含有 <see cref="SiloAddress"/> 信息待解。</param>
        /// <param name="provider">对象,该对象提供特定文化的格式信息。此参数被忽略。</param>
        /// <param name="result">撕裂的 <see cref="SiloAddress"/>,或 <see langword="null"/> 如果 剖析失败 。</param>
        /// 如果解析成功,则<returns><see langword="true"/> ;否则, <see langword="false"/>。</returns>
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [NotNullWhen(true)] out SiloAddress? result)
        {
            // 这必须是 ToParsableString的“反向”,在部署中,必须对所有 silos 都相同。
            // 基本上,除非 SiloAddress 的数据内容发生变化,否则这绝不应改变

            // 首先是 IPEndpoint; 然后是“ @” ; 然后是一代
            var atSign = utf8Text.LastIndexOf((byte)SEPARATOR);
            if (atSign < 0)
            {
                result = null;
                return false;
            }

            // IPendpoint 是主机, 然后是 ':', 然后是港口
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
        /// 返回此 SiloAddress的长字符串表示。
        /// </summary>
        /// <remarks>
        /// 注:此字符串值与 <see cref="FromParsableString"/> 方法不相可比 -- -- 为此目的使用 <see cref="ToParsableString"/> 方法。
        /// </remarks>
        /// <returns>此 SiloAddress的字符串表示。</returns>
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
        /// 返回此 SiloAddress的长字符串表示,包括它一贯的散列值。
        /// </summary>
        /// <remarks>
        /// 注:这一字符串值与 <c>FromParsableString</c> 方法 -- -- 为此目的使用 <c>ToParsableString</c> 方法不相可比。
        /// </remarks>
        /// <returns>此 SiloAddress的字符串表示。</returns>
        public string ToStringWithHashCode() => $"{this:H}";

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as SiloAddress);

        /// <inheritdoc />
        public override int GetHashCode() => Endpoint.GetHashCode() ^ Generation;

        /// <summary>确定两个 silo 地址是否有相同的终点和生成。</summary>
        /// <param name="left">第一个地址。</param>
        /// <param name="right">第二个地址。</param>
        /// <returns><see langword="true"/> ,如果两个地址均为无效或代表同一 silo;否则, <see langword="false"/>。</returns>
        public static bool operator ==(SiloAddress? left, SiloAddress? right)
            => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

        /// <summary>确定两个 silo 终端或生成地址不同。</summary>
        /// <param name="left">第一个地址。</param>
        /// <param name="right">第二个地址。</param>
        /// <returns><see langword="true"/> ,如果地址不同;否则, <see langword="false"/>。</returns>
        public static bool operator !=(SiloAddress? left, SiloAddress? right) => !(left == right);

        /// <summary>返回此 silo 地址的一致散列值。</summary>
        /// <returns>这个 silo 地址的一致散列值。</returns>
        public int GetConsistentHashCode() => hashCodeSet ? hashCode : CalculateConsistentHashCode();

        /// <summary>返回此 silo 地址的一致散列值。</summary>
        /// <returns>这个 silo 地址的一致散列值。</returns>
        internal int GetConsistentHashCode(int seed)
        {
            var tmp = (0, 0L, 0L, 0L); // 使用固定大小缓冲器避免堆叠式平流
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
            var tmp = (0L, 0L, 0L); // 使用固定大小缓冲器避免堆叠式平流
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
        /// 返回一个不可更改的统一的散列代码变量集合。
        /// </summary>
        /// <param name="numHashes">要返回的大麻代码数 。</param>
        /// <returns>收集了这方面的统一散列码变异物,这些变体是不可改变的。</returns>
        /// <remarks>
        /// 调查结果被隐藏起来,并分享其支持存储,通过要求同样数量的大麻的电话进行。
        /// </remarks>
        public ImmutableArray<uint> GetUniformHashCodes(int numHashes)
        {
            var cache = uniformHashCache;
            if (!cache.IsDefault && cache.Length == numHashes) return cache;
            return uniformHashCache = GetUniformHashCodesImpl(numHashes);
        }

        private ImmutableArray<uint> GetUniformHashCodesImpl(int numHashes)
        {
            Span<byte> bytes = stackalloc byte[16 + sizeof(int) + sizeof(int) + sizeof(int)]; // ip + 端口+ 发电+ extraBit

            // 端点 IP 地址
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
            // 港口端端口
            BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], Endpoint.Port);
            offset += sizeof(int);
            // 代 代
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
        /// 两个 silo 地址相同时,或者如果一代或一代是零,则匹配。
        /// </summary>
        /// <param name="other"> 另一 SiloAddress 来比较这个和。 </param>
        /// <returns>返回 <c>true</c> 如果两者 SiloAddresses 认为匹配 -- -- 如果相等,或一代人或另一代是0。 </returns>
        internal bool Matches([NotNullWhen(true)] SiloAddress? other)
        {
            return other is not null && Endpoint.Address.Equals(other.Endpoint.Address) && Endpoint.Port == other.Endpoint.Port &&
                (Generation == other.Generation || Generation == 0 || other.Generation == 0);
        }

        /// <inheritdoc/>
        public bool Equals([NotNullWhen(true)] SiloAddress? other)
            => other is not null && Generation == other.Generation && Endpoint.Address.Equals(other.Endpoint.Address) && Endpoint.Port == other.Endpoint.Port;

        /// <summary>
        /// 如果所提供的值代表与该值相同的逻辑服务器,则返回 <see langword="true"/> ,否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="other">
        /// 另一例。
        /// </param>
        /// <returns>
        /// <see langword="true"/> 如果所提供的数值代表与该值相同的逻辑服务器,否则 <see langword="false"/>。
        /// </returns>
        internal bool IsSameLogicalSilo([NotNullWhen(true)] SiloAddress? other)
            => other is not null && Endpoint.Address.Equals(other.Endpoint.Address) && Endpoint.Port == other.Endpoint.Port;

        /// <summary>
        /// 返回 <see langword="true"/> 如果所提供的值代表与该值相同的逻辑服务器,且是此服务器的继承者,否则 <see langword="false"/>.
        /// </summary>
        /// <param name="other">
        /// 另一种情况,或 <see langword="null"/> 返回 <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> ,如果所提供的数值代表与该值相同的逻辑服务器,并且是该服务器的后继者,否则 <see langword="false"/>。
        /// </returns>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "IsSameLogicalSilo returns false for null, short-circuiting the dereference and preserving the null-tolerant predicate contract.")]
        public bool IsSuccessorOf(SiloAddress other) => IsSameLogicalSilo(other) && other.Generation > 0 && Generation > other.Generation;

        /// <summary>
        /// 如果所提供的值代表与此值相同的逻辑服务器, 且是该服务器的前身, 则返回 <see langword="true"/> , 否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="other">
        /// 另一种情况,或 <see langword="null"/> 返回 <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> ,如果所提供的数值代表与该值相同的逻辑服务器并且是此服务器的前身,否则 <see langword="false"/>。
        /// </returns>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "IsSameLogicalSilo returns false for null, short-circuiting the dereference and preserving the null-tolerant predicate contract.")]
        public bool IsPredecessorOf(SiloAddress other) => IsSameLogicalSilo(other) && Generation > 0 && Generation < other.Generation;

        /// <inheritdoc/>
        public int CompareTo(SiloAddress? other)
        {
            if (other is null) return 1;
            // 比较一代第一。 它提供了一种廉价和快速的比较方法,避免分配 
            // 并且具有音义意义----老 silos (与小一代人)将首先出现在比较顺序中。
            // 只有一代人相同, 才能比较端口和IP地址( 比较范围更大) 。
            // 或者,我们可以比较 ConsistentHashCode 或 UniformHashCode。
            int comp = Generation.CompareTo(other.Generation);
            if (comp != 0) return comp;

            comp = Endpoint.Port.CompareTo(other.Endpoint.Port);
            if (comp != 0) return comp;

            return IPAddressComparer.Instance.Compare(Endpoint.Address, other.Endpoint.Address);
        }
    }

    /// <summary>
    /// 换算功能 <see cref="SiloAddress"/> 与从这些天起 JSON 代表。
    /// </summary>
    public sealed class SiloAddressConverter : JsonConverter<SiloAddress>
    {
        /// <inheritdoc />
        public override SiloAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => SiloAddress.FromParsableString(reader.GetString() ?? throw new JsonException($"Could not deserialize {nameof(SiloAddress)}."));

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 是 <see langword="null"/>。</exception>
        public override void Write(Utf8JsonWriter writer, SiloAddress value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteStringValue(value.ToUtf8String());
        }

        /// <inheritdoc />
        public override SiloAddress ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => SiloAddress.FromParsableString(reader.GetString()!);

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> 或 <paramref name="value"/> 是 <see langword="null"/>。</exception>
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] SiloAddress value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            writer.WritePropertyName(value.ToUtf8String());
        }
    }
}
