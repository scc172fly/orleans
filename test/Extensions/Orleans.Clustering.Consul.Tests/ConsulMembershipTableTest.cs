using System.Net;
using Consul;
using Orleans.Messaging;
using Orleans.Clustering.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Membership;
using TestExtensions;
using UnitTests;
using UnitTests.MembershipTests;
using Xunit;

namespace Consul.Tests
{
    /// <summary>
    /// 使用 Consul 测试 Orleans 成员资格表的操作，需要访问外部 Consul 集群。
    /// 
    /// Consul 提供分布式键值存储，Orleans 利用它实现：
    /// - 集群成员资格管理
    /// - 服务发现
    /// - 运行状况检查和故障检测
    /// 
    /// 这些测试验证基于 Consul 的成员资格提供程序是否正确实现了
    /// 所有必需的成员资格表操作，包括：
    /// - 读取和写入 Silo 条目
    /// - 更新存活信息（I Am Alive）
    /// - 为客户端发现网关
    /// - 清理失效的 Silo 条目
    /// </summary>
    [TestCategory("Membership"), TestCategory("Consul")]
    [TestSuite("Functional")]
    [TestProvider("Consul")]
    [TestArea("Membership")]
    public class ConsulMembershipTableTest : MembershipTableTestsBase
    {
        private IConsulClient _legacyGatewayClient = null!;

        public ConsulMembershipTableTest(ConnectionStringFixture fixture, TestEnvironmentFixture environment) : base(fixture, environment, CreateFilters())
        {
        }

        private static LoggerFilterOptions CreateFilters()
        {
            var filters = new LoggerFilterOptions();
            filters.AddFilter("ConsulBasedMembershipTable", Microsoft.Extensions.Logging.LogLevel.Trace);
            filters.AddFilter("Storage", Microsoft.Extensions.Logging.LogLevel.Trace);
            return filters;
        }

        /// <summary>
        /// 创建基于 Consul 的成员资格表以供测试。
        /// 使用测试服务器地址配置 Consul 客户端，
        /// 并创建成员资格表的实现实例。
        /// </summary>
        protected override IMembershipTable CreateMembershipTable(ILogger logger)
            => CreateMembershipTable(logger, _clusterOptions);

        protected override IMembershipTable CreateMembershipTable(ILogger logger, IOptions<ClusterOptions> clusterOptions)
        {
            ConsulTestUtils.EnsureConsul();
            var options = new ConsulClusteringOptions();
            var address = new Uri(this.connectionString);

            options.ConfigureConsulClient(address);

            return new ConsulBasedMembershipTable(loggerFactory.CreateLogger<ConsulBasedMembershipTable>(), Options.Create(options), clusterOptions);
        }

        protected override MembershipTableTestHandle CreateConformanceHandle(ILogger logger, IOptions<ClusterOptions> clusterOptions)
        {
            var options = new ConsulClusteringOptions();
            options.ConfigureConsulClient(new Uri(connectionString));
            var client = options.CreateClient();
            options.ConfigureConsulClient(() => client);
            var table = new ConsulBasedMembershipTable(
                loggerFactory.CreateLogger<ConsulBasedMembershipTable>(), Options.Create(options), clusterOptions);
            return new MembershipTableTestHandle(table, () =>
            {
                client.Dispose();
                return ValueTask.CompletedTask;
            });
        }

        protected override MembershipTableTestHandle CreateLegacyMembershipTableHandle(ILogger logger)
            => CreateConformanceHandle(logger, _clusterOptions);

        protected override MembershipTableTestFixture CreateConformanceFixture()
            => CreateConformanceFixture(IsConformanceClusterDeletedAsync);

        private async ValueTask<bool> IsConformanceClusterDeletedAsync(string clusterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new ConsulClient(options => options.Address = new Uri(connectionString));
            var response = await client.KV.Keys(
                $"orleans/{clusterId}/",
                separator: null,
                new QueryOptions { Consistency = ConsistencyMode.Consistent },
                cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NotFound))
            {
                throw new InvalidOperationException($"Consul deletion probe returned {response.StatusCode}.");
            }

            return response.Response is null or { Length: 0 };
        }

        /// <summary>
        /// 创建基于 Consul 的网关列表提供程序以供测试。
        /// 此提供程序允许客户端通过查询
        /// Consul 服务注册表来发现可用网关。
        /// </summary>
        protected override IGatewayListProvider CreateGatewayListProvider(ILogger logger)
        {
            ConsulTestUtils.EnsureConsul();
            var options = new ConsulClusteringOptions();
            var address = new Uri(this.connectionString);

            options.ConfigureConsulClient(address);
            var client = options.CreateClient();
            _legacyGatewayClient = client;
            options.ConfigureConsulClient(() => client);

            return new ConsulGatewayListProvider(loggerFactory.CreateLogger<ConsulGatewayListProvider>(), Options.Create(options), this._gatewayOptions, this._clusterOptions);
        }

        protected override ValueTask DisposeLegacyGatewayListProviderAsync(IGatewayListProvider gatewayListProvider)
        {
            _legacyGatewayClient.Dispose();
            return ValueTask.CompletedTask;
        }

        protected override async Task<string> GetConnectionString()
        {
            return await ConsulTestUtils.EnsureConsulAsync(TestContext.Current.CancellationToken)
                ? ConsulTestUtils.ConsulConnectionString
                : null!;
        }

        /// <summary>
        /// 测试通过 Consul 发现网关的功能。
        /// 验证客户端可以从 Consul 服务注册表
        /// 获取可用网关 Silo 的列表。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_GetGateways()
        {
            await MembershipTable_GetGateways();
        }

        /// <summary>
        /// 测试读取空的成员资格表。
        /// 验证提供程序能够正确处理
        /// 尚无任何 Silo 注册的情况。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_ReadAll_EmptyTable()
        {
            await MembershipTable_ReadAll_EmptyTable();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_InsertRow()
        {
            await MembershipTable_InsertRow();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_ReadRow_Insert_Read()
        {
            await MembershipTable_ReadRow_Insert_Read();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_ReadAll_Insert_ReadAll()
        {
            await MembershipTable_ReadAll_Insert_ReadAll();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_UpdateRow()
        {
            await MembershipTable_UpdateRow();
        }

        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_UpdateRowInParallel()
        {
            await MembershipTable_UpdateRowInParallel();
        }

        /// <summary>
        /// 测试“I Am Alive”心跳机制。
        /// 验证 Silo 可以在 Consul 中更新其存活时间戳，
        /// 以表明其仍在运行。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_UpdateIAmAlive()
        {
            await MembershipTable_UpdateIAmAlive();
        }

        /// <summary>
        /// 测试清理已死亡的 Silo 条目。
        /// 验证成员资格表能够删除
        /// 已被判定为死亡的 Silo 的条目，
        /// 以防止该表无限增长。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_CleanupDefunctSiloEntries()
        {
            await MembershipTable_CleanupDefunctSiloEntries();
        }
    }
}
