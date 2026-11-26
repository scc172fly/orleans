using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 确定系统目标。
    /// </summary>
    [Immutable]
    public readonly struct SystemTargetGrainId : IEquatable<SystemTargetGrainId>, IComparable<SystemTargetGrainId>, ISpanFormattable
    {
        private const char SegmentSeparator = '+';
        private readonly GrainId _grainId;

        /// <summary>
        /// 初始化 <see cref="SystemTargetGrainId"/> 结构的新实例。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        private SystemTargetGrainId(GrainId grainId) => _grainId = grainId;

        /// <summary>
        /// 获得基本身份。
        /// </summary>
        public GrainId GrainId => _grainId;

        /// <summary>
        /// 创建新的 <see cref="SystemTargetGrainId"/> 实例。
        /// </summary>
        /// <param name="kind">
        /// 类型 grain 。
        /// </param>
        /// <param name="address">
        /// 系统目标所在的服务器 。
        /// </param>
        /// <returns>
        /// A <see cref="SystemTargetGrainId"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 是 <see langword="null"/>.</exception>
        public static SystemTargetGrainId Create(GrainType kind, SiloAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return new SystemTargetGrainId(new GrainId(kind, new IdSpan(address.ToUtf8String())));
        }

        /// <summary>
        /// 创建新的 <see cref="SystemTargetGrainId"/> 实例。
        /// </summary>
        /// <param name="kind">
        /// 类型 grain 。
        /// </param>
        /// <param name="address">
        /// 系统目标所在的服务器 。
        /// </param>
        /// <param name="extraIdentifier">
        /// 可选按键扩展 。
        /// </param>
        /// <returns>
        /// A <see cref="SystemTargetGrainId"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 是 <see langword="null"/>.</exception>
        public static SystemTargetGrainId Create(GrainType kind, SiloAddress address, string? extraIdentifier)
        {
            ArgumentNullException.ThrowIfNull(address);
            var addr = address.ToUtf8String();
            if (extraIdentifier is not null)
            {
                var extraLen = Encoding.UTF8.GetByteCount(extraIdentifier);
                var buf = new byte[addr.Length + 1 + extraLen];
                addr.CopyTo(buf.AsSpan());
                buf[addr.Length] = (byte)SegmentSeparator;
                Encoding.UTF8.GetBytes(extraIdentifier, 0, extraIdentifier.Length, buf, addr.Length + 1);
                addr = buf;
            }

            return new SystemTargetGrainId(new GrainId(kind, new IdSpan(addr)));
        }

        /// <summary>
        /// 返回 <see langword="true"/> 如果所提供的实例代表系统目标, <see langword="false"/> 否则。
        /// </summary>
        /// <param name="id">
        /// 下 grain id. 数据。
        /// </param>
        /// <returns>
        /// <see langword="true"/> 如果值是系统目标,则值 grain id, <see langword="false"/> 否则。
        /// </returns>
        public static bool IsSystemTargetGrainId(in GrainId id) => id.Type.AsSpan().StartsWith(GrainTypePrefix.SystemTargetPrefixBytes.Span);

        /// <summary>
        /// 将提供的 <see cref="GrainId"/> 转换为 <see cref="SystemTargetGrainId"/>。返回值表示操作是否成功。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <param name="systemTargetId">
        /// 由此产生的系统目标识别码。
        /// </param>
        /// <returns>
        /// <see langword="true"/> 如果值是系统目标,则值 grain id, <see langword="false"/> 否则。
        /// </returns>
        public static bool TryParse(GrainId grainId, out SystemTargetGrainId systemTargetId)
        {
            if (!IsSystemTargetGrainId(grainId))
            {
                systemTargetId = default;
                return false;
            }

            systemTargetId = new SystemTargetGrainId(grainId);
            return true;
        }

        /// <summary>
        /// 返回针对所提供的地址的新 <see cref="SystemTargetGrainId"/> 。
        /// </summary>
        /// <param name="siloAddress">
        /// silo 地址。
        /// </param>
        /// <returns>
        /// 针对所提供的地址的一个新的 <see cref="SystemTargetGrainId"/> 。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="siloAddress"/> 是 <see langword="null"/>.</exception>
        public SystemTargetGrainId WithSiloAddress(SiloAddress siloAddress)
        {
            ArgumentNullException.ThrowIfNull(siloAddress);
            var addr = siloAddress.ToUtf8String();
            var key = _grainId.Key.AsSpan();
            if (key.IndexOf((byte)SegmentSeparator) is int index && index >= 0)
            {
                var extraIdentifier = key[(index + 1)..];

                var buf = new byte[addr.Length + 1 + extraIdentifier.Length];
                addr.CopyTo(buf.AsSpan());
                buf[addr.Length] = (byte)SegmentSeparator;
                extraIdentifier.CopyTo(buf.AsSpan(addr.Length + 1));
                addr = buf;
            }

            return new SystemTargetGrainId(new GrainId(_grainId.Type, new IdSpan(addr)));
        }

        /// <summary>
        /// 获得系统目标的 <see cref="SiloAddress"/> 。
        /// </summary>
        /// <returns>
        /// silo 地址与这个系统的目标代号相对应。
        /// </returns>
        public SiloAddress GetSiloAddress()
        {
            var key = _grainId.Key.AsSpan();
            if (key.IndexOf((byte)SegmentSeparator) is int index && index >= 0)
            {
                key = key[..index];
            }

            return SiloAddress.FromUtf8String(key);
        }

        /// <summary>
        /// 创建 <see cref="GrainId"/> a 的a grain 服务。
        /// </summary>
        /// <param name="typeCode">
        /// 类型代码。
        /// </param>
        /// <param name="grainSystemId">
        /// 系统 ID , 或 <see langword="null"/> 省略系统 id 后缀 。
        /// </param>
        /// <param name="address">
        /// silo 地址。
        /// </param>
        /// <returns>A grain 服务实例的 grain id。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 是 <see langword="null"/>.</exception>
        public static GrainId CreateGrainServiceGrainId(int typeCode, string grainSystemId, SiloAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return CreateGrainServiceGrainId(CreateGrainServiceGrainType(typeCode, grainSystemId), address);
        }

        /// <summary>
        /// 创建 <see cref="GrainId"/> a 的a grain 服务。
        /// </summary>
        /// <param name="typeCode">
        /// 类型代码。
        /// </param>
        /// <param name="grainSystemId">
        /// 系统识别码
        /// </param>
        /// <returns>A grain 服务实例的 grain id。</returns>
        internal static GrainType CreateGrainServiceGrainType(int typeCode, string? grainSystemId)
        {
            var extraLen = grainSystemId is null ? 0 : Encoding.UTF8.GetByteCount(grainSystemId);
            var buf = new byte[GrainTypePrefix.GrainServicePrefix.Length + 8 + extraLen];
            GrainTypePrefix.GrainServicePrefixBytes.Span.CopyTo(buf);
            Utf8Formatter.TryFormat(typeCode, buf.AsSpan(GrainTypePrefix.GrainServicePrefix.Length), out var len, new StandardFormat('X', 8));
            Debug.Assert(len == 8);
            if (grainSystemId != null) Encoding.UTF8.GetBytes(grainSystemId, 0, grainSystemId.Length, buf, buf.Length - extraLen);
            return new GrainType(buf);
        }

        /// <summary>
        /// 创建 <see cref="GrainId"/> a 的a grain 服务。
        /// </summary>
        /// <param name="grainType">
        /// 类型 grain 。
        /// </param>
        /// <param name="address">
        /// silo 地址。
        /// </param>
        /// <returns>A grain 服务实例的 grain id。</returns>
        internal static GrainId CreateGrainServiceGrainId(GrainType grainType, SiloAddress address)
            => new GrainId(grainType, new IdSpan(address.ToUtf8String()));

        /// <summary>
        /// 创建带有提供名称的系统目标 <see cref="GrainType"/> 。
        /// </summary>
        /// <param name="name">
        /// 系统目标 grain 类型名称。
        /// </param>
        /// <returns>
        /// 类型 grain 。
        /// </returns>
        public static GrainType CreateGrainType(string name) => GrainType.Create($"{GrainTypePrefix.SystemTargetPrefix}{name}");

        /// <inheritdoc/>
        public bool Equals(SystemTargetGrainId other) => _grainId.Equals(other._grainId);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is SystemTargetGrainId observer && this.Equals(observer);

        /// <inheritdoc/>
        public override int GetHashCode() => _grainId.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => _grainId.ToString();

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => ((ISpanFormattable)_grainId).TryFormat(destination, out charsWritten, format, provider);

        /// <inheritdoc/>
        public int CompareTo(SystemTargetGrainId other) => _grainId.CompareTo(other._grainId);

        /// <summary>
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(SystemTargetGrainId left, SystemTargetGrainId right) => left.Equals(right);

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(SystemTargetGrainId left, SystemTargetGrainId right) => !(left == right);

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 如果左边的手术功率低于右边的手术功率,那末, <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左边的手术功率低于右边的手术功率,那末, <see langword="false"/>.</returns>
        public static bool operator <(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) < 0;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> ,如果左操作小于或等于右操作,否则 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形操作小于或等于右形操作,否则 <see langword="false"/>。</returns>
        public static bool operator <=(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 倘若左边的手术胜过右边的手术,否则 <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形大于右形,否则 <see langword="false"/>。</returns>
        public static bool operator >(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) > 0;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 倘若左边的手术功过右的或等量,否则 <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形操作大于或等于右形操作,否则 <see langword="false"/>。</returns>
        public static bool operator >=(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) >= 0;
    }
}
