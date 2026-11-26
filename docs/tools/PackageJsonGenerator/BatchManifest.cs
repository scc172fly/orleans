// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Text.Json.Serialization;

namespace PackageJsonGenerator;

/// <summary>
/// 批处理多包件单次工具调用的JSON 货单。
/// </summary>
internal sealed class BatchManifest
{
    [JsonPropertyName("packages")]
    public List<BatchPackageEntry> Packages { get; set; } = [];
}

/// <summary>
/// <see cref="BatchManifest"/>内的一个单一包件条目。
/// </summary>
internal sealed class BatchPackageEntry
{
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    [JsonPropertyName("references")]
    public string[] References { get; set; } = [];

    [JsonPropertyName("output")]
    public string Output { get; set; } = "";

    [JsonPropertyName("packageVersion")]
    public string? PackageVersion { get; set; }

    [JsonPropertyName("packageName")]
    public string? PackageName { get; set; }

    [JsonPropertyName("sourceRepo")]
    public string? SourceRepo { get; set; }

    [JsonPropertyName("sourceCommit")]
    public string? SourceCommit { get; set; }

    [JsonPropertyName("sourceRoot")]
    public string? SourceRoot { get; set; }

    [JsonPropertyName("sourceFileManifest")]
    public string? SourceFileManifest { get; set; }

    [JsonPropertyName("targetFramework")]
    public string? TargetFramework { get; set; }
}
