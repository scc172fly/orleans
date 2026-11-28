using System;
using System.Runtime.Serialization;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示 Grain 的类型。
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
        /// 标识符。
        /// </param>
        public GrainType(IdSpan id) => _value = id;

        /// <summary>
        /// 初始化 <see cref="GrainType"/> 结构的新实例。
        /// </summary>
        /// <param name="value">
        /// 原始标识符值。
        /// </param>
        public GrainType(byte[] value) => _value = new IdSpan(value);

        /// <summary>
        /// 初始化 <see cref="GrainType"/> 结构的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        private GrainType(SerializationInfo info, StreamingContext context)
        {
            _value = IdSpan.UnsafeCreate((byte[]?)info.GetValue("v", typeof(byte[])), info.GetInt32("h"));
        }

        /// <summary>
        /// 获取底层值。
        /// </summary>
        public IdSpan Value => _value;

        /// <summary>
        /// 返回此实例的 Span 表示。
        /// </summary>
        /// <returns>
        /// 值的 <see cref="ReadOnlySpan{Byte}"/> 表示。
        /// </returns>
        public ReadOnlySpan<byte> AsSpan() => _value.AsSpan();

        /// <summary>
        /// 创建新的 <see cref="GrainType"/> 实例。
        /// </summary>
        /// <param name="value">
        /// 值。
        /// </param>
        /// <returns>
        /// 新创建的 <see cref="GrainType"/> 实例。
        /// </returns>
        public static GrainType Create(string value)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(value);
            return new GrainType(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// 将 <see cref="GrainType"/> 转换为 <see cref="IdSpan"/>。
        /// </summary>
        /// <param name="kind">要转换的 Grain 类型。</param>
        /// <returns>对应的 <see cref="IdSpan"/>。</returns>
        public static explicit operator IdSpan(GrainType kind) => kind._value;

        /// <summary>
        /// 将 <see cref="IdSpan"/> 转换为 <see cref="GrainType"/>。
        /// </summary>
        /// <param name="id">要转换的标识符 Span。</param>
        /// <returns>对应的 <see cref="GrainType"/>。</returns>
        public static explicit operator GrainType(IdSpan id) => new GrainType(id);

        /// <summary>
        /// 获取一个指示此实例是否为默认值的值。
        /// </summary>
        public bool IsDefault => _value.IsDefault;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is GrainType kind && Equals(kind);

        /// <inheritdoc/>
        public bool Equals(GrainType obj) => _value.Equals(obj._value);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>
        /// 为此 Grain 类型生成均匀分布、稳定的哈希码。
        /// </summary>
        /// <returns>
        /// 此实例的均匀分布、稳定的哈希值。
        /// </returns>
        public uint GetUniformHashCode() => _value.GetUniformHashCode();

        /// <summary>
        /// 返回 Grain 类型实例的底层数组。
        /// </summary>
        /// <param name="id">Grain 类型。</param>
        /// <returns>Grain 类型实例的底层数组。</returns>
        /// <remarks>
        /// 不得修改返回的数组。
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
        /// 返回此实例的字符串表示，使用 UTF-8 解码其值。
        /// </summary>
        /// <returns>
        /// 此实例的 <see cref="string"/> 表示。
        /// </returns>
        public override string ToString() => _value.ToString();

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => _value.TryFormat(destination, out charsWritten);

        /// <summary>
        /// 比较给定操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果给定值相等，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public static bool operator ==(GrainType left, GrainType right) => left.Equals(right);

        /// <summary>
        /// 比较给定操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果给定值不相等，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public static bool operator !=(GrainType left, GrainType right) => !(left == right);
    }
}
