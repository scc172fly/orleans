using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;
using TestVersionGrainInterfaces;
using Xunit;

namespace Tester.HeterogeneousSilosTests.UpgradeTests
{
    /// <summary>
    /// 测试最低版本选择器策略，确保始终激活 v1 Grain。
    /// </summary>
    [TestSuite("SlowBVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("Versioning"), TestCategory("ExcludeXAML"), TestCategory("SlowBVT")]
    public class MinimumVersionTests : UpgradeTestsBase
    {
        protected override Type VersionSelectorStrategy => typeof(MinimumVersion);
        protected override Type CompatibilityStrategy => typeof(BackwardCompatible);

        [Fact]
        public Task AlwaysCreateActivationWithMinimumVersion()
        {
            // 即使部署 v2 Silo 后，也应仅激活 v1 Grain
            return Step1_StartV1Silo_Step2_StartV2Silo_Step3_StopV2Silo(step2Version: 1);
        }
    }

    /// <summary>
    /// 测试采用向后兼容性和 Grain 升级的最新版本选择器策略。
    /// </summary>
    [TestSuite("SlowBVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("Versioning"), TestCategory("ExcludeXAML"), TestCategory("SlowBVT")]
    public class LatestVersionTests : UpgradeTestsBase
    {
        protected override Type VersionSelectorStrategy => typeof(LatestVersion);
        protected override Type CompatibilityStrategy => typeof(BackwardCompatible);

        [Fact]
        public Task AlwaysCreateActivationWithLatestVersion()
        {
            // 部署 v2 后，应始终激活 v2 Grain
            return Step1_StartV1Silo_Step2_StartV2Silo_Step3_StopV2Silo(step2Version: 2);
        }

        [Fact]
        public Task UpgradeProxyCallNoPendingRequest()
        {
            // v2 -> v1 调用应触发 Grain 激活实例升级。
            // Grain 收到消息时未在处理请求
            return ProxyCallNoPendingRequest(expectedVersion: 2);
        }

        [Fact]
        public Task UpgradeProxyCallWithPendingRequest()
        {
            // v2 -> v1 调用应触发 Grain 激活实例升级
            // Grain 收到消息时已在处理一个请求
            return ProxyCallWithPendingRequest(expectedVersion: 2);
        }
    }

    /// <summary>
    /// 测试所有版本均兼容的策略，防止 Grain 自动升级。
    /// </summary>
    [TestSuite("SlowBVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("Versioning"), TestCategory("ExcludeXAML"), TestCategory("SlowBVT")]
    public class AllVersionsCompatibleTests : UpgradeTestsBase
    {
        protected override Type VersionSelectorStrategy => typeof(LatestVersion);
        protected override Type CompatibilityStrategy => typeof(AllVersionsCompatible);

        [Fact]
        public Task DoNotUpgradeProxyCallNoPendingRequest()
        {
            // v2 -> v1 调用应触发 Grain 激活实例升级，因为两者兼容（译注：原注释有误；此 AllVersionsCompatible 测试实际断言保留版本 1、不进行升级）
            // Grain 收到消息时未在处理请求
            return ProxyCallNoPendingRequest(expectedVersion: 1);
        }

        [Fact]
        public Task DoNotUpgradeProxyCallWithPendingRequest()
        {
            // v2 -> v1 调用应触发 Grain 激活实例升级，因为两者兼容（译注：原注释有误；此 AllVersionsCompatible 测试实际断言保留版本 1、不进行升级）
            // Grain 收到消息时已在处理一个请求
            return ProxyCallWithPendingRequest(expectedVersion: 1);
        }
    }

    /// <summary>
    /// 测试随机兼容版本选择策略，使激活实例分布于不同版本。
    /// </summary>
    [TestSuite("SlowBVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("Versioning"), TestCategory("ExcludeXAML"), TestCategory("SlowBVT")]
    public class RandomCompatibleVersionTests : UpgradeTestsBase
    {
        protected override Type VersionSelectorStrategy => typeof(AllCompatibleVersions);
        protected override Type CompatibilityStrategy => typeof(AllVersionsCompatible);

        [Fact]
        public async Task CreateActivationWithBothVersion()
        {
            const float numberOfGrains = 300;

            await StartSiloV1();
            await StartSiloV2();

            var versionCounter = new int[2];

            // 应创建 v1 和 v2 激活实例

            for (var i = 0; i < numberOfGrains; i++)
            {
                var v = await Client.GetGrain<IVersionUpgradeTestGrain>(i).GetVersion();
                versionCounter[v - 1]++;
            }

            // 成功概率为 99.95%
            Assert.InRange(versionCounter[0] / numberOfGrains, 0.35, 0.65);
            Assert.InRange(versionCounter[1] / numberOfGrains, 0.35, 0.65);
        }
    }
}
