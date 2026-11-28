using Orleans.Metadata;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;
using TestVersionGrainInterfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tester.HeterogeneousSilosTests.UpgradeTests
{
    /// <summary>
    /// 测试运行时版本控制策略变更，包括兼容性策略和选择器策略修改。
    /// </summary>
    [TestSuite("SlowBVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("Versioning"), TestCategory("ExcludeXAML"), TestCategory("SlowBVT")]
    public class RuntimeStrategyChangeTests : UpgradeTestsBase
    {
        protected override Type VersionSelectorStrategy => typeof(LatestVersion);
        protected override Type CompatibilityStrategy => typeof(AllVersionsCompatible);

        [Fact]
        public async Task ChangeCompatibilityStrategy()
        {
            await StartSiloV1();
            var resolver = this.Client.ServiceProvider.GetService<GrainInterfaceTypeResolver>()!;
            var ifaceId = resolver.GetGrainInterfaceType(typeof(IVersionUpgradeTestGrain));

            var grainV1 = Client.GetGrain<IVersionUpgradeTestGrain>(0);
            Assert.Equal(1, await grainV1.GetVersion());

            await StartSiloV2();

            var grainV2 = Client.GetGrain<IVersionUpgradeTestGrain>(1);
            Assert.Equal(2, await grainV2.GetVersion());

            // 当前策略 "AllVersionsCompatible" -> 不降级
            Assert.Equal(2, await grainV1.ProxyGetVersion(grainV2));
            Assert.Equal(2, await grainV2.GetVersion());
            Assert.Equal(1, await grainV1.GetVersion());

            await ManagementGrain.SetCompatibilityStrategy(
                ifaceId,
                StrictVersionCompatible.Singleton,
                TestContext.Current.CancellationToken);

            // 当前策略 "StrictVersionCompatible" -> 必须降级
            Assert.Equal(1, await grainV1.ProxyGetVersion(grainV2));
            Assert.Equal(1, await grainV2.GetVersion());
            Assert.Equal(1, await grainV1.GetVersion());

            // 由于此客户端为 V1，即使采用 "LatestVersion" 规则，也应仅激活 V1
            for (var i = 2; i < 102; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(1, await grain.GetVersion());
            }

            // 回退至 AllVersionsCompatible
            await ManagementGrain.SetCompatibilityStrategy(
                ifaceId,
                null!,
                TestContext.Current.CancellationToken);

            // 现在应该仅激活 v2
            for (var i = 102; i < 202; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(2, await grain.GetVersion());
            }
        }

        [Fact]
        public async Task ChangeVersionSelectorStrategy()
        {
            await StartSiloV1();
            var resolver = this.Client.ServiceProvider.GetService<GrainInterfaceTypeResolver>()!;
            var ifaceId = resolver.GetGrainInterfaceType(typeof(IVersionUpgradeTestGrain));

            // 仅存在 V1
            var grainV1 = Client.GetGrain<IVersionUpgradeTestGrain>(0);
            Assert.Equal(1, await grainV1.GetVersion());

            await StartSiloV2();

            // 不要影响 V1
            Assert.Equal(1, await grainV1.GetVersion());
            // 但仅激活 V2
            for (int i = 1; i < 101; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(2, await grain.GetVersion());
            }

            await ManagementGrain.SetSelectorStrategy(
                ifaceId,
                MinimumVersion.Singleton,
                TestContext.Current.CancellationToken);

            // 不要影响现有激活实例
            Assert.Equal(1, await grainV1.GetVersion());
            for (int i = 1; i < 101; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(2, await grain.GetVersion());
            }
            // 但仅激活 V1
            for (int i = 101; i < 201; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(1, await grain.GetVersion());
            }
        }

        [Fact]
        public async Task ChangeDefaultVersionCompatibilityStrategy()
        {
            Assert.Equal(typeof(AllVersionsCompatible), CompatibilityStrategy);

            await StartSiloV1();

            // 仅存在 V1
            var grainV1 = new IVersionUpgradeTestGrain[2];
            grainV1[0] = Client.GetGrain<IVersionUpgradeTestGrain>(0);
            grainV1[1] = Client.GetGrain<IVersionUpgradeTestGrain>(1);
            Assert.Equal(1, await grainV1[0].GetVersion());
            Assert.Equal(1, await grainV1[1].GetVersion());

            // 将默认策略改为向后兼容
            await ManagementGrain.SetCompatibilityStrategy(
                BackwardCompatible.Singleton,
                TestContext.Current.CancellationToken);

            await StartSiloV2();

            var grainV2 = Client.GetGrain<IVersionUpgradeTestGrain>(2);
            Assert.Equal(2, await grainV2.GetVersion());

            // 应该触发“升级”
            Assert.Equal(2, await grainV2.ProxyGetVersion(grainV1[0]));
            Assert.Equal(2, await grainV1[0].GetVersion());

            // 将默认策略改为向后兼容
            await ManagementGrain.SetCompatibilityStrategy(null!, TestContext.Current.CancellationToken);

            // 不应触发升级
            Assert.Equal(1, await grainV2.ProxyGetVersion(grainV1[1]));
            Assert.Equal(1, await grainV1[1].GetVersion());
        }

        [Fact]
        public async Task ChangeDefaultVersionSelectorStrategy()
        {
            Assert.Equal(typeof(LatestVersion), VersionSelectorStrategy);

            await StartSiloV1();

            // 仅存在 V1
            var grainV1 = Client.GetGrain<IVersionUpgradeTestGrain>(0);
            Assert.Equal(1, await grainV1.GetVersion());

            await StartSiloV2();

            // 将默认策略改为最低版本
            await ManagementGrain.SetSelectorStrategy(
                MinimumVersion.Singleton,
                TestContext.Current.CancellationToken);

            // 但仅激活 V1
            for (int i = 0; i < 100; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(1, await grain.GetVersion());
            }

            // 将默认策略改为最新版本
            await ManagementGrain.SetSelectorStrategy(null!, TestContext.Current.CancellationToken);

            // 不要影响现有激活实例
            for (int i = 0; i < 100; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(1, await grain.GetVersion());
            }
            // 但仅激活 V2
            for (int i = 100; i < 200; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(2, await grain.GetVersion());
            }
        }
    }
}
