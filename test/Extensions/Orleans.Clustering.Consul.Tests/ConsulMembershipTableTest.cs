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
    /// 使用领事的 Orleans 成员表运行测试----需要访问外部领事群
    /// 
    /// 领事提供分布式钥匙价值商店 Orleans 用于:
    /// - 群组成员管理
    /// - 服务发现
    /// - 检查健康和检查故障
    /// 
    /// 这些测试核实,驻领事机构成员提供商是否正确地执行了这些测试
    /// 需要的所有成员名单业务包括:
    /// - 读写 silo 条目
    /// - 更新生命信息(我活着)
    /// - 为客户发现网关
    /// - 清理已关闭的条目 silo
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
        /// 创建以领事为基础的成员表,供测试。
        /// 配置Conser Consul 客户端,使用测试服务器地址
        /// 并创建成员表执行情况。
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
        /// 创建一个基于领事的网关列表提供者,供测试。
        /// 此服务提供商允许客户发现可用的网关
        /// 查询领事服务登记处。
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
        /// 通过领事测试网关发现
        /// 证明客户可以检索现有清单
        /// 领事服务登记处的网关 silos 。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_GetGateways()
        {
            await MembershipTable_GetGateways();
        }

        /// <summary>
        /// 测试空席位表的读取。
        /// 证明提供者处理案件正确
        /// 时无 silos 登记者尚未登记。
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
        /// 测试"我活着"的心跳机制
        /// 证明 silos 能够更新其活性时间戳
        /// 以表示他们仍在运行。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_UpdateIAmAlive()
        {
            await MembershipTable_UpdateIAmAlive();
        }

        /// <summary>
        /// 测试已死亡的条目的清理 silo 。
        /// 证明成员表可以删除条目
        /// 用于 silos 被宣布为已死亡以防止
        /// 桌子从无限长长。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task MembershipTable_Consul_CleanupDefunctSiloEntries()
        {
            await MembershipTable_CleanupDefunctSiloEntries();
        }
    }
}
