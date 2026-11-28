using System;

namespace Orleans.Metadata
{
    /// <summary>
    /// 表示由主版本（最高有效部分）和次版本（最低有效部分）组成的版本。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct MajorMinorVersion : IComparable<MajorMinorVersion>, IEquatable<MajorMinorVersion>
    {
        /// <summary>
        /// 初始化 <see cref="MajorMinorVersion"/> 结构的新实例。
        /// </summary>
        /// <param name="majorVersion">主版本部分。</param>
        /// <param name="minorVersion">次版本部分。</param>
        public MajorMinorVersion(long majorVersion, long minorVersion)
        {
            Major = majorVersion;
            Minor = minorVersion;
        }

        /// <summary>
        /// 获取零值。
        /// </summary>
        public static MajorMinorVersion Zero => new(0, 0);

        /// <summary>
        /// 获取最小值。
        /// </summary>
        public static MajorMinorVersion MinValue => new(long.MinValue, long.MinValue);

        /// <summary>
        /// 获取版本的最高有效部分。
        /// </summary>
        [Id(0)]
        public long Major { get; }

        /// <summary>
        /// 获取版本的最低有效部分。
        /// </summary>
        [Id(1)]
        public long Minor { get; }

        /// <inheritdoc />
        public int CompareTo(MajorMinorVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0) return major;

            return Minor.CompareTo(other.Minor);
        }

        /// <inheritdoc />
        public bool Equals(MajorMinorVersion other) => Major == other.Major && Minor == other.Minor;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is MajorMinorVersion other && this.Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Major, Minor);

        /// <summary>
        /// 解析 <see cref="MajorMinorVersion"/>。
        /// </summary>
        /// <param name="value">
        /// 字符串表示。
        /// </param>
        /// <returns>
        /// 解析得到的 <see cref="MajorMinorVersion"/> 值。
        /// </returns>
        public static MajorMinorVersion Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(value));

            var i = value.IndexOf('.', StringComparison.Ordinal);
            if (i < 0) throw new ArgumentException("The value must contain a major and minor version separated by a period.", nameof(value));
            return new MajorMinorVersion(long.Parse(value[..i]), long.Parse(value[(i + 1)..]));
        }

        /// <inheritdoc />
        public override string ToString() => $"{Major}.{Minor}";

        /// <summary>
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(MajorMinorVersion left, MajorMinorVersion right) => left.Equals(right);

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(MajorMinorVersion left, MajorMinorVersion right) => !left.Equals(right);

        /// <summary>
        /// 比较提供的操作数，左操作数大于或等于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数大于或等于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator >=(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) >= 0;

        /// <summary>
        /// 比较提供的操作数，左操作数小于或等于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数小于或等于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator <=(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// 比较提供的操作数，左操作数大于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数大于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator >(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) > 0;

        /// <summary>
        /// 比较提供的操作数，左操作数小于右操作数时返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果左操作数小于右操作数，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator <(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) < 0;
    }
}
