using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TestExtensions;
using UnitTests.MembershipTests;
using Orleans.Messaging;
using Orleans.Clustering.Cosmos;
using Orleans.Runtime;
using Orleans.Clustering.TestKit;
using Orleans.Configuration;
using UnitTests;

namespace Tester.Cosmos.Clustering;

/// <summary>
/// 使用 Azure Cosmos DB 测试 Orleans 成员资格表的操作，需要访问外部 Azure Cosmos DB 帐户。
/// 
/// Azure Cosmos DB 提供全球分布式多模型数据库服务，Orleans 可使用它管理集群
/// 成员资格。
/// 
/// 这些测试验证 Cosmos DB 成员资格提供程序能否正确实现
/// 所有成员资格操作，并利用 Cosmos DB 的特有功能，例如：
/// - 支持 SQL 查询的文档存储
/// - 使用 ETag 的乐观并发控制
/// - 用于集群隔离的分区键策略
/// </summary>
[TestCategory("Membership"), TestCategory("Cosmos")]
[TestSuite("Functional")]
[TestProvider("Cosmos")]
[TestArea("Membership")]
public class CosmosMembershipTableTests : MembershipTableTestsBase
{
    private readonly ITestOutputHelper _output;
    private readonly List<CosmosClient> _legacyGatewayClients = [];

    public CosmosMembershipTableTests(ConnectionStringFixture fixture, TestEnvironmentFixture environment, ITestOutputHelper output) : base(fixture, environment, CreateFilters())
    {
        _output = output;
    }

    private static LoggerFilterOptions CreateFilters()
    {
        var filters = new LoggerFilterOptions();
        filters.AddFilter(typeof(CosmosMembershipTable).FullName, LogLevel.Trace);
        filters.AddFilter("Orleans.Storage", LogLevel.Trace);
        return filters;
    }

    /// <summary>
    /// 创建基于 Cosmos DB 的成员资格表供测试使用。
    /// 使用测试专用设置配置 Cosmos DB 客户端，
    /// 包括数据库/容器名称及一致性级别。
    /// </summary>
    protected override IMembershipTable CreateMembershipTable(ILogger logger)
        => CreateMembershipTable(logger, _clusterOptions);

    protected override IMembershipTable CreateMembershipTable(ILogger logger, IOptions<ClusterOptions> clusterOptions)
    {
        CosmosTestUtils.CheckCosmosStorage();
        return new CosmosMembershipTable(loggerFactory, Services, Options.Create(CreateClusteringOptions()), clusterOptions);
    }

    protected override MembershipTableTestHandle CreateLegacyMembershipTableHandle(ILogger logger)
    {
        CosmosTestUtils.CheckCosmosStorage();
        var clients = new List<CosmosClient>();
        var table = new CosmosMembershipTable(
            loggerFactory, Services, Options.Create(CreateOwnedClusteringOptions(clients)), _clusterOptions);
        return new MembershipTableTestHandle(table, () => DisposeClientsAsync(clients));
    }

    // SDK 默认查询页最多包含 100 个项。
    protected override int ConformanceConcurrencyRowCount => 101;

    protected override MembershipTableTestFixture CreateConformanceFixture()
    {
        CosmosTestUtils.CheckCosmosStorage();
        var probeOptions = CreateClusteringOptions();
        CosmosClient? probe = null;
        return new MembershipTableTestFixture(
            GetType().Name,
            async (serviceId, clusterId, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clients = new List<CosmosClient>();
                try
                {
                    if (probe is null)
                    {
                        probe = await probeOptions.CreateClient!(Services);
                        clients.Add(probe);
                    }

                    var options = CreateOwnedClusteringOptions(clients);
                    cancellationToken.ThrowIfCancellationRequested();
                    var table = new CosmosMembershipTable(
                        loggerFactory,
                        Services,
                        Options.Create(options),
                        Options.Create(new ClusterOptions { ServiceId = serviceId, ClusterId = clusterId }));
                    return new MembershipTableTestHandle(table, () => DisposeClientsAsync(clients));
                }
                catch
                {
                    foreach (var client in clients)
                    {
                        client.Dispose();
                    }

                    throw;
                }
            },
            async (clusterId, cancellationToken) =>
            {
                var container = probe!.GetContainer(probeOptions.DatabaseName, probeOptions.ContainerName);
                // 查询范围应包括原分区中的 ClusterVersion 和所有 Silo 文档。
                // 读取继承客户端/帐户的一致性设置，与提供程序配置一致。
                using var iterator = container.GetItemQueryIterator<string>(
                    "SELECT TOP 1 VALUE c.id FROM c",
                    requestOptions: new QueryRequestOptions
                    {
                        PartitionKey = new PartitionKey(clusterId)
                    });
                while (iterator.HasMoreResults)
                {
                    var page = await iterator.ReadNextAsync(cancellationToken);
                    if (page.Count > 0)
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    private static CosmosClusteringOptions CreateClusteringOptions()
    {
        var options = new CosmosClusteringOptions();
        options.ConfigureTestDefaults();
        options.CleanResourcesOnInitialization = false;
        return options;
    }

    private static CosmosClusteringOptions CreateOwnedClusteringOptions(List<CosmosClient> clients)
    {
        var options = CreateClusteringOptions();
        var createClient = options.CreateClient!;
        options.ConfigureCosmosClient(async services =>
        {
            var client = await createClient(services);
            clients.Add(client);
            return client;
        });
        return options;
    }

    private static ValueTask DisposeClientsAsync(List<CosmosClient> clients)
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }

        clients.Clear();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 创建基于 Cosmos DB 的网关列表提供程序。
    /// 利用 Cosmos DB 的查询功能，高效检索
    /// 可供客户端连接的网关 Silo。
    /// </summary>
    protected override IGatewayListProvider CreateGatewayListProvider(ILogger logger)
    {
        var options = CreateOwnedClusteringOptions(_legacyGatewayClients);
        return new CosmosGatewayListProvider(loggerFactory, Services, Options.Create(options), _clusterOptions, _gatewayOptions);
    }

    protected override ValueTask DisposeLegacyGatewayListProviderAsync(IGatewayListProvider gatewayListProvider)
        => DisposeClientsAsync(_legacyGatewayClients);

    protected override Task<string> GetConnectionString()
    {
        return Task.FromResult(TestDefaultConfiguration.CosmosDBAccountKey!);
    }

    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_Init()
    {
        await InitializeLegacyMembershipTableAsync(TestContext.Current.CancellationToken);
        var options = new CosmosClusteringOptions();
        options.ConfigureTestDefaults();
        using var client = await options.CreateClient(Services);
        var account = await client.ReadAccountAsync();

        Assert.Null(client.ClientOptions.ConsistencyLevel);
        _output.WriteLine("Account default consistency: {0}; client consistency override: inherited; connection mode: {1}",
            account.Consistency.DefaultConsistencyLevel, client.ClientOptions.ConnectionMode);
    }

    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_HeartbeatPreservesMembershipTokens()
    {
        var token = TestContext.Current.CancellationToken;
        var table = await GetLegacyMembershipTableAsync(token);
        var initial = await table.ReadAllAsync(token);
        var entry = new MembershipEntry
        {
            SiloAddress = SiloAddress.New(IPAddress.Loopback, 11111, 1),
            HostName = "host",
            SiloName = "silo",
            Status = SiloStatus.Active,
            StartTime = DateTime.UnixEpoch,
            IAmAliveTime = DateTime.UnixEpoch
        };
        Assert.True(await table.InsertRowAsync(entry, initial.Version.Next(), token));
        var before = await table.ReadAllAsync(token);
        var (membershipEntry, rowToken) = Assert.IsType<Tuple<MembershipEntry, string>>(before.TryGet(entry.SiloAddress));

        entry.IAmAliveTime = entry.IAmAliveTime.AddMinutes(1);
        await table.UpdateIAmAliveAsync(entry, token);
        membershipEntry.Status = SiloStatus.Dead;
        membershipEntry.AddSuspector(entry.SiloAddress, DateTime.UnixEpoch.AddMinutes(2));
        Assert.True(await table.UpdateRowAsync(membershipEntry, rowToken, before.Version.Next(), token));
        Assert.False(await table.UpdateRowAsync(membershipEntry, rowToken, before.Version.Next(), token));

        var after = await table.ReadAllAsync(token);
        var updated = after.TryGet(entry.SiloAddress)!.Item1;
        Assert.Equal(SiloStatus.Dead, updated.Status);
        Assert.Equal(membershipEntry.SuspectTimes, updated.SuspectTimes);
        Assert.Equal(before.Version.Version + 1, after.Version.Version);
    }

    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_GetGateways()
    {
        await MembershipTable_GetGateways();
    }

    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_ReadAll_EmptyTable()
    {
        await MembershipTable_ReadAll_EmptyTable();
    }

    /// <summary>
    /// 测试将 Silo 条目作为 Cosmos DB 文档插入。
    /// 验证文档创建时正确分配分区键，
    /// 并自动建立索引以提高查询效率。
    /// </summary>
    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_InsertRow()
    {
        await MembershipTable_InsertRow();
    }

    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_ReadAll_Insert_TryGet()
    {
        await MembershipTable_ReadRow_Insert_Read();
    }

    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_ReadAll_Insert_ReadAll()
    {
        await MembershipTable_ReadAll_Insert_ReadAll();
    }

    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_UpdateRow()
    {
        await MembershipTable_UpdateRow();
    }

    /// <summary>
    /// 测试使用 Cosmos DB 基于 ETag 的并发控制执行并发更新。
    /// 验证乐观并发控制可以防止
    /// 更新冲突并保证数据一致性。
    /// </summary>
    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_UpdateRowInParallel()
    {
        await MembershipTable_UpdateRowInParallel();
    }

    /// <summary>
    /// 测试 Cosmos DB 中的心跳更新。
    /// 验证能高效地局部更新文档中的存活信息，
    /// 而无需重写整个成员资格条目。
    /// </summary>
    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_UpdateIAmAlive()
    {
        await MembershipTable_UpdateIAmAlive();
    }
}
