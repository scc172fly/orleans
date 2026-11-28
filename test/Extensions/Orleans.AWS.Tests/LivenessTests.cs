using AWSUtils.Tests.StorageTests;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using UnitTests.MembershipTests;
using Xunit;

namespace AWSUtils.Tests.Liveness
{
    /// <summary>
    /// AWS DynamoDB 成员资格提供程序的存活性测试。
    /// 
    /// 这些测试验证 Orleans 集群成员资格管理在
    /// 使用 DynamoDB 作为成员资格表提供程序时能否正常工作。DynamoDB 提供：
    /// - 跨 Silo 的分布式成员资格跟踪
    /// - 故障检测与恢复
    /// - 一致的集群拓扑视图
    /// 
    /// 测试模拟多种故障场景，确保集群
    /// 保持一致性并正确恢复。
    /// </summary>
    [TestCategory("Membership"), TestCategory("AWS"), TestCategory("DynamoDb")]
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
    [TestArea("Membership")]
    public class LivenessTests_DynamoDB : LivenessTestsBase
    {
        public LivenessTests_DynamoDB(ITestOutputHelper output) : base(output)
        {
            if (!AWSTestConstants.IsDynamoDbAvailable)
                throw Xunit.Sdk.SkipException.ForSkip("Unable to connect to DynamoDB simulator");
        }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.AddSiloBuilderConfigurator<SiloBuilderConfigurator>();
            builder.AddClientBuilderConfigurator<ClientBuilderConfigurator>();
        }

        /// <summary>
        /// 配置 Silo 使用 DynamoDB 管理集群成员资格。
        /// 设置用于测试的 DynamoDB 服务端点（通常为本地模拟器）。
        /// </summary>
        public class SiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.UseDynamoDBClustering(options => { options.Service = AWSTestConstants.DynamoDbService; });
            }
        }

        /// <summary>
        /// 配置客户端使用 DynamoDB 发现集群网关。
        /// 确保客户端能够利用相同的
        /// 基于 DynamoDB 的成员资格信息定位并连接 Silo。
        /// </summary>
        public class ClientBuilderConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder.UseDynamoDBClustering(ob => ob.Configure(gatewayOptions =>
                {
                    gatewayOptions.Service = AWSTestConstants.DynamoDbService;
                }));
            }
        }

        /// <summary>
        /// 验证集群成员资格操作的基本存活性测试。
        /// 测试 Silo 能否加入集群、被其他 Silo 发现，
        /// 并在 DynamoDB 中维护准确的成员资格信息。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_1()
        {
            await Do_Liveness_OracleTest_1();
        }

        /// <summary>
        /// 测试主 Silo 重启时的集群恢复。
        /// 验证集群能否在不丢失成员资格一致性的情况下处理
        /// 主 Silo 的丢失及恢复。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_2_Restart_Primary()
        {
            await Do_Liveness_OracleTest_2(0);
        }

        /// <summary>
        /// 测试网关 Silo 重启时的集群恢复。
        /// 验证客户端连接能否在当前连接的网关发生故障时恢复，
        /// 并找到其他可用网关。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_3_Restart_GW()
        {
            await Do_Liveness_OracleTest_2(1);
        }

        /// <summary>
        /// 测试非主 Silo 重启时的集群恢复。
        /// 验证 Grain 激活实例能否正确迁移，且
        /// 集群在 Silo 发生故障期间能否维持运行。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_4_Restart_Silo_1()
        {
            await Do_Liveness_OracleTest_2(2);
        }

        /// <summary>
        /// 测试具有活动定时器的 Silo 被终止时的集群恢复。
        /// 验证在故障后 Grain 在其他 Silo 上重新激活时，
        /// 定时器注册能否正确恢复。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_5_Kill_Silo_1_With_Timers()
        {
            await Do_Liveness_OracleTest_2(2, false, true);
        }
    }
}
