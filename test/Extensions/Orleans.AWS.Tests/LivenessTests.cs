using AWSUtils.Tests.StorageTests;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using UnitTests.MembershipTests;
using Xunit;

namespace AWSUtils.Tests.Liveness
{
    /// <summary>
    /// AWS DynamoDB 成员供应商的活度测试。
    /// 
    /// 这些测试核实 Orleans 群组成员管理是否正确
    /// 当使用 DynamoDB 作为成员表提供者时。 DynamoDB 规定:
    /// - silos之间分配成员跟踪
    /// - 故障检测和复原
    /// - 一致的群集地形学观点
    /// 
    /// 测试模拟各种故障假设情景,以确保集群
    /// 保持一致性,并妥善收回。
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
        /// 配置配置 silos 使用 DynamoDB 分组成员。
        /// 设置 DynamoDB 服务端点用于测试(通常是本地模拟器)。
        /// </summary>
        public class SiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.UseDynamoDBClustering(options => { options.Service = AWSTestConstants.DynamoDbService; });
            }
        }

        /// <summary>
        /// 配置客户端, 以使用 DynamoDB 来发现集束网关 。
        /// 确保客户能够找到并连接到 silos 使用相同
        /// DynamoDB- 以成员信息为基础的成员信息。
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
        /// 基本生存测试,核查分组成员业务。
        /// 测试 silos 能够加入该组群,由其他 silos发现,
        /// 并保持 DynamoDB的准确成员信息。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_1()
        {
            await Do_Liveness_OracleTest_1();
        }

        /// <summary>
        /// 重新启动主 silo 时测试组群恢复 。
        /// 证明该组群能够处理损失和追回
        /// 基本 silo ,但不会失去成员一致性。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_2_Restart_Primary()
        {
            await Do_Liveness_OracleTest_2(0);
        }

        /// <summary>
        /// 当网关 silo 重新启动时测试组群恢复 。
        /// 证明客户联系能够恢复并找到替代
        /// 当连接的网关失败时, 将打开网关 。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_3_Restart_GW()
        {
            await Do_Liveness_OracleTest_2(1);
        }

        /// <summary>
        /// 当非主 silo 重新启动时测试组群恢复。
        /// 证明 grain 启动程序已适当迁移和
        /// 组群在 silo 故障时保持运行。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_4_Restart_Silo_1()
        {
            await Do_Liveness_OracleTest_2(2);
        }

        /// <summary>
        /// 当有活动计时器的 silo 被杀死时测试集集恢复 。
        /// 证明在下列情况下适当收回计时器登记时,即
        /// grains 在失败后在其他 silos 上重新启动。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task Liveness_AWS_DynamoDB_5_Kill_Silo_1_With_Timers()
        {
            await Do_Liveness_OracleTest_2(2, false, true);
        }
    }
}
