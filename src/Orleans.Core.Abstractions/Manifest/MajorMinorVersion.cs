using System;

namespace Orleans.Metadata
{
    /// <summary>
    /// 代表一个版本,有两个组成部分:主要(最重大)部分和次要(最微小)部分。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct MajorMinorVersion : IComparable<MajorMinorVersion>, IEquatable<MajorMinorVersion>
    {
        /// <summary>
        /// 初始化 <see cref="MajorMinorVersion"/> 结构的新实例。
        /// </summary>
        /// <param name="majorVersion">主版本组件。</param>
        /// <param name="minorVersion">次要版本组件。</param>
        public MajorMinorVersion(long majorVersion, long minorVersion)
        {
            Major = majorVersion;
            Minor = minorVersion;
        }

        /// <summary>
        /// 得到零值。
        /// </summary>
        public static MajorMinorVersion Zero => new(0, 0);

        /// <summary>
        /// 获得最低价值。
        /// </summary>
        public static MajorMinorVersion MinValue => new(long.MinValue, long.MinValue);

        /// <summary>
        /// 获得最重要的版本组件。
        /// </summary>
        [Id(0)]
        public long Major { get; }

        /// <summary>
        /// 获得最小的版本组件 。
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
        /// 分析 <see cref="MajorMinorVersion"/>。
        /// </summary>
        /// <param name="value">
        /// 字符串代表。
        /// </param>
        /// <returns>
        /// 分析 <see cref="MajorMinorVersion"/> 值。
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
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(MajorMinorVersion left, MajorMinorVersion right) => left.Equals(right);

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(MajorMinorVersion left, MajorMinorVersion right) => !left.Equals(right);

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 倘若左边的手术功过右的或等量,否则 <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形操作大于或等于右形操作,否则 <see langword="false"/>。</returns>
        public static bool operator >=(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) >= 0;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> ,如果左操作小于或等于右操作,否则 <see langword="false"/>。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形操作小于或等于右形操作,否则 <see langword="false"/>。</returns>
        public static bool operator <=(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 倘若左边的手术胜过右边的手术,否则 <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左形大于右形,否则 <see langword="false"/>。</returns>
        public static bool operator >(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) > 0;

        /// <summary>
        /// 比较所提供的操作和返回 <see langword="true"/> 如果左边的手术功率低于右边的手术功率,那末, <see langword="false"/>.
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果左边的手术功率低于右边的手术功率,那末, <see langword="false"/>.</returns>
        public static bool operator <(MajorMinorVersion left, MajorMinorVersion right) => left.CompareTo(right) < 0;
    }
}
