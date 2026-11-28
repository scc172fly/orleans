namespace Orleans.GrainDirectory.AdoNet;

/// <summary>
/// 表示 ADO.NET Grain 目录中的 Grain 激活实例的模型。
/// </summary>
internal sealed record AdoNetGrainDirectoryEntry(
    string ClusterId,
    string ProviderId,
    string GrainId,
    string SiloAddress,
    string ActivationId)
{
    public AdoNetGrainDirectoryEntry() : this("", "", "", "", "")
    {
    }

    public GrainAddress ToGrainAddress() => new()
    {
        GrainId = Runtime.GrainId.Parse(GrainId, CultureInfo.InvariantCulture),
        SiloAddress = Runtime.SiloAddress.FromParsableString(SiloAddress),
        ActivationId = Runtime.ActivationId.FromParsableString(ActivationId)
    };

    public static AdoNetGrainDirectoryEntry FromGrainAddress(string clusterId, string providerId, GrainAddress address)
    {
        ArgumentNullException.ThrowIfNull(clusterId);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(address.SiloAddress);

        return new AdoNetGrainDirectoryEntry(
            clusterId,
            providerId,
            address.GrainId.ToString(),
            address.SiloAddress.ToParsableString(),
            address.ActivationId.ToParsableString());
    }
}
