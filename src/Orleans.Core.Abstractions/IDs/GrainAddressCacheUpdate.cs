using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime;

/// <summary>
/// 表示一条将缓存中无效的 <see cref="GrainAddress"/> 更新为有效 <see cref="GrainAddress"/> 的指令。
/// </summary>
[GenerateSerializer, Immutable]
public sealed class GrainAddressCacheUpdate : ISpanFormattable
{
    [Id(0)]
    private readonly GrainId _grainId;

    [Id(1)]
    private readonly ActivationId _invalidActivationId;

    [Id(2)]
    private readonly SiloAddress? _invalidSiloAddress;

    [Id(3)]
    private readonly MembershipVersion _invalidMembershipVersion = MembershipVersion.MinValue;

    [Id(4)]
    private readonly ActivationId _validActivationId;

    [Id(5)]
    private readonly SiloAddress? _validSiloAddress;

    [Id(6)]
    private readonly MembershipVersion _validMembershipVersion = MembershipVersion.MinValue;

    /// <summary>
    /// 初始化 <see cref="GrainAddressCacheUpdate"/> 类的新实例。
    /// </summary>
    /// <param name="invalidAddress">要使其失效的缓存 Grain 地址。</param>
    /// <param name="validAddress">替换用的 Grain 地址；若为 <see langword="null"/>，则移除无效地址而不进行替换。</param>
    public GrainAddressCacheUpdate(GrainAddress invalidAddress, GrainAddress? validAddress)
    {
        ArgumentNullException.ThrowIfNull(invalidAddress);

        _grainId = invalidAddress.GrainId;
        _invalidActivationId = invalidAddress.ActivationId;
        _invalidSiloAddress = invalidAddress.SiloAddress;
        _invalidMembershipVersion = invalidAddress.MembershipVersion;

        if (validAddress is not null)
        {
            if (invalidAddress.GrainId != validAddress.GrainId)
            {
                ThrowGrainIdDoesNotMatch(invalidAddress, validAddress);
                return;
            }

            _validActivationId = validAddress.ActivationId;
            _validSiloAddress = validAddress.SiloAddress;
            _validMembershipVersion = validAddress.MembershipVersion;
        }
    }

    /// <summary>
    /// Grain 的标识符。
    /// </summary>
    public GrainId GrainId => _grainId;

    /// <summary>
    /// 无效 Grain 激活实例的标识符。
    /// </summary>
    public ActivationId InvalidActivationId => _invalidActivationId;

    /// <summary>
    /// 无效 Grain 激活实例缓存条目所指示的 Silo 的地址。
    /// </summary>
    public SiloAddress? InvalidSiloAddress => _invalidSiloAddress;

    /// <summary>
    /// 获取有效的 Grain 激活实例地址。
    /// </summary>
    public GrainAddress? ValidGrainAddress => _validSiloAddress switch
    {
        null => null,
        _ => new()
        {
            GrainId = _grainId,
            ActivationId = _validActivationId,
            SiloAddress = _validSiloAddress,
            MembershipVersion = _validMembershipVersion,
        }
    };

    /// <summary>
    /// 获取无效的 Grain 激活实例地址。
    /// </summary>
    public GrainAddress InvalidGrainAddress => new()
    {
        GrainId = _grainId,
        ActivationId = _invalidActivationId,
        SiloAddress = _invalidSiloAddress,
        MembershipVersion = _invalidMembershipVersion,
    };

    /// <inheritdoc />
    public override string ToString() => $"[{nameof(GrainAddressCacheUpdate)} GrainId {_grainId}, InvalidActivationId: {_invalidActivationId}, InvalidSiloAddress: {_invalidSiloAddress}, ValidGrainAddress: {ValidGrainAddress}]";

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => destination.TryWrite($"[{nameof(GrainAddressCacheUpdate)} GrainId {_grainId}, InvalidActivationId: {_invalidActivationId}, InvalidSiloAddress: {_invalidSiloAddress}, ValidGrainAddress: {ValidGrainAddress}]", out charsWritten);

    /// <summary>
    /// 返回包含无效地址、替换地址以及无效地址的成员资格版本的字符串表示形式。
    /// </summary>
    /// <returns>此次缓存更新的详细字符串表示形式。</returns>
    public string ToFullString() => $"[{nameof(GrainAddressCacheUpdate)} GrainId {_grainId}, InvalidActivationId: {_invalidActivationId}, InvalidSiloAddress: {_invalidSiloAddress}, ValidGrainAddress: {ValidGrainAddress}, MembershipVersion: {_invalidMembershipVersion}]";

    [DoesNotReturn]
    private static void ThrowGrainIdDoesNotMatch(GrainAddress invalidAddress, GrainAddress validAddress) => throw new ArgumentException($"Invalid grain address grain id {invalidAddress.GrainId} does not match valid grain address grain id {validAddress.GrainId}.", nameof(validAddress));
}
