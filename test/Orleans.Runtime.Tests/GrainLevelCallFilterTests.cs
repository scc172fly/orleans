using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("BVT"), TestCategory("GrainLevelCallFilter")]
    public class GrainLevelCallFilterTests : OrleansTestingBase, IClassFixture<GrainLevelCallFilterTests.Fixture>
    {
        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            }
        }

        private readonly Fixture fixture;

        public GrainLevelCallFilterTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        /// 不存在全局 Grain 调用筛选器时，仅测试 Grain 级别的筛选器
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_GrainLevel_Without_Global_Filter_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(0);
            var result = await grain.One();
            Assert.Equal("intercepted one with no args", result);

            result = await grain.Echo("stao erom tae");
            Assert.Equal("eat more oats", result);// Grain 拦截器应收到实现方法的 MethodInfo，而不是接口方法的 MethodInfo。

            result = await grain.NotIntercepted();
            Assert.Equal("not intercepted", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);
        }
    }
}
