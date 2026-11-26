using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 指定一个客户端观察员对象。
    /// </summary>
    internal readonly struct ObserverGrainId : IEquatable<ObserverGrainId>, IComparable<ObserverGrainId>, ISpanFormattable
    {
        /// <summary>
        /// 观察员身份的客户代号部分与客户范围的观察员代号部分之间的分隔。
        /// </summary>
        internal const char SegmentSeparator = '+';

        /// <summary>
        /// 初始化 <see cref="ObserverGrainId"/> 结构的新实例。
        /// </summary>
        private ObserverGrainId(GrainId grainId)
        {
            this.GrainId = grainId;
        }

        /// <summary>
        /// 获取底部 <see cref="GrainId"/>。
        /// </summary>
        public readonly GrainId GrainId;

        /// <summary>
        /// 返回所提供客户名 ID 的新随机 <see cref="ObserverGrainId"/> 例。
        /// </summary>
        /// <param name="clientId">
        /// 客户身份
        /// </param>
        /// <returns>
        /// 提供给客户端 ID 的一个新的随机 <see cref="ObserverGrainId"/> 例。
        /// </returns>
        public static ObserverGrainId Create(ClientGrainId clientId) => Create(clientId, GrainIdKeyExtensions.CreateGuidKey(Guid.NewGuid()));

        /// <summary>
        /// 返回所提供客户名 ID 的新 <see cref="ObserverGrainId"/> 例。
        /// </summary>
        /// <param name="clientId">
        /// 客户身份
        /// </param>
        /// <param name="scopedId">
        /// 客户范围观察员身份。
        /// </param>
        /// <returns>
        /// 所提供客户名id的新 <see cref="ObserverGrainId"/> 例。
        /// </returns>
        public static ObserverGrainId Create(ClientGrainId clientId, IdSpan scopedId) => new ObserverGrainId(ConstructGrainId(clientId, scopedId));

        /// <summary>
        /// 如果所提供的实例代表观察员,则返回 <see langword="true"/> ;如果不是的话,返回 <see langword="false"/> 。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <returns>
        /// <see langword="true"/> 如果已提供 grain id是观察员id,否则 <see langword="false"/>.
        /// </returns>
        public static bool IsObserverGrainId(GrainId grainId) => grainId.IsClient() && grainId.Key.AsSpan().IndexOf((byte)SegmentSeparator) >= 0;

        /// <summary>
        /// 将提供的 <see cref="GrainId"/> 转换为 <see cref="ObserverGrainId"/>。返回值表示操作是否成功。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <param name="observerId">
        /// 相应的观察员id。
        /// </param>
        /// <returns>
        /// <see langword="true"/> 如果已提供 grain id是观察员id,否则 <see langword="false"/>.
        /// </returns>
        public static bool TryParse(GrainId grainId, out ObserverGrainId observerId)
        {
            if (!IsObserverGrainId(grainId))
            {
                observerId = default;
                return false;
            }

            observerId = new ObserverGrainId(grainId);
            return true;
        }

        private static GrainId ConstructGrainId(ClientGrainId clientId, IdSpan scopedId)
        {
            var grain = clientId.GrainId.Key.AsSpan();
            var scope = scopedId.AsSpan();

            var buf = new byte[grain.Length + 1 + scope.Length];
            grain.CopyTo(buf);
            buf[grain.Length] = (byte)SegmentSeparator;
            scope.CopyTo(buf.AsSpan(grain.Length + 1));

            return GrainId.Create(clientId.GrainId.Type, new IdSpan(buf));
        }

        /// <inheritdoc/>
        public bool Equals(ObserverGrainId other) => this.GrainId.Equals(other.GrainId);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ObserverGrainId observer && this.Equals(observer);

        /// <inheritdoc/>
        public override int GetHashCode() => this.GrainId.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => this.GrainId.ToString();

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => ((ISpanFormattable)GrainId).TryFormat(destination, out charsWritten, format, provider);

        /// <inheritdoc/>
        public int CompareTo(ObserverGrainId other) => this.GrainId.CompareTo(other.GrainId);
    }
}
