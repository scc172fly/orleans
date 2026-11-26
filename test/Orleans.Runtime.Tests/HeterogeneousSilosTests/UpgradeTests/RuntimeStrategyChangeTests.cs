using Orleans.Metadata;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;
using TestVersionGrainInterfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tester.HeterogeneousSilosTests.UpgradeTests
{
    /// <summary>
    /// 测试对版本战略的运行时间变化,包括兼容性和选择策略修改。
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

            // 当前政策 "AllVersionsCompatible" - > 不降级
            Assert.Equal(2, await grainV1.ProxyGetVersion(grainV2));
            Assert.Equal(2, await grainV2.GetVersion());
            Assert.Equal(1, await grainV1.GetVersion());

            await ManagementGrain.SetCompatibilityStrategy(
                ifaceId,
                StrictVersionCompatible.Singleton,
                TestContext.Current.CancellationToken);

            // 现行政策 "StrictVersionCompatible" - > 强制性降级
            Assert.Equal(1, await grainV1.ProxyGetVersion(grainV2));
            Assert.Equal(1, await grainV2.GetVersion());
            Assert.Equal(1, await grainV1.GetVersion());

            // 由于客户端是V1, 即使有 "LatestVersion规则 " , 但仅应激活V1
            for (var i = 2; i < 102; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(1, await grain.GetVersion());
            }

            // 返回到 AllVersionsCompatible
            await ManagementGrain.SetCompatibilityStrategy(
                ifaceId,
                null!,
                TestContext.Current.CancellationToken);

            // 现在我们只应该激活 v2
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

            // 只有V1存在
            var grainV1 = Client.GetGrain<IVersionUpgradeTestGrain>(0);
            Assert.Equal(1, await grainV1.GetVersion());

            await StartSiloV2();

            // 别碰V1
            Assert.Equal(1, await grainV1.GetVersion());
            // 但只激活 V2
            for (int i = 1; i < 101; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(2, await grain.GetVersion());
            }

            await ManagementGrain.SetSelectorStrategy(
                ifaceId,
                MinimumVersion.Singleton,
                TestContext.Current.CancellationToken);

            // 别碰现有的激活
            Assert.Equal(1, await grainV1.GetVersion());
            for (int i = 1; i < 101; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(2, await grain.GetVersion());
            }
            // 但只激活 V1
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

            // 只有V1存在
            var grainV1 = new IVersionUpgradeTestGrain[2];
            grainV1[0] = Client.GetGrain<IVersionUpgradeTestGrain>(0);
            grainV1[1] = Client.GetGrain<IVersionUpgradeTestGrain>(1);
            Assert.Equal(1, await grainV1[0].GetVersion());
            Assert.Equal(1, await grainV1[1].GetVersion());

            // 将默认更改为后向兼容
            await ManagementGrain.SetCompatibilityStrategy(
                BackwardCompatible.Singleton,
                TestContext.Current.CancellationToken);

            await StartSiloV2();

            var grainV2 = Client.GetGrain<IVersionUpgradeTestGrain>(2);
            Assert.Equal(2, await grainV2.GetVersion());

            // 应该是引起"升级"
            Assert.Equal(2, await grainV2.ProxyGetVersion(grainV1[0]));
            Assert.Equal(2, await grainV1[0].GetVersion());

            // 将默认更改为后向兼容
            await ManagementGrain.SetCompatibilityStrategy(null!, TestContext.Current.CancellationToken);

            // 不应引起升级
            Assert.Equal(1, await grainV2.ProxyGetVersion(grainV1[1]));
            Assert.Equal(1, await grainV1[1].GetVersion());
        }

        [Fact]
        public async Task ChangeDefaultVersionSelectorStrategy()
        {
            Assert.Equal(typeof(LatestVersion), VersionSelectorStrategy);

            await StartSiloV1();

            // 只有V1存在
            var grainV1 = Client.GetGrain<IVersionUpgradeTestGrain>(0);
            Assert.Equal(1, await grainV1.GetVersion());

            await StartSiloV2();

            // 将默认值更改为最小版本
            await ManagementGrain.SetSelectorStrategy(
                MinimumVersion.Singleton,
                TestContext.Current.CancellationToken);

            // 但只激活 V1
            for (int i = 0; i < 100; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(1, await grain.GetVersion());
            }

            // 将默认值更改为最新版本
            await ManagementGrain.SetSelectorStrategy(null!, TestContext.Current.CancellationToken);

            // 别碰现有的激活
            for (int i = 0; i < 100; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(1, await grain.GetVersion());
            }
            // 但只激活 V2
            for (int i = 100; i < 200; i++)
            {
                var grain = Client.GetGrain<IVersionUpgradeTestGrain>(i);
                Assert.Equal(2, await grain.GetVersion());
            }
        }
    }
}
