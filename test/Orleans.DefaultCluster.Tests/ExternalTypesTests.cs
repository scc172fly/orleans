using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 Orleans 处理 Grain 接口和实现程序集之外定义的类型的能力。
    /// 验证序列化系统能够正确处理作为 Grain 方法参数传递的外部类型（例如枚举）。
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
        /// 测试 Grain 能否成功接收并处理外部程序集定义的枚举类型。
        /// 这验证了 Orleans 类型系统对未在 Grain 接口程序集中直接引用的类型进行
        /// 序列化及反序列化的能力，对于支持共享领域模型很重要。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Serialization")]
        public async Task ExternalTypesTest_GrainWithEnumExternalTypeParam()
        {
            var grainWithEnumTypeParam = this.GrainFactory.GetGrain<IExternalTypeGrain>(0);
            await grainWithEnumTypeParam.GetEnumModel();
        }
    }
}
