#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.Cosmos;
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.Cosmos;
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.Cosmos;
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.Cosmos;
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.Cosmos;
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif

/// <summary>
/// 验证 <see cref="CosmosOptions"/> 的实例。
/// </summary>
/// <typeparam name="TOptions">选项类型。</typeparam>
public class CosmosOptionsValidator<TOptions> : IConfigurationValidator where TOptions : CosmosOptions
{
    private readonly TOptions _options;
    private readonly string _name;

    /// <summary>
    /// 初始化 <see cref="CosmosOptionsValidator{TOptions}"/> 类型的新实例。
    /// </summary>
    /// <param name="options">要验证的实例。</param>
    /// <param name="name">要验证的选项名称。</param>
    public CosmosOptionsValidator(TOptions options, string name)
    {
        _options = options;
        _name = name;
    }

    /// <inheritdoc/>
    public void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.DatabaseName))
            throw new OrleansConfigurationException(
                $"Configuration for Azure Cosmos DB provider {_name} is invalid. {nameof(_options.DatabaseName)} is not valid.");

        if (string.IsNullOrWhiteSpace(_options.ContainerName))
            throw new OrleansConfigurationException(
                $"Configuration for Azure Cosmos DB provider {_name} is invalid. {nameof(_options.ContainerName)} is not valid.");

        if (_options.CreateClient is null)
        {
            throw new OrleansConfigurationException(
                $"Configuration for Azure Cosmos DB provider {_name} is invalid. You must call {nameof(_options.ConfigureCosmosClient)} to configure access to Azure Cosmos DB.");
        }
    }
}
