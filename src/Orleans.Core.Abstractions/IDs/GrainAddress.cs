using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Orleans.GrainDirectory;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示 <see cref="IGrainDirectory"/> 中的条目。
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class GrainAddress : IEquatable<GrainAddress>, ISpanFormattable
    {
        [Id(0)]
        private readonly GrainId _grainId;

        [Id(1)]
        private readonly ActivationId _activationId;

        /// <summary>
        /// Grain 的标识符。
        /// </summary>
        public GrainId GrainId { get => _grainId; init => _grainId = value; }

        /// <summary>
        /// 特定 Grain 激活实例的 ID。
        /// </summary>
        public ActivationId ActivationId { get => _activationId; init => _activationId = value; }

        /// <summary>
        /// Grain 激活实例所在的 Silo 的地址。
        /// </summary>
        [Id(2)]
        public SiloAddress? SiloAddress { get; init; }

        /// <summary>
        /// 注册时的 MembershipVersion。
        /// </summary>
        [Id(3)]
        public MembershipVersion MembershipVersion { get; init; } = MembershipVersion.MinValue;

        /// <summary>
        /// 获取一个值，指示是否已指定 Grain、激活实例以及 Silo 的全部标识符。
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
        /// 如果两个 Grain 地址的 <see cref="SiloAddress"/> 与 <see cref="GrainId"/> 值分别相等，
        /// 并且满足以下任一条件：其中一个具有默认 <see cref="ActivationId"/> 值，或二者的 <see cref="ActivationId"/> 值相等，则两个地址匹配。
        /// </summary>
        /// <param name="other">要与此地址比较的另一个 <see cref="GrainAddress"/>。</param>
        /// <returns>返回 <c>true</c> 表示两个 <see cref="GrainAddress"/> 被视为匹配。</returns>
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
        /// 返回包含 Grain、激活实例、Silo 及成员资格版本的字符串表示形式。
        /// </summary>
        /// <returns>此 Grain 地址的详细字符串表示形式。</returns>
        public string ToFullString() => $"[{nameof(GrainAddress)} GrainId {_grainId}, ActivationId: {_activationId}, SiloAddress: {SiloAddress}, MembershipVersion: {MembershipVersion}]";

        internal static GrainAddress NewActivationAddress(SiloAddress silo, GrainId grain) => GetAddress(silo, grain, ActivationId.NewId());

        internal static GrainAddress GetAddress(SiloAddress? silo, GrainId grain, ActivationId activation)
        {
            // Silo 部分不是必需的。
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
