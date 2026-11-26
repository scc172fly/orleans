using Xunit;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// Orleans 提供者和 grain 扩展的测试。
    /// 供应商是扩展 grain 功能的可插入组件,
    /// 扩展 grain 允许 grains 暴露其他接口
    /// 能够动态安装。这可以进行模块 grain 设计
    /// 可根据需要添加任择功能。
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
        /// 测试 grain 扩展安装和调用。
        /// 证明:
        /// - 调用未安装的扩展投出 GrainExtensionNotInstalledException
        /// - 扩展可动态安装在 grains
        /// - 可通过接口引用安装的扩展名
        /// 这显示了 grain 延期的动态性质。
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
                // 这是预期的行为
            }
            catch (AggregateException ex)
            {
                var baseEx = ex.GetBaseException();
                if (baseEx is GrainExtensionNotInstalledException)
                {
                    // 这也是预期的行为
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
        /// 测试在通用 grain 界面上激活非遗传扩展。
        /// 证明当通用 grain 具有非通用扩展,
        /// grain 可通过扩展参比激活,而无需
        /// 问题,证明扩展与通用 grains的兼容性。
        /// </summary>
        [Fact, TestCategory("Providers"), TestCategory("BVT"), TestCategory("Cast"), TestCategory("Generics")]
        public async Task Providers_ActivateNonGenericExtensionOfGenericInterface()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGenericGrainWithNonGenericExtension<int>>(GetRandomGrainId());
            var extension = grain.AsReference<ISimpleExtension>(); //通用基底 grain 尚未激活 - 仅 virt refs

            try
            {
                var res = await extension.CheckExtension_1(); //现发生激活,但有非遗传参考
            }
            catch (Exception ex)
            {
                Assert.Fail("No exception should have been thrown. Ex: " + ex.Message);
            }

            Assert.True(true);
        }

        /// <summary>
        /// 用于参考通用 grain 激活后非遗传扩展的测试。
        /// 证明在一般 grain 正常激活后,
        /// 其非基因延伸仍可被获取和援引,
        /// 确保已激活的通用 grains扩展工作正确。
        /// </summary>
        [Fact, TestCategory("Providers"), TestCategory("BVT"), TestCategory("Cast"), TestCategory("Generics")]
        public async Task Providers_ReferenceNonGenericExtensionOfGenericInterface()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGenericGrainWithNonGenericExtension<int>>(GetRandomGrainId());
            await grain.DoSomething(); //原通用 grain 在此激活

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
        /// 测试自动安装 grain 扩展。
        /// 验证配置为 AddGrainExtension 的扩展名
        /// 在 grains 上自动可用,无需手动安装,
        /// 而在非配置扩展仍然需要安装明确装置。
        /// 这显示了通用扩展的自动电线能力。
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
