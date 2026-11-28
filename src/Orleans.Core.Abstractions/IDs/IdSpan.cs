using System;
using System.Runtime.Serialization;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于标识的基础类型，表示字节序列。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct IdSpan : IEquatable<IdSpan>, IComparable<IdSpan>, ISerializable, ISpanFormattable
    {
        /// <summary>
        /// 底层值的稳定哈希。
        /// </summary>
        [Id(0)]
        private readonly int _hashCode;

        /// <summary>
        /// 底层值。
        /// </summary>
        [Id(1)]
        private readonly byte[]? _value;

        /// <summary>
        /// 初始化 <see cref="IdSpan"/> 结构的新实例。
        /// </summary>
        /// <param name="value">
        /// 值。
        /// </param>
        public IdSpan(byte[] value)
        {
            _value = value;
            _hashCode = (int)StableHash.ComputeHash(value);
        }

        /// <summary>
        /// 初始化 <see cref="IdSpan"/> 结构的新实例。
        /// </summary>
        /// <param name="value">
        /// 值。
        /// </param>
        /// <param name="hashCode">
        /// 值的哈希码。
        /// </param>
        private IdSpan(byte[]? value, int hashCode)
        {
            _value = value;
            _hashCode = hashCode;
        }

        /// <summary>
        /// 初始化 <see cref="IdSpan"/> 结构的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列化信息。
        /// </param>
        /// <param name="context">
        /// 上下文。
        /// </param>
        private IdSpan(SerializationInfo info, StreamingContext context)
        {
            _value = (byte[]?)info.GetValue("v", typeof(byte[]));
            _hashCode = info.GetInt32("h");
        }

        /// <summary>
        /// 获取底层值。
        /// </summary>
        public ReadOnlyMemory<byte> Value => _value;

        /// <summary>
        /// 获取一个值，指示此实例是否为默认值。
        /// </summary>
        public bool IsDefault => _value is null || _value.Length == 0;

        /// <summary>
        /// 根据提供的值创建新的 <see cref="IdSpan"/> 实例。
        /// </summary>
        /// <returns>
        /// 与提供的 ID 对应的新 <see cref="IdSpan"/>。
        /// </returns>
        public static IdSpan Create(string id) => new(Encoding.UTF8.GetBytes(id));

        /// <summary>
        /// 返回此实例的 Span 表示形式。
        /// </summary>
        /// <returns>
        /// 此实例的 Span 表示形式。
        /// </returns>
        public ReadOnlySpan<byte> AsSpan() => _value;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is IdSpan kind && Equals(kind);

        /// <inheritdoc/>
        public bool Equals(IdSpan obj)
        {
            if (_value == obj._value)
            {
                return true;
            }

            if (_value is null || obj._value is null)
            {
                return false;
            }

            return _value.AsSpan().SequenceEqual(obj._value);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => _hashCode;

        /// <summary>
        /// 返回 <see cref="IdSpan"/> 的均匀分布且稳定的哈希码。
        /// </summary>
        /// <returns>
        /// 此实例的哈希码。
        /// </returns>
        public uint GetUniformHashCode() => unchecked((uint)_hashCode);

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("v", _value);
            info.AddValue("h", _hashCode);
        }

        /// <summary>
        /// 创建实例，同时指定哈希码和值。
        /// </summary>
        /// <remarks>
        /// 此方法供序列化器和其他底层库使用。
        /// </remarks>
        /// <param name="value">
        /// 底层值。
        /// </param>
        /// <param name="hashCode">
        /// 底层值的哈希。
        /// </param>
        /// <returns>
        /// <see cref="IdSpan"/> 实例。
        /// </returns>
        public static IdSpan UnsafeCreate(byte[]? value, int hashCode) => new(value, hashCode);

        /// <summary>
        /// 获取此实例的底层数组。
        /// </summary>
        /// <param name="id">ID Span。</param>
        /// <returns>此实例的底层数组。</returns>
        public static byte[]? UnsafeGetArray(IdSpan id) => id._value;

        /// <inheritdoc/>
        public int CompareTo(IdSpan other)
        {
            if (_value is null)
            {
                return other._value is null ? 0 : -1;
            }

            if (other._value is null)
            {
                return 1;
            }

            return _value.AsSpan().SequenceCompareTo(other._value.AsSpan());
        }

        /// <summary>
        /// 返回此实例的字符串表示形式，将其值按 UTF-8 解码。
        /// </summary>
        /// <returns>
        /// 此实例的字符串表示形式。
        /// </returns>
        public override string ToString() => _value is null ? "" : Encoding.UTF8.GetString(_value);

        /// <summary>
        /// 将 UTF-8 编码的标识符格式化到提供的字符 Span 中。
        /// </summary>
        /// <param name="destination">用于写入格式化标识符的 Span。</param>
        /// <param name="charsWritten">此方法返回时，包含写入 <paramref name="destination"/> 的字符数。</param>
        /// <returns>如果目标空间足够大，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public bool TryFormat(Span<char> destination, out int charsWritten)
        {
            if (_value is null)
            {
                charsWritten = 0;
                return true;
            }

            var len = Encoding.UTF8.GetCharCount(_value);
            if (destination.Length < len)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = Encoding.UTF8.GetChars(_value, destination);
            return true;
        }

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => TryFormat(destination, out charsWritten);

        /// <summary>
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(IdSpan left, IdSpan right) => left.Equals(right);

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(IdSpan left, IdSpan right) => !left.Equals(right);
    }
}
