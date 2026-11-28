using System.Net;
#if ORLEANS_PERSISTENCE
using System.Threading;
#endif
using Azure;
using Azure.Core;

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
/// Azure Cosmos DB 存储选项。
/// </summary>
public abstract class CosmosOptions
{
    /// <summary>
    /// 尝试在集群管理所用的数据库及容器不存在时创建它们。默认为 <see langword="false"/>。
    /// </summary>
    public bool IsResourceCreationEnabled { get; set; }

    /// <summary>
    /// 数据库配置的吞吐量。如果设置为默认值 <see langword="null"/>，则不配置吞吐量。
    /// </summary>
    /// <seealso href="https://learn.microsoft.com/azure/cosmos-db/set-throughput"/>
    public int? DatabaseThroughput { get; set; }

    /// <summary>
    /// 用于集群信息的数据库名称。默认为 <c>Orleans</c>。
    /// </summary>
    public string DatabaseName { get; set; } = "Orleans";

    /// <summary>
    /// 用于存储集群信息的容器名称。
    /// </summary>
    public string ContainerName { get; set; } = default!;

    /// <summary>
    /// 容器的吞吐量属性。默认值为 <see langword="null"/>，表示使用无服务器吞吐量模式。
    /// </summary>
    /// <seealso href="https://learn.microsoft.com/azure/cosmos-db/set-throughput"/>
    public ThroughputProperties? ContainerThroughputProperties { get; set; }

    /// <summary>
    /// 初始化时删除数据库。仅用于测试场景。
    /// </summary>
    /// <remarks>此选项仅用于测试场景。</remarks>
    public bool CleanResourcesOnInitialization { get; set; }

    /// <summary>
    /// 传递给 Cosmos DB 客户端的选项。
    /// </summary>
    public CosmosClientOptions ClientOptions { get; set; } = new();

    /// <summary>
    /// 用于通过 Cosmos DB 客户端执行操作的操作执行器。
    /// </summary>
    public ICosmosOperationExecutor OperationExecutor { get; set; } = DefaultCosmosOperationExecutor.Instance;

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="connectionString">连接字符串。</param>
    /// <see cref="CosmosClient(string, CosmosClientOptions)"/>
    public void ConfigureCosmosClient(string connectionString)
    {
        CreateClient = _ => new(new CosmosClient(connectionString, ClientOptions));
    }

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="accountEndpoint">账户终结点，格式为 <code>https://{databaseaccount}.documents.azure.com:443/</code>。参见 <see href="https://learn.microsoft.com/rest/api/cosmos-db/cosmosdb-resource-uri-syntax-for-rest"/>。</param>
    /// <param name="authKeyOrResourceTokenCredential"><see cref="AzureKeyCredential"/>，使用主密钥或资源令牌。</param>
    /// <see cref="CosmosClient(string, AzureKeyCredential, CosmosClientOptions)"/>
    public void ConfigureCosmosClient(string accountEndpoint, AzureKeyCredential authKeyOrResourceTokenCredential)
    {
        CreateClient = _ => new(new CosmosClient(accountEndpoint, authKeyOrResourceTokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="accountEndpoint">账户终结点，格式为 <code>https://{databaseaccount}.documents.azure.com:443/</code>。参见 <see href="https://learn.microsoft.com/rest/api/cosmos-db/cosmosdb-resource-uri-syntax-for-rest"/>。</param>
    /// <param name="tokenCredential">提供给 AAD 用于授权的令牌。</param>
    /// <see cref="CosmosClient(string, TokenCredential, CosmosClientOptions)"/>
    public void ConfigureCosmosClient(string accountEndpoint, TokenCredential tokenCredential)
    {
        CreateClient = _ => new(new CosmosClient(accountEndpoint, tokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="accountEndpoint">账户终结点，格式为 <code>https://{databaseaccount}.documents.azure.com:443/</code>。参见 <see href="https://learn.microsoft.com/rest/api/cosmos-db/cosmosdb-resource-uri-syntax-for-rest"/>。</param>
    /// <param name="authKeyOrResourceToken">用于创建客户端的 Cosmos 账户密钥或资源令牌。</param>
    /// <see cref="CosmosClient(string, TokenCredential, CosmosClientOptions)"/>
    public void ConfigureCosmosClient(string accountEndpoint, string authKeyOrResourceToken)
    {
        CreateClient = _ => new(new CosmosClient(accountEndpoint, authKeyOrResourceToken, ClientOptions));
    }

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="createClient">用于创建 Cosmos DB 客户端的委托。</param>
    public void ConfigureCosmosClient(Func<IServiceProvider, ValueTask<CosmosClient>> createClient)
    {
        CreateClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
    }

    /// <summary>
    /// 用于创建 <see cref="CosmosClient"/> 的工厂方法。
    /// </summary>
    internal Func<IServiceProvider, ValueTask<CosmosClient>> CreateClient { get; private set; } = null!;
}

/// <summary>
/// 使用 Cosmos DB 客户端执行操作的功能。
/// </summary>
public interface ICosmosOperationExecutor
{
    /// <summary>
    /// 执行提供的 Cosmos DB 操作。
    /// </summary>
    /// <typeparam name="TArg">函数参数的类型。</typeparam>
    /// <typeparam name="TResult">结果值的类型。</typeparam>
    /// <param name="func">要执行的委托。</param>
    /// <param name="arg">传递给委托调用的参数。</param>
    /// <returns>调用委托的结果。</returns>
    Task<TResult> ExecuteOperation<TArg, TResult>(Func<TArg, Task<TResult>> func, TArg arg);

#if ORLEANS_PERSISTENCE
    /// <summary>
    /// 执行提供的 Cosmos DB 操作，支持取消。
    /// </summary>
    /// <typeparam name="TArg">函数参数的类型。</typeparam>
    /// <typeparam name="TResult">结果值的类型。</typeparam>
    /// <param name="func">要执行的委托。</param>
    /// <param name="arg">传递给委托调用的参数。</param>
    /// <param name="cancellationToken">用于取消操作及重试等待的令牌。</param>
    /// <returns>调用委托的结果。</returns>
    /// <remarks>
    /// 默认实现检查取消状态后，委托给现有执行器。
    /// 执行器可以实现此重载以取消其重试等待。
    /// </remarks>
    Task<TResult> ExecuteOperation<TArg, TResult>(Func<TArg, Task<TResult>> func, TArg arg, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteOperation(func, arg);
    }
#endif
}

internal sealed class DefaultCosmosOperationExecutor : ICosmosOperationExecutor
{
    public static readonly DefaultCosmosOperationExecutor Instance = new();
    private const HttpStatusCode TOO_MANY_REQUESTS = (HttpStatusCode)429;
#if ORLEANS_PERSISTENCE
    public Task<TResult> ExecuteOperation<TArg, TResult>(Func<TArg, Task<TResult>> func, TArg arg)
        => ExecuteOperation(func, arg, CancellationToken.None);

    public async Task<TResult> ExecuteOperation<TArg, TResult>(Func<TArg, Task<TResult>> func, TArg arg, CancellationToken cancellationToken)
#else
    public async Task<TResult> ExecuteOperation<TArg, TResult>(Func<TArg, Task<TResult>> func, TArg arg)
#endif
    {
        // 来源：https://blogs.msdn.microsoft.com/bigdatasupport/2015/09/02/dealing-with-requestratetoolarge-errors-in-azure-documentdb-and-testing-performance/
        while (true)
        {
#if ORLEANS_PERSISTENCE
            cancellationToken.ThrowIfCancellationRequested();
#endif
            TimeSpan sleepTime;
            try
            {
                return await func(arg).ConfigureAwait(false);
            }
            catch (CosmosException dce) when (dce.StatusCode == TOO_MANY_REQUESTS)
            {
                sleepTime = dce.RetryAfter ?? TimeSpan.Zero;
            }
            catch (AggregateException ae) when (ae.InnerException is CosmosException dce && dce.StatusCode == TOO_MANY_REQUESTS)
            {
                sleepTime = dce.RetryAfter ?? TimeSpan.Zero;
            }

#if ORLEANS_PERSISTENCE
            await Task.Delay(sleepTime, cancellationToken).ConfigureAwait(false);
#else
            await Task.Delay(sleepTime);
#endif
        }
    }
}
