using System.Collections.Generic;
using System.Collections.Immutable;

namespace Orleans.Runtime.MembershipService.SiloMetadata;

/// <summary>
/// 描述与 Silo 关联的元数据。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Runtime.MembershipService.SiloMetadata.SiloMetadata")]
public record SiloMetadata
{
    /// <summary>
    /// 获取一个不包含元数据的实例。
    /// </summary>
    public static SiloMetadata Empty { get; } = new SiloMetadata();

    /// <summary>
    /// 获取与 Silo 关联的元数据键值对。
    /// </summary>
    [Id(0)]
    public ImmutableDictionary<string, string> Metadata { get; private set; } = ImmutableDictionary<string, string>.Empty;

    internal void AddMetadata(IEnumerable<KeyValuePair<string, string>> metadata) => Metadata = Metadata.SetItems(metadata);
    internal void AddMetadata(string key, string value) => Metadata = Metadata.SetItem(key, value);
}
