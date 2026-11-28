using System.Globalization;
using System.Net;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.AzureUtils;
using Orleans.Clustering.AzureStorage;
using Orleans.Clustering.TestKit;
using Orleans.Configuration;
using Orleans.Messaging;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;
using TestExtensions;
using UnitTests;
using UnitTests.MembershipTests;
using Xunit;

namespace Tester.AzureUtils
{
    /// <summary>
    /// 使用 AzureStore 测试 Orleans 成员资格表的操作，需要访问外部 Azure 存储。
    /// 
    /// Azure Table Storage 为 Orleans 提供可扩展、高可用的成员资格表实现。
    /// 主要功能包括：
    /// - 自动分区和负载均衡
    /// - 分区内的强一致性保证
    /// - 内置冗余和灾难恢复
    /// - 与 Azure 监控和诊断集成
    /// 
    /// 这些测试验证 Azure Table Storage 上的所有成员资格操作均能正确执行，
    /// 包括并发更新、故障检测和网关发现。
    /// </summary>
    [TestCategory("Membership"), TestCategory("AzureStorage")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Membership")]
    public class AzureMembershipTableTests : MembershipTableTestsBase
    {
        public AzureMembershipTableTests(ConnectionStringFixture fixture, TestEnvironmentFixture environment) : base(fixture, environment, CreateFilters())
        {
            TestUtils.CheckForAzureStorage();
        }

        private static LoggerFilterOptions CreateFilters()
        {
            var filters = new LoggerFilterOptions();
            filters.AddFilter(typeof(Orleans.Clustering.AzureStorage.AzureTableDataManager<>).FullName, LogLevel.Trace);
            filters.AddFilter(typeof(OrleansSiloInstanceManager).FullName, LogLevel.Trace);
            filters.AddFilter("Orleans.Storage", LogLevel.Trace);
            return filters;
        }

        /// <summary>
        /// 创建基于 Azure Table Storage 的成员资格表供测试使用。
        /// 使用适合单元测试的测试默认值配置该表，包括连接字符串
        /// 和表名。
        /// </summary>
        protected override IMembershipTable CreateMembershipTable(ILogger logger)
            => CreateMembershipTable(logger, _clusterOptions);

        protected override IMembershipTable CreateMembershipTable(ILogger logger, IOptions<ClusterOptions> clusterOptions)
        {
            TestUtils.CheckForAzureStorage();
            var options = new AzureStorageClusteringOptions();
            options.ConfigureTestDefaults();
            return new AzureBasedMembershipTable(loggerFactory, Options.Create(options), clusterOptions);
        }

        // Azure Table Storage 每个响应页最多返回 1,000 个实体。
        protected override int ConformanceConcurrencyRowCount => 1001;

        protected override MembershipTableTestFixture CreateConformanceFixture()
        {
            TestUtils.CheckForAzureStorage();
            var options = new AzureStorageClusteringOptions();
            var probe = AzureStorageOperationOptionsExtensions.GetTableServiceClient().GetTableClient(options.TableName);
            return new MembershipTableTestFixture(
                GetType().Name,
                (serviceId, clusterId, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(new MembershipTableTestHandle(CreateMembershipTable(
                        loggerFactory.CreateLogger<AzureBasedMembershipTable>(),
                        Options.Create(new ClusterOptions { ServiceId = serviceId, ClusterId = clusterId }))));
                },
                async (clusterId, cancellationToken) =>
                {
                    // 该分区同时包含 Silo 实体和成员资格版本记录。
                    await foreach (var page in probe.QueryAsync<TableEntity>(
                        TableClient.CreateQueryFilter($"PartitionKey eq {clusterId}"),
                        maxPerPage: 1,
                        select: [nameof(TableEntity.PartitionKey), nameof(TableEntity.RowKey)],
                        cancellationToken: cancellationToken).AsPages())
                    {
                        if (page.Values.Count > 0)
                        {
                            return false;
                        }
                    }

                    return true;
                });
        }

        /// <summary>
        /// 创建基于 Azure 的网关列表提供程序，用于客户端连接。
        /// 此提供程序查询 Azure Table Storage，以发现可供
        /// 客户端连接的网关 Silo。
        /// </summary>
        protected override IGatewayListProvider CreateGatewayListProvider(ILogger logger)
        {
            var options = new AzureStorageGatewayOptions();
            options.ConfigureTestDefaults();
            return new AzureGatewayListProvider(loggerFactory, Options.Create(options), this._clusterOptions, this._gatewayOptions);
        }

        protected override Task<string> GetConnectionString()
        {
            TestUtils.CheckForAzureStorage();
            return Task.FromResult("not used");
        }

        [Fact, TestCategory("Functional")]
        public Task MembershipTable_Azure_Init()
            => InitializeLegacyMembershipTableAsync(TestContext.Current.CancellationToken);

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_GetGateways()
        {
            await MembershipTable_GetGateways();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_ReadAll_EmptyTable()
        {
            await MembershipTable_ReadAll_EmptyTable();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1001)]
        public async Task MembershipTable_Azure_ReadAll_UpgradesLegacyBoundaryRows(int rowCount)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var client = AzureStorageOperationOptionsExtensions.GetTableServiceClient()
                .GetTableClient(new AzureStorageClusteringOptions().TableName);
            await client.CreateIfNotExistsAsync(cancellationToken);
            var version = new SiloInstanceTableEntry
            {
                PartitionKey = clusterId,
                RowKey = SiloInstanceTableEntry.TABLE_VERSION_ROW,
                DeploymentId = clusterId,
                MembershipVersion = "7"
            };
            await client.AddEntityAsync(version, cancellationToken);
            var legacyVersion = await client.GetEntityAsync<SiloInstanceTableEntry>(
                clusterId, version.RowKey, cancellationToken: cancellationToken);
            var startTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            // 直接写入 10.2.x 格式的数据：VersionRow 和 Silo 行，均不包含两个边界标记中的任何一个。
            var rows = Enumerable.Range(0, rowCount).Select(index =>
            {
                var address = SiloAddress.New(IPAddress.Loopback, 12000 + index, 1);
                return new SiloInstanceTableEntry
                {
                    PartitionKey = clusterId,
                    RowKey = SiloInstanceTableEntry.ConstructRowKey(address),
                    DeploymentId = clusterId,
                    Address = address.Endpoint.Address.ToString(),
                    Port = address.Endpoint.Port.ToString(CultureInfo.InvariantCulture),
                    Generation = "1",
                    HostName = $"legacy-{index}",
                    Status = nameof(SiloStatus.Active),
                    StartTime = LogFormatter.PrintDate(startTime),
                    IAmAliveTime = LogFormatter.PrintDate(startTime)
                };
            }).ToArray();
            foreach (var batch in rows.Chunk(100))
            {
                await client.SubmitTransactionAsync(
                    batch.Select(row => new TableTransactionAction(TableTransactionActionType.Add, row)), cancellationToken);
            }

            var legacyEtags = new Dictionary<string, string>();
            await foreach (var row in client.QueryAsync<SiloInstanceTableEntry>(
                TableClient.CreateQueryFilter($"PartitionKey eq {clusterId}"), cancellationToken: cancellationToken))
            {
                legacyEtags.Add(row.RowKey, row.ETag.ToString());
            }

            var table = await GetLegacyMembershipTableAsync(cancellationToken);
            var result = await table.ReadAllAsync(cancellationToken);

            Assert.Equal(7, result.Version.Version);
            Assert.Equal(legacyVersion.Value.ETag.ToString(), result.Version.VersionEtag);
            Assert.Equal(rowCount, result.Members.Count);
            for (var index = 0; index < rowCount; index++)
            {
                var row = Assert.IsType<Tuple<MembershipEntry, string>>(result.TryGet(
                    SiloInstanceTableEntry.UnpackRowKey(rows[index].RowKey)));
                Assert.Equal($"legacy-{index}", row.Item1.HostName);
                Assert.Equal(SiloStatus.Active, row.Item1.Status);
                Assert.Equal(startTime, row.Item1.StartTime);
                Assert.Equal(startTime, row.Item1.IAmAliveTime);
                Assert.Equal(legacyEtags[rows[index].RowKey], row.Item2);
            }

            foreach (var rowKey in new[] { SiloInstanceTableEntry.TABLE_VERSION_ROW_MIN, SiloInstanceTableEntry.TABLE_VERSION_ROW_MAX })
            {
                var boundary = await client.GetEntityIfExistsAsync<SiloInstanceTableEntry>(
                    clusterId, rowKey, cancellationToken: cancellationToken);
                Assert.False(boundary.HasValue);
            }

            Assert.Equal(result.Version, (await table.ReadAllAsync(cancellationToken)).Version);
            var joining = new MembershipEntry
            {
                SiloAddress = SiloAddress.New(IPAddress.Loopback, 14000, 2),
                HostName = "new-silo",
                Status = SiloStatus.Joining,
                StartTime = startTime,
                IAmAliveTime = startTime
            };
            Assert.True(await table.InsertRowAsync(joining, result.Version.Next(), cancellationToken));
            var joined = await table.ReadAllAsync(cancellationToken);
            Assert.Equal(8, joined.Version.Version);
            Assert.Equal(rowCount + 1, joined.Members.Count);
            var joinedRow = Assert.IsType<Tuple<MembershipEntry, string>>(joined.TryGet(joining.SiloAddress));
            foreach (var rowKey in new[] { SiloInstanceTableEntry.TABLE_VERSION_ROW_MIN, SiloInstanceTableEntry.TABLE_VERSION_ROW_MAX })
            {
                var boundary = await client.GetEntityAsync<SiloInstanceTableEntry>(
                    clusterId, rowKey, cancellationToken: cancellationToken);
                Assert.Equal("8", boundary.Value.MembershipVersion);
                Assert.Null(boundary.Value.Status);
            }

            joining.Status = SiloStatus.Active;
            Assert.False(await table.UpdateRowAsync(joining, joinedRow.Item2, result.Version.Next(), cancellationToken));
            Assert.True(await table.UpdateRowAsync(joining, joinedRow.Item2, joined.Version.Next(), cancellationToken));
            var active = await table.ReadAllAsync(cancellationToken);
            Assert.Equal(9, active.Version.Version);
            Assert.Equal(SiloStatus.Active,
                Assert.IsType<Tuple<MembershipEntry, string>>(active.TryGet(joining.SiloAddress)).Item1.Status);
        }

        /// <summary>
        /// 测试向 Azure Table Storage 插入新的 Silo 条目。
        /// 验证该条目能以全部必需属性正确存储，
        /// 并能处理 Azure 的实体大小限制。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_InsertRow()
        {
            await MembershipTable_InsertRow();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_ReadAll_Insert_TryGet()
        {
            await MembershipTable_ReadRow_Insert_Read();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_ReadAll_Insert_ReadAll()
        {
            await MembershipTable_ReadAll_Insert_ReadAll();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_UpdateRow()
        {
            await MembershipTable_UpdateRow();
        }

        /// <summary>
        /// 测试成员资格条目的并发更新。
        /// 验证 Azure Table Storage 的乐观并发控制能够
        /// 正确处理来自多个 Silo 的同时更新。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_UpdateRowInParallel()
        {
            await MembershipTable_UpdateRowInParallel();
        }

        /// <summary>
        /// 测试使用 Azure Table Storage 的心跳机制。
        /// 验证 Silo 能够高效更新其存活时间戳，
        /// 不会发生冲突或产生过多存储操作。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_UpdateIAmAlive()
        {
            await MembershipTable_UpdateIAmAlive();
        }
    }
}
