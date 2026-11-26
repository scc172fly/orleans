using System;
using System.Runtime.Serialization;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 身份原始类型,代表一个字节序列。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct IdSpan : IEquatable<IdSpan>, IComparable<IdSpan>, ISerializable, ISpanFormattable
    {
        /// <summary>
        /// 基本价值的稳定散列。
        /// </summary>
        [Id(0)]
        private readonly int _hashCode;

        /// <summary>
        /// 基本价值。
        /// </summary>
        [Id(1)]
        private readonly byte[]? _value;

        /// <summary>
        /// 初始化 <see cref="IdSpan"/> 结构的新实例。
        /// </summary>
        /// <param name="value">
        /// 价值。
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
        /// 价值。
        /// </param>
        /// <param name="hashCode">
        /// 值的散列代码 。
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
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        private IdSpan(SerializationInfo info, StreamingContext context)
        {
            _value = (byte[]?)info.GetValue("v", typeof(byte[]));
            _hashCode = info.GetInt32("h");
        }

        /// <summary>
        /// 获得基本价值。
        /// </summary>
        public ReadOnlyMemory<byte> Value => _value;

        /// <summary>
        /// 获得一个值,以显示此实例是否为默认值。
        /// </summary>
        public bool IsDefault => _value is null || _value.Length == 0;

        /// <summary>
        /// 从所提供的值创建新的 <see cref="IdSpan"/> 实例。
        /// </summary>
        /// <returns>
        /// 与所提供的代号相应的新 <see cref="IdSpan"/> 。
        /// </returns>
        public static IdSpan Create(string id) => new(Encoding.UTF8.GetBytes(id));

        /// <summary>
        /// 返回此实例的跨度代表。
        /// </summary>
        /// <returns>
        /// 本案的跨度代表。
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
        /// 返回 <see cref="IdSpan"/>的统一、稳定的散列代码。
        /// </summary>
        /// <returns>
        /// 此处的散列代码 。
        /// </returns>
        public uint GetUniformHashCode() => unchecked((uint)_hashCode);

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("v", _value);
            info.AddValue("h", _hashCode);
        }

        /// <summary>
        /// 创建实例,指定散列代码和值。
        /// </summary>
        /// <remarks>
        /// 这种方法供序列器和其他低层图书馆使用。
        /// </remarks>
        /// <param name="value">
        /// 基本价值。
        /// </param>
        /// <param name="hashCode">
        /// 基本价值的散列。
        /// </param>
        /// <returns>
        /// <see cref="IdSpan"/> 实例。
        /// </returns>
        public static IdSpan UnsafeCreate(byte[]? value, int hashCode) => new(value, hashCode);

        /// <summary>
        /// 从此实例中获取底部矩阵 。
        /// </summary>
        /// <param name="id">假跨度。</param>
        /// <returns>此实例的底部数组。</returns>
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
        /// 返回此实例的字符串表示,将值解码为 UTF8。
        /// </summary>
        /// <returns>
        /// 字符串表示符在此实例下 。
        /// </returns>
        public override string ToString() => _value is null ? "" : Encoding.UTF8.GetString(_value);

        /// <summary>
        /// 将 UTF-8 编码识别符格式化为提供的字符范围。
        /// </summary>
        /// <param name="destination">写格式化标识符的间隔。</param>
        /// <param name="charsWritten">当此方法返回时, 包含写到 <paramref name="destination"/>的字符数。</param>
        /// <returns><see langword="true"/> 如果目的地足够大;否则, <see langword="false"/>。</returns>
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
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(IdSpan left, IdSpan right) => left.Equals(right);

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(IdSpan left, IdSpan right) => !left.Equals(right);
    }
}
