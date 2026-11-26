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
/// 试验试验的试验 Orleans 成员人数 Azure 宇宙 DB - 需要外部访问 Azure 宇宙 DB 账户
/// 
/// Azure 宇宙DB提供全球分布的多模式数据库服务, Orleans 可用于分组成员
/// 管理。
/// 
/// 这些测试验证宇宙DB成员供应商是否正确执行
/// 具有宇宙DB独特特征的所有会员业务,例如:
/// - 储存文件,并有 SQL 查询
/// - 使用ETags的乐观同价货币
/// - 隔离集群隔离的关键战略
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
    /// 创建基于宇宙DB的成员表供测试。
    /// 配置带有测试特定设置的 Cosmos DB 客户端
    /// 包括数据库/集装箱名称和一致性水平。
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

    // SDK 默认查询页面最多包含100项。
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
                // 在原始分区中包含 ClusterVersion 和所有 silo 文档。
                // 阅读继承客户/账户的一致性,与提供者配置相符。
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
    /// 创建 Cosmos DB 网关列表提供者。
    /// 有效使用宇宙DB的查询能力
    /// 获取客户端连接的可用网关 silos 。
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
    /// 测试插入 silo 条目作为宇宙 DB 文档。
    /// 验证文档创建并配有适当的分区密钥指定
    /// 和自动索引化的有效查询。
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
    /// 使用宇宙DB的ETag计算法测试同时更新。
    /// 证明乐观货币管制防止
    /// 更新时相互矛盾,确保数据的一致性。
    /// </summary>
    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_UpdateRowInParallel()
    {
        await MembershipTable_UpdateRowInParallel();
    }

    /// <summary>
    /// 测试心跳更新 在宇宙DB。
    /// 有效核证部分文件的活性更新
    /// 信息,不重写全部会员条目。
    /// </summary>
    [Fact, TestCategory("Functional")]
    public async Task MembershipTable_Cosmos_UpdateIAmAlive()
    {
        await MembershipTable_UpdateIAmAlive();
    }
}
