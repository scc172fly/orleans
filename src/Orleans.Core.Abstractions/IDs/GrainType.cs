using System;
using System.Runtime.Serialization;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示 grain的类型。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct GrainType : IEquatable<GrainType>, IComparable<GrainType>, ISerializable, ISpanFormattable
    {
        [Id(0)]
        private readonly IdSpan _value;

        /// <summary>
        /// 初始化 <see cref="GrainType"/> 结构的新实例。 
        /// </summary>
        /// <param name="id">
        /// 身份证
        /// </param>
        public GrainType(IdSpan id) => _value = id;

        /// <summary>
        /// 初始化 <see cref="GrainType"/> 结构的新实例。 
        /// </summary>
        /// <param name="value">
        /// 原始 ID 值 。
        /// </param>
        public GrainType(byte[] value) => _value = new IdSpan(value);

        /// <summary>
        /// 初始化 <see cref="GrainType"/> 结构的新实例。 
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        private GrainType(SerializationInfo info, StreamingContext context)
        {
            _value = IdSpan.UnsafeCreate((byte[]?)info.GetValue("v", typeof(byte[])), info.GetInt32("h"));
        }

        /// <summary>
        /// 获得基本价值。
        /// </summary>
        public IdSpan Value => _value;

        /// <summary>
        /// 返回此实例的跨度代表。
        /// </summary>
        /// <returns>
        /// 值的 <see cref="ReadOnlySpan{Byte}"/> 表示值。
        /// </returns>
        public ReadOnlySpan<byte> AsSpan() => _value.AsSpan();

        /// <summary>
        /// 创建新的 <see cref="GrainType"/> 实例。
        /// </summary>
        /// <param name="value">
        /// 价值。
        /// </param>
        /// <returns>
        /// 新创建的 <see cref="GrainType"/> 实例 。
        /// </returns>
        public static GrainType Create(string value)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(value);
            return new GrainType(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// 将 <see cref="GrainType"/> 转换为 <see cref="IdSpan"/>。
        /// </summary>
        /// <param name="kind">下 grain 要转换的类型。</param>
        /// <returns>对应对应 <see cref="IdSpan"/>.</returns>
        public static explicit operator IdSpan(GrainType kind) => kind._value;

        /// <summary>
        /// 将 <see cref="IdSpan"/> 转换为 <see cref="GrainType"/>。
        /// </summary>
        /// <param name="id">要转换的id 区域。</param>
        /// <returns>对应对应 <see cref="GrainType"/>.</returns>
        public static explicit operator GrainType(IdSpan id) => new GrainType(id);

        /// <summary>
        /// 获得一个值,以显示此实例是否为默认值。
        /// </summary>
        public bool IsDefault => _value.IsDefault;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is GrainType kind && Equals(kind);

        /// <inheritdoc/>
        public bool Equals(GrainType obj) => _value.Equals(obj._value);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>
        /// 为该类型 grain 生成一个统一的、稳定的散列代码。 
        /// </summary>
        /// <returns>
        /// 这件案子的制服和稳定
        /// </returns>
        public uint GetUniformHashCode() => _value.GetUniformHashCode();

        /// <summary>
        /// 返回 grain 类型实例下的数组。
        /// </summary>
        /// <param name="id"> grain 类型。</param>
        /// <returns> grain 类型实例的矩阵。</returns>
        /// <remarks>
        /// 返回的数组不得修改 。
        /// </remarks>
        public static byte[]? UnsafeGetArray(GrainType id) => IdSpan.UnsafeGetArray(id._value);

        /// <inheritdoc/>
        public int CompareTo(GrainType other) => _value.CompareTo(other._value);

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("v", IdSpan.UnsafeGetArray(_value));
            info.AddValue("h", _value.GetHashCode());
        }

        /// <summary>
        /// 返回此实例的字符串表示,将值解码为 UTF8。
        /// </summary>
        /// <returns>
        /// 此处的 <see cref="string"/> 表示。
        /// </returns>
        public override string ToString() => _value.ToString();

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => _value.TryFormat(destination, out charsWritten);

        /// <summary>
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(GrainType left, GrainType right) => left.Equals(right);

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(GrainType left, GrainType right) => !(left == right);
    }
}
