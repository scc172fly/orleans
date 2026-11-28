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
    /// 测试使用 ZookeeperStore 的 Orleans SiloInstanceManager 操作，需要访问外部 ZooKeeper 存储。
    /// 
    /// Apache ZooKeeper 为分布式协调提供层次化命名空间。
    /// Orleans 使用 ZooKeeper 实现：
    /// - 使用 znode（ZooKeeper 节点）进行分布式成员资格管理
    /// - 领导者选举和分布式共识
    /// - 在 Silo 故障时利用临时节点自动清理
    /// - 成员资格变更的监听通知
    /// 
    /// 这些测试验证基于 ZooKeeper 的成员资格提供程序能正确处理所有
    /// 成员资格操作，包括节点故障和网络分区。
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
        /// 创建用于测试的 ZooKeeper 成员资格表。
        /// 配置 ZooKeeper 连接，并创建使用 ZooKeeper
        /// 层次化命名空间进行存储的成员资格表。
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
        /// 创建基于 ZooKeeper 的网关列表提供程序。
        /// 该提供程序使用 ZooKeeper 监听机制，维护供客户端连接使用的
        /// 最新可用网关列表。
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
        /// 测试将 Silo 条目作为 ZooKeeper znode 插入。
        /// 验证成员资格数据被正确序列化，
        /// 并存储在 ZooKeeper 的层次化结构中。
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
        /// 测试利用 ZooKeeper 版本控制进行并发更新。
        /// 验证 ZooKeeper 的乐观并发控制能
        /// 正确处理多个 Silo 同时进行的更新。
        /// </summary>
        [Fact]
        public async Task MembershipTable_ZooKeeper_UpdateRowInParallel()
        {
            await MembershipTable_UpdateRowInParallel();
        }

        /// <summary>
        /// 测试使用 ZooKeeper 更新心跳。
        /// 验证临时节点和会话超时机制能
        /// 正确检测发生故障的 Silo。
        /// </summary>
        [Fact]
        public async Task MembershipTable_ZooKeeper_UpdateIAmAlive()
        {
            await MembershipTable_UpdateIAmAlive();
        }
    }
}
