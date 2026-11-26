using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 Orleans处理 grain 接口和执行组件以外定义的类型的能力。
    /// 证明序列系统正确处理外部类型(如昆虫),作为参数通过至 grain 方法。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ExternalTypesTests : HostedTestClusterEnsureDefaultStarted
    {
        public ExternalTypesTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// grains 能够成功接收和处理外部组合中定义的昆虫类型的测试。
        /// 以此验证 Orleans' 类型系统对未直接引用的类别进行序列/去序化的能力
        /// 在 grain 接口组件中,这对支持共享域模型十分重要。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Serialization")]
        public async Task ExternalTypesTest_GrainWithEnumExternalTypeParam()
        {
            var grainWithEnumTypeParam = this.GrainFactory.GetGrain<IExternalTypeGrain>(0);
            await grainWithEnumTypeParam.GetEnumModel();
        }
    }
}
