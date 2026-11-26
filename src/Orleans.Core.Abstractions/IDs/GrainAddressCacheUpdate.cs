using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime;

/// <summary>
/// 表示将一个无效的缓存 <see cref="GrainAddress"/> 更新为有效 <see cref="GrainAddress"/>的指示。
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
    /// <param name="invalidAddress">缓存 grain 地址无效。</param>
    /// <param name="validAddress">替换 grain 地址或 <see langword="null"/> ,以删除无效地址而不替换。</param>
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
    /// Grain的标识。
    /// </summary>
    public GrainId GrainId => _grainId;

    /// <summary>
    /// 无效 grain 激活的标识符 。
    /// </summary>
    public ActivationId InvalidActivationId => _invalidActivationId;

    /// <summary>
    /// 唱唱唱唱唱唱唱的要 silo 无效者指定的 grain 启动缓存进入
    /// </summary>
    public SiloAddress? InvalidSiloAddress => _invalidSiloAddress;

    /// <summary>
    /// 获取有效的 grain 激活地址。
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
    /// 获取无效的 grain 激活地址。
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
    /// 返回字符串代表,包括无效地址和替换地址以及无效地址会籍版本。
    /// </summary>
    /// <returns>此缓存更新的详细字符串表示 。</returns>
    public string ToFullString() => $"[{nameof(GrainAddressCacheUpdate)} GrainId {_grainId}, InvalidActivationId: {_invalidActivationId}, InvalidSiloAddress: {_invalidSiloAddress}, ValidGrainAddress: {ValidGrainAddress}, MembershipVersion: {_invalidMembershipVersion}]";

    [DoesNotReturn]
    private static void ThrowGrainIdDoesNotMatch(GrainAddress invalidAddress, GrainAddress validAddress) => throw new ArgumentException($"Invalid grain address grain id {invalidAddress.GrainId} does not match valid grain address grain id {validAddress.GrainId}.", nameof(validAddress));
}
