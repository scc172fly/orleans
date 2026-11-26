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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif

/// <summary>
/// Azure 宇宙DB储存的选项。
/// </summary>
public abstract class CosmosOptions
{
    /// <summary>
    /// 如果数据库不存在, 尝试创建数据库和用于分组的容器。 默认为 <see langword="false"/>。
    /// </summary>
    public bool IsResourceCreationEnabled { get; set; }

    /// <summary>
    /// 数据库配置传输。 如果设置为 <see langword="null"/>, 即默认值, 它将不会被配置 。 
    /// </summary>
    /// <seealso href="https://learn.microsoft.com/azure/cosmos-db/set-throughput"/>
    public int? DatabaseThroughput { get; set; }

    /// <summary>
    /// 用于分组信息的数据库名称。默认为 <c>Orleans</c>。
    /// </summary>
    public string DatabaseName { get; set; } = "Orleans";

    /// <summary>
    /// 存储集群信息所使用的容器名称。
    /// </summary>
    public string ContainerName { get; set; } = default!;

    /// <summary>
    /// 容器的传输量属性。默认值为 <see langword="null"/>,表示将使用无服务器传输模式。
    /// </summary>
    /// <seealso href="https://learn.microsoft.com/azure/cosmos-db/set-throughput"/>
    public ThroughputProperties? ContainerThroughputProperties { get; set; }

    /// <summary>
    /// 删除初始化数据库。 只用于测试情景。
    /// </summary>
    /// <remarks>仅用于试验设想方案。</remarks>
    public bool CleanResourcesOnInitialization { get; set; }

    /// <summary>
    /// 选项传递给 Cosmos DB 客户端 。
    /// </summary>
    public CosmosClientOptions ClientOptions { get; set; } = new();

    /// <summary>
    /// 使用 Cosmos DB 客户端执行操作操作执行器 。
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
    /// <param name="accountEndpoint">帐户终结点，格式为 <code>https://{databaseaccount}.documents.azure.com:443/</code>。参见 <see href="https://learn.microsoft.com/rest/api/cosmos-db/cosmosdb-resource-uri-syntax-for-rest"/>。</param>
    /// <param name="authKeyOrResourceTokenCredential"><see cref="AzureKeyCredential"/> ,带有主钥或资源符号。</param>
    /// <see cref="CosmosClient(string, AzureKeyCredential, CosmosClientOptions)"/>
    public void ConfigureCosmosClient(string accountEndpoint, AzureKeyCredential authKeyOrResourceTokenCredential)
    {
        CreateClient = _ => new(new CosmosClient(accountEndpoint, authKeyOrResourceTokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="accountEndpoint">帐户终结点，格式为 <code>https://{databaseaccount}.documents.azure.com:443/</code>。参见 <see href="https://learn.microsoft.com/rest/api/cosmos-db/cosmosdb-resource-uri-syntax-for-rest"/>。</param>
    /// <param name="tokenCredential">提供 AAD 授权的标记。</param>
    /// <see cref="CosmosClient(string, TokenCredential, CosmosClientOptions)"/>
    public void ConfigureCosmosClient(string accountEndpoint, TokenCredential tokenCredential)
    {
        CreateClient = _ => new(new CosmosClient(accountEndpoint, tokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="accountEndpoint">帐户终结点，格式为 <code>https://{databaseaccount}.documents.azure.com:443/</code>。参见 <see href="https://learn.microsoft.com/rest/api/cosmos-db/cosmosdb-resource-uri-syntax-for-rest"/>。</param>
    /// <param name="authKeyOrResourceToken">Cosmos 账户密钥或用于创建客户端的资源代号。</param>
    /// <see cref="CosmosClient(string, TokenCredential, CosmosClientOptions)"/>
    public void ConfigureCosmosClient(string accountEndpoint, string authKeyOrResourceToken)
    {
        CreateClient = _ => new(new CosmosClient(accountEndpoint, authKeyOrResourceToken, ClientOptions));
    }

    /// <summary>
    /// 配置 Cosmos DB 客户端。
    /// </summary>
    /// <param name="createClient">代表曾创建宇宙 DB 客户端。</param>
    public void ConfigureCosmosClient(Func<IServiceProvider, ValueTask<CosmosClient>> createClient)
    {
        CreateClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
    }

    /// <summary>
    /// 创建 <see cref="CosmosClient"/>的工厂方法。
    /// </summary>
    internal Func<IServiceProvider, ValueTask<CosmosClient>> CreateClient { get; private set; } = null!;
}

/// <summary>
/// 利用宇宙DB客户执行业务的职能。
/// </summary>
public interface ICosmosOperationExecutor
{
    /// <summary>
    /// 执行所提供的宇宙DB操作。
    /// </summary>
    /// <typeparam name="TArg">函数参数。</typeparam>
    /// <typeparam name="TResult">结果值。</typeparam>
    /// <param name="func">执行代表。</param>
    /// <param name="arg">将这一论点转至代表的传教。</param>
    /// <returns>援引代表职务的结果。</returns>
    Task<TResult> ExecuteOperation<TArg, TResult>(Func<TArg, Task<TResult>> func, TArg arg);

#if ORLEANS_PERSISTENCE
    /// <summary>
    /// 执行提供的 Cosmos DB 操作并取消。
    /// </summary>
    /// <typeparam name="TArg">函数参数。</typeparam>
    /// <typeparam name="TResult">结果值。</typeparam>
    /// <param name="func">执行代表。</param>
    /// <param name="arg">将这一论点转至代表的传教。</param>
    /// <param name="cancellationToken">用于取消操作和重试等待的标记。</param>
    /// <returns>援引代表职务的结果。</returns>
    /// <remarks>
    /// 检查取消后,默认执行代表给现有执行器。
    /// 执行器可以执行此超载来取消他们的重试等待 。
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
        // 发讯人: https://blogs.msdn.microsoft.com/bigdatasupport/2015/09/02/dealing-with-requestratetoolarge-errors-in-azure-documentdb-and-testing-performance/
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
