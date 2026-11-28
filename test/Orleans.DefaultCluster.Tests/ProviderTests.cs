using Xunit;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// Orleans 提供程序及 Grain 扩展测试。
    /// 提供程序是扩展 Grain 功能的可插拔组件，
    /// 而 Grain 扩展允许 Grain 公开可以动态安装的附加接口。
    /// 这使 Grain 可以采用模块化设计，
    /// 按需添加可选功能。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ProviderTests : OrleansTestingBase, IClassFixture<ProviderTests.Fixture>
    {
        private readonly Fixture fixture;

        public ProviderTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.AddSiloBuilderConfigurator<Configurator>();
            }

            private class Configurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder.AddGrainExtension<IAutoExtension, AutoExtension>();
                }
            }
        }

        /// <summary>
        /// 测试 Grain 扩展的安装及调用。
        /// 验证以下情况：
        /// - 调用未安装的扩展会引发 GrainExtensionNotInstalledException
        /// - 扩展可以动态安装到 Grain 上
        /// - 已安装的扩展可以通过其接口调用
        /// 这展示了 Grain 扩展的动态特性。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Providers")]
        public async Task Providers_TestExtensions()
        {
            IExtensionTestGrain grain = this.fixture.GrainFactory.GetGrain<IExtensionTestGrain>(GetRandomGrainId());
            ITestExtension extension = grain.AsReference<ITestExtension>();
            bool exceptionThrown = true;

            try
            {
                var p1 = extension.CheckExtension_1();
                await p1;
                exceptionThrown = false;
            }
            catch (GrainExtensionNotInstalledException)
            {
                // 这是预期行为
            }
            catch (AggregateException ex)
            {
                var baseEx = ex.GetBaseException();
                if (baseEx is GrainExtensionNotInstalledException)
                {
                    // 这也是预期行为
                }
                else
                {
                    Assert.Fail("Incorrect exception thrown when an unimplemented method is invoked: " + baseEx);
                }
            }
            catch (Exception ex1)
            {
                Assert.Fail("Incorrect exception thrown when an unimplemented method is invoked: " + ex1);
            }

            if (!exceptionThrown)
            {
                Assert.Fail("Expected exception not thrown when no extension configured");
            }

            var p2 = grain.InstallExtension("test");
            await p2;

            try
            {
                var p1 = extension.CheckExtension_1();
                await p1;
                Assert.Equal("test", await p1);
            }
            catch (Exception exc)
            {
                Assert.Fail("Unexpected exception thrown when extension is configured. Exc = " + exc);
            }

            try
            {
                var p1 = extension.CheckExtension_2();
                await p1;
                Assert.Equal("23", await p1);
            }
            catch (Exception exc)
            {
                Assert.Fail("Unexpected exception thrown when extension is configured. Exc = " + exc);
            }
        }

        /// <summary>
        /// 测试通过泛型 Grain 接口上的非泛型扩展进行激活。
        /// 验证当泛型 Grain 具有非泛型扩展时，
        /// 可以通过扩展引用顺利激活该 Grain，
        /// 展示扩展与泛型 Grain 的兼容性。
        /// </summary>
        [Fact, TestCategory("Providers"), TestCategory("BVT"), TestCategory("Cast"), TestCategory("Generics")]
        public async Task Providers_ActivateNonGenericExtensionOfGenericInterface()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGenericGrainWithNonGenericExtension<int>>(GetRandomGrainId());
            var extension = grain.AsReference<ISimpleExtension>(); //泛型基础 Grain 尚未激活，仅有虚拟引用

            try
            {
                var res = await extension.CheckExtension_1(); //现在发生激活，但通过非泛型引用进行
            }
            catch (Exception ex)
            {
                Assert.Fail("No exception should have been thrown. Ex: " + ex.Message);
            }

            Assert.True(true);
        }

        /// <summary>
        /// 测试泛型 Grain 激活后引用非泛型扩展。
        /// 验证泛型 Grain 正常激活后，
        /// 仍可以获取并调用其非泛型扩展，
        /// 确保扩展与已激活的泛型 Grain 正常协作。
        /// </summary>
        [Fact, TestCategory("Providers"), TestCategory("BVT"), TestCategory("Cast"), TestCategory("Generics")]
        public async Task Providers_ReferenceNonGenericExtensionOfGenericInterface()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGenericGrainWithNonGenericExtension<int>>(GetRandomGrainId());
            await grain.DoSomething(); //原始泛型 Grain 在此处激活

            var extension = grain.AsReference<ISimpleExtension>();
            try
            {
                var res = await extension.CheckExtension_1();
            }
            catch (Exception ex)
            {
                Assert.Fail("No exception should have been thrown. Ex: " + ex.Message);
            }

            Assert.True(true);
        }

        /// <summary>
        /// 测试自动安装 Grain 扩展。
        /// 验证使用 AddGrainExtension 配置的扩展
        /// 无需手动安装即可在 Grain 上自动使用，
        /// 而未配置的扩展仍需显式安装。
        /// 这展示了常用扩展的自动装配能力。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Providers")]
        public async Task Providers_AutoInstallExtensionTest()
        {
            INoOpTestGrain grain = this.fixture.GrainFactory.GetGrain<INoOpTestGrain>(GetRandomGrainId());
            ISimpleExtension uninstalled = grain.AsReference<ISimpleExtension>();
            IAutoExtension autoInstalled = grain.AsReference<IAutoExtension>();
            await Assert.ThrowsAsync<GrainExtensionNotInstalledException>(() => uninstalled.CheckExtension_1());
            await autoInstalled.CheckExtension();
        }
    }
}
