using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;
using TestVersionGrainInterfaces;
using Xunit;

namespace Tester.HeterogeneousSilosTests.UpgradeTests
{
    /// <summary>
    /// 最小版本选择器战略的测试,确保 v1 grains 总是被激活。
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
            // 即使在部署 v2 silo 之后, 我们仍应该只激活 v1 grains
            return Step1_StartV1Silo_Step2_StartV2Silo_Step3_StopV2Silo(step2Version: 1);
        }
    }

    /// <summary>
    /// 测试最新版本选择器战略,使其与后向兼容和 grain 升级。
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
            // v2 部署后, 我们总是应该激活 v2 grains
            return Step1_StartV1Silo_Step2_StartV2Silo_Step3_StopV2Silo(step2Version: 2);
        }

        [Fact]
        public Task UpgradeProxyCallNoPendingRequest()
        {
            // v2 - > v1 呼叫应引发启动升级 grain 。
            // 接收信件时 grain 不活动
            return ProxyCallNoPendingRequest(expectedVersion: 2);
        }

        [Fact]
        public Task UpgradeProxyCallWithPendingRequest()
        {
            // v2 - > v1 调用应引发 grain 激活升级
            // grain 收到电文时已经处理请求
            return ProxyCallWithPendingRequest(expectedVersion: 2);
        }
    }

    /// <summary>
    /// 测试所有版本兼容战略的测试,以防止自动 grain 升级。
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
            // v2 - > v1 调用应引发 grain 激活升级,因为它们兼容
            // 接收信件时 grain 不活动
            return ProxyCallNoPendingRequest(expectedVersion: 1);
        }

        [Fact]
        public Task DoNotUpgradeProxyCallWithPendingRequest()
        {
            // v2 - > v1 调用应引发 grain 激活升级,因为它们兼容
            // grain 收到电文时已经处理请求
            return ProxyCallWithPendingRequest(expectedVersion: 1);
        }
    }

    /// <summary>
    /// 随机兼容版本选择策略的测试, 将激活在各种版本中进行分配 。
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

            // 我们应该创造 v1 和 v2 激活

            for (var i = 0; i < numberOfGrains; i++)
            {
                var v = await Client.GetGrain<IVersionUpgradeTestGrain>(i).GetVersion();
                versionCounter[v - 1]++;
            }

            // 99.95%成功机会
            Assert.InRange(versionCounter[0] / numberOfGrains, 0.35, 0.65);
            Assert.InRange(versionCounter[1] / numberOfGrains, 0.35, 0.65);
        }
    }
}
