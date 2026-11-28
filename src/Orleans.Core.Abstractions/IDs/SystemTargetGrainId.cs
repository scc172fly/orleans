using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 标识系统目标。
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
        /// Grain ID。
        /// </param>
        private SystemTargetGrainId(GrainId grainId) => _grainId = grainId;

        /// <summary>
        /// 获取底层标识。
        /// </summary>
        public GrainId GrainId => _grainId;

        /// <summary>
        /// 创建新的 <see cref="SystemTargetGrainId"/> 实例。
        /// </summary>
        /// <param name="kind">
        /// Grain 类型。
        /// </param>
        /// <param name="address">
        /// 系统目标所在的服务器。
        /// </param>
        /// <returns>
        /// 一个 <see cref="SystemTargetGrainId"/>。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public static SystemTargetGrainId Create(GrainType kind, SiloAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return new SystemTargetGrainId(new GrainId(kind, new IdSpan(address.ToUtf8String())));
        }

        /// <summary>
        /// 创建新的 <see cref="SystemTargetGrainId"/> 实例。
        /// </summary>
        /// <param name="kind">
        /// Grain 类型。
        /// </param>
        /// <param name="address">
        /// 系统目标所在的服务器。
        /// </param>
        /// <param name="extraIdentifier">
        /// 可选的键扩展。
        /// </param>
        /// <returns>
        /// 一个 <see cref="SystemTargetGrainId"/>。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
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
        /// 如果提供的实例表示系统目标，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="id">
        /// Grain ID。
        /// </param>
        /// <returns>
        /// 如果此值是系统目标 Grain ID，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </returns>
        public static bool IsSystemTargetGrainId(in GrainId id) => id.Type.AsSpan().StartsWith(GrainTypePrefix.SystemTargetPrefixBytes.Span);

        /// <summary>
        /// 将提供的 <see cref="GrainId"/> 转换为 <see cref="SystemTargetGrainId"/>。返回值指示操作是否成功。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <param name="systemTargetId">
        /// 得到的系统目标 ID。
        /// </param>
        /// <returns>
        /// 如果此值是系统目标 Grain ID，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
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
        /// 返回以提供的地址为目标的新 <see cref="SystemTargetGrainId"/>。
        /// </summary>
        /// <param name="siloAddress">
        /// Silo 地址。
        /// </param>
        /// <returns>
        /// 以提供的地址为目标的新 <see cref="SystemTargetGrainId"/>。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="siloAddress"/> 为 <see langword="null"/>。</exception>
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
        /// 获取系统目标的 <see cref="SiloAddress"/>。
        /// </summary>
        /// <returns>
        /// 此系统目标 ID 对应的 Silo 地址。
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
        /// 为 Grain 服务创建 <see cref="GrainId"/>。
        /// </summary>
        /// <param name="typeCode">
        /// 类型码。
        /// </param>
        /// <param name="grainSystemId">
        /// 系统 ID；若要省略系统 ID 后缀，则使用 <see langword="null"/>。
        /// </param>
        /// <param name="address">
        /// Silo 地址。
        /// </param>
        /// <returns>Grain 服务实例的 Grain ID。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public static GrainId CreateGrainServiceGrainId(int typeCode, string grainSystemId, SiloAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            return CreateGrainServiceGrainId(CreateGrainServiceGrainType(typeCode, grainSystemId), address);
        }

        /// <summary>
        /// 为 Grain 服务创建 <see cref="GrainId"/>。
        /// </summary>
        /// <param name="typeCode">
        /// 类型码。
        /// </param>
        /// <param name="grainSystemId">
        /// 系统 ID。
        /// </param>
        /// <returns>Grain 服务实例的 Grain ID。</returns>
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
        /// 为 Grain 服务创建 <see cref="GrainId"/>。
        /// </summary>
        /// <param name="grainType">
        /// Grain 类型。
        /// </param>
        /// <param name="address">
        /// Silo 地址。
        /// </param>
        /// <returns>Grain 服务实例的 Grain ID。</returns>
        internal static GrainId CreateGrainServiceGrainId(GrainType grainType, SiloAddress address)
            => new GrainId(grainType, new IdSpan(address.ToUtf8String()));

        /// <summary>
        /// 使用提供的名称创建系统目标 <see cref="GrainType"/>。
        /// </summary>
        /// <param name="name">
        /// 系统目标 Grain 类型的名称。
        /// </param>
        /// <returns>
        /// Grain 类型。
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
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(SystemTargetGrainId left, SystemTargetGrainId right) => left.Equals(right);

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(SystemTargetGrainId left, SystemTargetGrainId right) => !(left == right);

        /// <summary>
        /// 比较提供的操作数，左操作数小于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数小于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator <(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) < 0;

        /// <summary>
        /// 比较提供的操作数，左操作数小于或等于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数小于或等于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator <=(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// 比较提供的操作数，左操作数大于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数大于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator >(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) > 0;

        /// <summary>
        /// 比较提供的操作数，左操作数大于或等于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数大于或等于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator >=(SystemTargetGrainId left, SystemTargetGrainId right) => left.CompareTo(right) >= 0;
    }
}
