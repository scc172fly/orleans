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
    /// 试验试验的试验 Orleans 成员人数 AzureStore - 需要外部进入 Azure 储存
    /// 
    /// Azure 表格存储提供了 Orleans成员名单执行情况,可缩放、备供查阅。
    /// 主要特征包括:
    /// - 自动分割和负负平衡
    /// - 分分区内强有力的一致性保障
    /// - 内置冗余和灾后恢复
    /// - 与 Azure 监测和诊断相结合
    /// 
    /// 这些测试核实了所有成员的操作是否正确,与 Azure 表格存储系统(Q0Q]表存储系统)一起工作;
    /// 包括同时更新、检测故障和网关发现。
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
        /// 创建用于测试的 Azure 表格存储成员表。
        /// 配置带有测试默认值的表格,包括连接字符串
        /// 和适合进行单位测试的表格名称。
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

        // Azure 表格存储返回最多每页答复1,000个实体。
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
                    // 分区包含 silo 实体和会籍版本行。
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
        /// 创建客户端连接的 Azure- 基于用户端的网关列表提供者。
        /// 此提供者查询 Azure 表格存储以发现可用
        /// 客户端可连接到的网关 silos 。
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
            // 10.2.x 种子格式直接: VersionRow 和 silo 行,没有界碑。
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
        /// 测试将新的 silo 条目插入 Azure 表格存储。
        /// 证明条目与所有要求的条目一起正确存储
        /// 并且能够处理 Azure实体规模限制。
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
        /// 测试同时更新加入条目。
        /// 证明 Azure 表存储器的乐观同货币控制
        /// 正确处理多个 silos的同步更新。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_UpdateRowInParallel()
        {
            await MembershipTable_UpdateRowInParallel();
        }

        /// <summary>
        /// 使用 Azure 表存储测试心跳机制。
        /// 证明: silos 能够有效地更新他们的生活
        /// 时间戳没有冲突或储存作业过多。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Azure_UpdateIAmAlive()
        {
            await MembershipTable_UpdateIAmAlive();
        }
    }
}
