using TestExtensions;
using TestGrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// 测试 Orleans支持 grain 界面继承等级。
    /// 验证 grains 能够正确安装扩展其他接口的界面,
    /// 支持多个继承路径,正确暴露各级的方法
    /// 界面等级。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GrainInterfaceHierarchyTests : HostedTestClusterEnsureDefaultStarted
    {
        public GrainInterfaceHierarchyTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        private T GetHierarchyGrain<T>() where T : IDoSomething, IGrainWithIntegerKey
        {
            return GrainFactory.GetGrain<T>(GetRandomGrainId());
        }

        /// <summary>
        /// 测试一个 grain ,以实施简单的界面等级。
        /// 在 grain 实施接口时验证基本界面继承
        /// 来扩展一个基界面。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingGrainEmptyTest()
        {
            IDoSomethingEmptyGrain doSomething = GetHierarchyGrain<IDoSomethingEmptyGrain>();
            Assert.Equal("DoSomethingEmptyGrain", await doSomething.DoIt());
        }

        /// <summary>
        /// 测试一个 grain ,以实施扩展的界面等级。
        /// 验证 grains 能够实施添加额外方法的接口
        /// 暴露了两组功能
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingGrainEmptyWithMoreTest()
        {
            IDoSomethingEmptyWithMoreGrain doSomething = GetHierarchyGrain<IDoSomethingEmptyWithMoreGrain>();
            Assert.Equal("DoSomethingEmptyWithMoreGrain", await doSomething.DoIt());
            Assert.Equal("DoSomethingEmptyWithMoreGrain", await doSomething.DoMore());
        }

        /// <summary>
        /// 测试一个 grain ,与多方法扩展执行接口。
        /// 当接口扩展基界面时验证正确的方法解析
        /// 具有额外功能。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingWithMoreEmptyGrainTest()
        {
            IDoSomethingWithMoreEmptyGrain doSomething = GetHierarchyGrain<IDoSomethingWithMoreEmptyGrain>();
            Assert.Equal("DoSomethingWithMoreEmptyGrain", await doSomething.DoIt());
            Assert.Equal("DoSomethingWithMoreEmptyGrain", await doSomething.DoMore());
        }

        /// <summary>
        /// 以不同的界面扩展模式测试 grain 。
        /// 验证 grains 正确执行替代方法成套套件
        /// 当扩展基界面时。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingWithMoreGrainTest()
        {
            IDoSomethingWithMoreGrain doSomething = GetHierarchyGrain<IDoSomethingWithMoreGrain>();
            Assert.Equal("DoSomethingWithMoreGrain", await doSomething.DoIt());
            Assert.Equal("DoSomethingWithMoreGrain", await doSomething.DoThat());
        }

        /// <summary>
        /// 测试一个 grain 执行多个接口继承路径。
        /// 验证 grains 可以在以下位置执行复杂的界面等级结构
        /// 多个界面被组合起来,暴露了所有继承的方法。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingCombinedGrainTest()
        {
            IDoSomethingCombinedGrain doSomething = GetHierarchyGrain<IDoSomethingCombinedGrain>();
            Assert.Equal("DoSomethingCombinedGrain", await doSomething.DoIt());
            Assert.Equal("DoSomethingCombinedGrain", await doSomething.DoMore());
            Assert.Equal("DoSomethingCombinedGrain", await doSomething.DoThat());
        }

        /// <summary>
        /// 测试州管理在不同界面等级体系执行中的管理情况。
        /// 验证 grains 与不同界面等级结构保持独立
        /// 并且国家业务通过继承的接口正确运作。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingValidateSingleGrainTest()
        {
            var doSomethingEmptyGrain = GetHierarchyGrain<IDoSomethingEmptyGrain>();
            var doSomethingEmptyWithMoreGrain = GetHierarchyGrain<IDoSomethingEmptyWithMoreGrain>();
            var doSomethingWithMoreEmptyGrain = GetHierarchyGrain<IDoSomethingWithMoreEmptyGrain>();
            var doSomethingWithMoreGrain = GetHierarchyGrain<IDoSomethingWithMoreGrain>();
            var doSomethingCombinedGrain = GetHierarchyGrain<IDoSomethingCombinedGrain>();

            await doSomethingEmptyGrain.SetA(10);
            await doSomethingEmptyWithMoreGrain.SetA(10);
            await doSomethingWithMoreEmptyGrain.SetA(10);
            await doSomethingWithMoreGrain.SetA(10);
            await doSomethingWithMoreGrain.SetB(10);
            await doSomethingCombinedGrain.SetA(10);
            await doSomethingCombinedGrain.SetB(10);
            await doSomethingCombinedGrain.SetC(10);

            await doSomethingEmptyGrain.IncrementA();
            await doSomethingEmptyWithMoreGrain.IncrementA();
            await doSomethingWithMoreEmptyGrain.IncrementA();
            await doSomethingWithMoreGrain.IncrementA();
            await doSomethingWithMoreGrain.IncrementB();
            await doSomethingCombinedGrain.IncrementA();
            await doSomethingCombinedGrain.IncrementB();
            await doSomethingCombinedGrain.IncrementC();

            Assert.Equal(11, await doSomethingEmptyGrain.GetA());
            Assert.Equal(11, await doSomethingEmptyWithMoreGrain.GetA());
            Assert.Equal(11, await doSomethingWithMoreEmptyGrain.GetA());
            Assert.Equal(11, await doSomethingWithMoreGrain.GetA());
            Assert.Equal(11, await doSomethingWithMoreGrain.GetB());
            Assert.Equal(11, await doSomethingCombinedGrain.GetA());
            Assert.Equal(11, await doSomethingCombinedGrain.GetB());
            Assert.Equal(11, await doSomethingCombinedGrain.GetC());
        }
    }
}
