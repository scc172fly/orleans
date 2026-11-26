using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime
{
    /// <summary>
    /// 代表一个分组成员。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class ClusterMember : IEquatable<ClusterMember>, ISpanFormattable
    {
        /// <summary>
        /// 初始化 <see cref="ClusterMember"/> 类的新实例。
        /// </summary>                
        /// <param name="siloAddress">
        /// silo 地址。
        /// </param>
        /// <param name="status">
        /// 状态 silo 。
        /// </param>
        /// <param name="name">
        /// silo 名称。
        /// </param>
        public ClusterMember(SiloAddress siloAddress, SiloStatus status, string name)
            : this(siloAddress, status, name, wasDeclaredDead: false)
        {
        }

        internal ClusterMember(SiloAddress siloAddress, SiloStatus status, string name, bool wasDeclaredDead)
        {
            this.SiloAddress = siloAddress ?? throw new ArgumentNullException(nameof(siloAddress));
            this.Status = status;
            this.Name = name;
            this.WasDeclaredDead = wasDeclaredDead;
        }

        /// <summary>
        /// 获取 silo 地址。
        /// </summary>
        /// <value> silo 地址。</value>
        [Id(0)]
        public SiloAddress SiloAddress { get; }

        /// <summary>
        /// 获取 silo 状态。
        /// </summary>
        /// <value> silo 状态。</value>
        [Id(1)]
        public SiloStatus Status { get; }

        /// <summary>
        /// 获取 silo 名称。
        /// </summary>
        /// <value> silo 名称。</value>
        [Id(2)]
        public string Name { get; }

        [Id(3)]
        internal bool WasDeclaredDead { get; }

        /// <inheritdoc/>
        public override bool Equals([NotNullWhen(true)] object? obj) => this.Equals(obj as ClusterMember);

        /// <inheritdoc/>
        public bool Equals(ClusterMember? other) => other != null
            && this.SiloAddress.Equals(other.SiloAddress)
            && this.Status == other.Status
            && string.Equals(this.Name, other.Name, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override int GetHashCode() => this.SiloAddress.GetConsistentHashCode();

        /// <inheritdoc/>
        public override string ToString() => $"{this.SiloAddress}/{this.Name}/{this.Status}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"{this.SiloAddress}/{this.Name}/{this.Status}", out charsWritten);
    }
}
