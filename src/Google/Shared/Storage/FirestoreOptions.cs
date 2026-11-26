using Orleans.Runtime;

#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.Firestore;
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.Firestore;
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.Firestore;
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.Firestore;
#else
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif

/// <summary>
/// Google 云烟火炉备选方案
/// </summary>
public class FirestoreOptions
{
    /// <summary>
    /// 谷歌云项目id。
    /// </summary>
    public string ProjectId { get; set; } = default!;

    /// <summary>
    /// 火炉模拟主机。 请使用 Google Cloud Firshstore , 保留未设定的位置 。
    /// </summary>
    public string? EmulatorHost { get; set; }

    /// <summary>
    /// 谷歌云火炉根收集名称。
    /// </summary>
    public string RootCollectionName { get; set; } = "Orleans";

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(this.RootCollectionName))
            throw new OrleansConfigurationException("RootCollectionName is required.");

        if (Utils.ForbiddenIdRegex().IsMatch(this.RootCollectionName))
            throw new OrleansConfigurationException(
                $"The RootCollectionName '{this.RootCollectionName}' contains invalid characters.");

        if (this.RootCollectionName.Contains('/', StringComparison.Ordinal))
            throw new OrleansConfigurationException("RootCollectionName must be a single Firestore collection identifier.");

        if (string.IsNullOrWhiteSpace(this.ProjectId))
            throw new OrleansConfigurationException("ProjectId is required.");

        if (Utils.ForbiddenIdRegex().IsMatch(this.ProjectId))
            throw new OrleansConfigurationException($"The ProjectId '{this.ProjectId}' contains invalid characters.");
    }
}

internal sealed class FirestoreOptionsValidator<TOptions> : IConfigurationValidator where TOptions : FirestoreOptions
{
    private readonly TOptions _options;

    public FirestoreOptionsValidator(TOptions options) => _options = options;

    public void ValidateConfiguration() => _options.Validate();
}
