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
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif

/// <summary>
/// Google Cloud Firestore 选项。
/// </summary>
public class FirestoreOptions
{
    /// <summary>
    /// Google Cloud 项目标识。
    /// </summary>
    public string ProjectId { get; set; } = default!;

    /// <summary>
    /// Firestore 模拟器主机。未设置时使用 Google Cloud Firestore。
    /// </summary>
    public string? EmulatorHost { get; set; }

    /// <summary>
    /// Google Cloud Firestore 根集合名称。
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
