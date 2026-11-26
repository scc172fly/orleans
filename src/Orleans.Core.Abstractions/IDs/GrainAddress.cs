using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Orleans.GrainDirectory;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示 <see cref="IGrainDirectory"/>中的条目
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class GrainAddress : IEquatable<GrainAddress>, ISpanFormattable
    {
        [Id(0)]
        private readonly GrainId _grainId;

        [Id(1)]
        private readonly ActivationId _activationId;

        /// <summary>
        /// Grain的标识符
        /// </summary>
        public GrainId GrainId { get => _grainId; init => _grainId = value; }

        /// <summary>
        /// 特定 Grain 激活的 Id
        /// </summary>
        public ActivationId ActivationId { get => _activationId; init => _activationId = value; }

        /// <summary>
        /// 唱唱唱唱唱唱唱的要 silo 位于 grain 激活生命
        /// </summary>
        [Id(2)]
        public SiloAddress? SiloAddress { get; init; }

        /// <summary>
        /// 登记时的MembershipVersion
        /// </summary>
        [Id(3)]
        public MembershipVersion MembershipVersion { get; init; } = MembershipVersion.MinValue;

        /// <summary>
        /// 获得一个值,以显示是否指定了 grain、激活和 silo 识别符号。
        /// </summary>
        [JsonIgnore]
        public bool IsComplete => !_grainId.IsDefault && !_activationId.IsDefault && SiloAddress != null;

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as GrainAddress);

        /// <inheritdoc />
        public bool Equals(GrainAddress? other)
        {
            if (ReferenceEquals(this, other)) return true;
            return MatchesGrainIdAndSilo(this, other)
                && _activationId.Equals(other._activationId);
        }

        /// <summary>
        /// 两个 grain 地址匹配,如果其数值等于 <see cref="SiloAddress"/> 和 <see cref="GrainId"/>
        /// 并且一方有默认 <see cref="ActivationId"/> 值,或两者均具有等同的 <see cref="ActivationId"/> 值。
        /// </summary>
        /// <param name="other"> 另一 <see cref="GrainAddress"/> 来比较这个和。</param>
        /// <returns> 返回 <c>true</c> 如果两者 <see cref="GrainAddress"/> 认为匹配。</returns>
        public bool Matches([NotNullWhen(true)] GrainAddress? other)
        {
            if (ReferenceEquals(this, other)) return true;
            return MatchesGrainIdAndSilo(this, other)
                && (_activationId.IsDefault || other._activationId.IsDefault || _activationId.Equals(other._activationId));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool MatchesGrainIdAndSilo([NotNullWhen(true)] GrainAddress? address, [NotNullWhen(true)] GrainAddress? other)
        {
            return other is not null
                && address is not null
                && address.GrainId.Equals(other.GrainId)
                && !(address.SiloAddress is null ^ other.SiloAddress is null)
                && (address.SiloAddress is null || address.SiloAddress.Equals(other.SiloAddress));
        }

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(SiloAddress, _grainId, _activationId);

        /// <inheritdoc />
        public override string ToString() => $"[{nameof(GrainAddress)} GrainId {_grainId}, ActivationId: {_activationId}, SiloAddress: {SiloAddress}]";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"[{nameof(GrainAddress)} GrainId {_grainId}, ActivationId: {_activationId}, SiloAddress: {SiloAddress}]", out charsWritten);

        /// <summary>
        /// 返回字符串表示式,包括 grain、激活、 silo和会籍版本。
        /// </summary>
        /// <returns>此 grain 地址的详细字符串表示。</returns>
        public string ToFullString() => $"[{nameof(GrainAddress)} GrainId {_grainId}, ActivationId: {_activationId}, SiloAddress: {SiloAddress}, MembershipVersion: {MembershipVersion}]";

        internal static GrainAddress NewActivationAddress(SiloAddress silo, GrainId grain) => GetAddress(silo, grain, ActivationId.NewId());

        internal static GrainAddress GetAddress(SiloAddress? silo, GrainId grain, ActivationId activation)
        {
            // Silo 部分非强制性
            if (grain.IsDefault) throw new ArgumentNullException(nameof(grain));

            return new GrainAddress
            {
                GrainId = grain,
                ActivationId = activation,
                SiloAddress = silo,
            };
        }
    }
}
