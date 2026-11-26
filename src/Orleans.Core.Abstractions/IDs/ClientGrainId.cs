using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 确定客户身份。
    /// </summary>
    internal readonly struct ClientGrainId : IEquatable<ClientGrainId>, IComparable<ClientGrainId>, ISpanFormattable
    {
        /// <summary>
        /// 创建新的 <see cref="ClientGrainId"/> 实例。
        /// </summary>
        private ClientGrainId(GrainId grainId) => this.GrainId = grainId;

        /// <summary>
        /// 获取底部 <see cref="GrainId"/>。
        /// </summary>
        public readonly GrainId GrainId;

        /// <summary>
        /// 创建新的 <see cref="ClientGrainId"/> 实例。
        /// </summary>
        public static ClientGrainId Create() => Create(GrainIdKeyExtensions.CreateGuidKey(Guid.NewGuid()));

        /// <summary>
        /// 创建新的 <see cref="ClientGrainId"/> 实例。
        /// </summary>
        public static ClientGrainId Create(string id)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(id);
            return Create(IdSpan.Create(id));
        }

        /// <summary>
        /// 创建新的 <see cref="ClientGrainId"/> 实例。
        /// </summary>
        public static ClientGrainId Create(IdSpan id) => new ClientGrainId(new GrainId(GrainTypePrefix.ClientGrainType, id));

        /// <summary>
        /// 将提供的 <see cref="GrainId"/> 转换为 <see cref="ClientGrainId"/>。返回值表示操作是否成功。
        /// </summary>
        public static bool TryParse(GrainId grainId, out ClientGrainId clientId)
        {
            if (!grainId.Type.IsClient())
            {
                clientId = default;
                return false;
            }

            // 观察员身份(如果在场)。
            var key = grainId.Key.AsSpan();
            if (key.IndexOf((byte)ObserverGrainId.SegmentSeparator) is int index && index >= 0)
            {
                key = key[..index];
                grainId = new GrainId(grainId.Type, new IdSpan(key.ToArray()));
            }

            clientId = new ClientGrainId(grainId);
            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ClientGrainId clientId && GrainId.Equals(clientId.GrainId);

        /// <inheritdoc/>
        public override int GetHashCode() => this.GrainId.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => this.GrainId.ToString();

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => ((ISpanFormattable)GrainId).TryFormat(destination, out charsWritten, format, provider);

        /// <inheritdoc/>
        public bool Equals(ClientGrainId other) => this.GrainId.Equals(other.GrainId);

        /// <inheritdoc/>
        public int CompareTo(ClientGrainId other) => this.GrainId.CompareTo(other.GrainId);

        /// <summary>
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(ClientGrainId left, ClientGrainId right) => left.Equals(right);

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(ClientGrainId left, ClientGrainId right) => !(left == right);
    }
}
