using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Clustering.TestKit;
using Orleans.Messaging;
using Orleans.Runtime.Membership;
using Orleans.Configuration;
using org.apache.zookeeper;
using TestExtensions;
using Xunit;
using Tester.ZooKeeperUtils;

namespace UnitTests.MembershipTests
{
    /// <summary>
    /// 试验试验的试验 Orleans SiloInstanceManager 使用 ZookeeperStore - 需要进入外部动物园管理员仓库
    /// 
    /// Apache ZooKeeper 提供了一个用于分布式协调的等级命名空间。
    /// Orleans 使用 ZooKeeper 用于:
    /// - 使用znodes(ZooKeeper 节点)分配成员管理
    /// - 领导人选举和散发协商一致
    /// - silo 故障时自动清理的短节点
    /// - 监督成员变更通知
    /// 
    /// 这些测试核查 ZooKeeper- 以会员为基础的提供商处理所有事务
    /// 正确加入操作, 包括节点故障和网络分割 。
    /// </summary>
    [TestCategory("Membership"), TestCategory("ZooKeeper")]
    [TestSuite("Functional")]
    [TestProvider("ZooKeeper")]
    [TestArea("Membership")]
    public class ZookeeperMembershipTableTests : MembershipTableTestsBase
    {
        public ZookeeperMembershipTableTests(ConnectionStringFixture fixture, TestEnvironmentFixture environment)
            : base(fixture, environment, CreateFilters())
        {
        }

        private static LoggerFilterOptions CreateFilters()
        {
            var filters = new LoggerFilterOptions();
            filters.AddFilter(typeof(ZookeeperMembershipTableTests).Name, LogLevel.Trace);
            return filters;
        }

        /// <summary>
        /// 创建用于测试的 ZooKeeper- 基于成员表。
        /// 配置 ZooKeeper 连接并创建成员
        /// 表格使用 ZooKeeper的等级名称空间存储。
        /// </summary>
        protected override IMembershipTable CreateMembershipTable(ILogger logger)
            => CreateMembershipTable(logger, _clusterOptions);

        protected override IMembershipTable CreateMembershipTable(ILogger logger, IOptions<ClusterOptions> clusterOptions)
        {
            var options = new ZooKeeperClusteringSiloOptions();
            options.ConnectionString = this.connectionString;

            var typedLogger = this.Services.GetService<ILogger<ZooKeeperBasedMembershipTable>>();
            Assert.NotNull(typedLogger);
            return new ZooKeeperBasedMembershipTable(typedLogger, Options.Create(options), clusterOptions);
        }

        protected override MembershipTableTestFixture CreateConformanceFixture()
            => CreateConformanceFixture(IsConformanceClusterDeletedAsync);

        private async ValueTask<bool> IsConformanceClusterDeletedAsync(string clusterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ZooKeeper.Using(connectionString, 10_000, new ConformanceWatcher(), async client =>
            {
                await client.sync("/");
                cancellationToken.ThrowIfCancellationRequested();
                return await client.existsAsync("/" + clusterId, false) is null;
            });
        }

        private sealed class ConformanceWatcher : Watcher
        {
            public override Task process(WatchedEvent @event) => Task.CompletedTask;
        }

        /// <summary>
        /// 创建基于 ZooKeeper- 的网关列表提供者。
        /// 此提供者使用 ZooKeeper 手表来保持更新
        /// 列表中列出客户端连接的现有网关。
        /// </summary>
        protected override IGatewayListProvider CreateGatewayListProvider(ILogger logger)
        {
            var options = new ZooKeeperGatewayListProviderOptions();
            options.ConnectionString = this.connectionString;

            return ActivatorUtilities.CreateInstance<ZooKeeperGatewayListProvider>(this.Services, Options.Create(options), this._clusterOptions);
        }

        protected override async Task<string> GetConnectionString()
        {
            bool isReachable = await ZookeeperTestUtils.EnsureZooKeeperAsync(TestContext.Current.CancellationToken);
            return isReachable ? TestDefaultConfiguration.ZooKeeperConnectionString! : null!;
        }

        [Fact]
        public Task MembershipTable_ZooKeeper_Init()
            => InitializeLegacyMembershipTableAsync(TestContext.Current.CancellationToken);

        [Fact]
        public async Task MembershipTable_ZooKeeper_GetGateways()
        {
            await MembershipTable_GetGateways();
        }

        [Fact]
        public async Task MembershipTable_ZooKeeper_ReadAll_EmptyTable()
        {
            await MembershipTable_ReadAll_EmptyTable();
        }

        /// <summary>
        /// 测试将 silo 条目插入为 ZooKeeper znode。
        /// 证明成员数据已正确按序号排列
        /// 并储存在 ZooKeeper的等级结构中。
        /// </summary>
        [Fact]
        public async Task MembershipTable_ZooKeeper_InsertRow()
        {
            await MembershipTable_InsertRow();
        }

        [Fact]
        public async Task MembershipTable_ZooKeeper_ReadRow_Insert_Read()
        {
            await MembershipTable_ReadRow_Insert_Read();
        }

        [Fact]
        public async Task MembershipTable_ZooKeeper_ReadAll_Insert_ReadAll()
        {
            await MembershipTable_ReadAll_Insert_ReadAll();
        }

        [Fact]
        public async Task MembershipTable_ZooKeeper_UpdateRow()
        {
            await MembershipTable_UpdateRow();
        }

        /// <summary>
        /// 使用 ZooKeeper的版本测试同时更新。
        /// 证明 ZooKeeper的乐观同货币控制
        /// 正确处理多个 silos的同步更新。
        /// </summary>
        [Fact]
        public async Task MembershipTable_ZooKeeper_UpdateRowInParallel()
        {
            await MembershipTable_UpdateRowInParallel();
        }

        /// <summary>
        /// 使用 ZooKeeper测试心跳更新。
        /// 证明短节点和会话超时
        /// 正确检测失败 silos。
        /// </summary>
        [Fact]
        public async Task MembershipTable_ZooKeeper_UpdateIAmAlive()
        {
            await MembershipTable_UpdateIAmAlive();
        }
    }
}
