using TestExtensions;
using TestGrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// Orleans 对 Grain 接口继承层次结构支持的测试。
    /// 验证 Grain 能正确实现扩展其他接口的接口，
    /// 支持多条继承路径，并正确公开接口层次结构中
    /// 各级的方法。
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
        /// 测试实现简单接口层次结构的 Grain。
        /// 验证基本接口继承，其中 Grain 实现一个
        /// 扩展基接口的接口。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingGrainEmptyTest()
        {
            IDoSomethingEmptyGrain doSomething = GetHierarchyGrain<IDoSomethingEmptyGrain>();
            Assert.Equal("DoSomethingEmptyGrain", await doSomething.DoIt());
        }

        /// <summary>
        /// 测试实现扩展接口层次结构的 Grain。
        /// 验证 Grain 可以实现向基接口添加额外方法的接口，
        /// 从而公开两组功能。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingGrainEmptyWithMoreTest()
        {
            IDoSomethingEmptyWithMoreGrain doSomething = GetHierarchyGrain<IDoSomethingEmptyWithMoreGrain>();
            Assert.Equal("DoSomethingEmptyWithMoreGrain", await doSomething.DoIt());
            Assert.Equal("DoSomethingEmptyWithMoreGrain", await doSomething.DoMore());
        }

        /// <summary>
        /// 测试实现具有多个方法扩展的接口的 Grain。
        /// 验证接口扩展基接口并提供额外功能时，
        /// 能够正确解析方法。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingWithMoreEmptyGrainTest()
        {
            IDoSomethingWithMoreEmptyGrain doSomething = GetHierarchyGrain<IDoSomethingWithMoreEmptyGrain>();
            Assert.Equal("DoSomethingWithMoreEmptyGrain", await doSomething.DoIt());
            Assert.Equal("DoSomethingWithMoreEmptyGrain", await doSomething.DoMore());
        }

        /// <summary>
        /// 测试具有不同接口扩展模式的 Grain。
        /// 验证扩展基接口时 Grain 能正确实现
        /// 不同的方法集合。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DoSomethingWithMoreGrainTest()
        {
            IDoSomethingWithMoreGrain doSomething = GetHierarchyGrain<IDoSomethingWithMoreGrain>();
            Assert.Equal("DoSomethingWithMoreGrain", await doSomething.DoIt());
            Assert.Equal("DoSomethingWithMoreGrain", await doSomething.DoThat());
        }

        /// <summary>
        /// 测试具有多条接口继承路径的 Grain。
        /// 验证 Grain 能实现复杂的接口层次结构，其中多个接口被组合，
        /// 从而公开所有继承的方法。
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
        /// 测试不同接口层次结构实现之间的状态管理。
        /// 验证具有不同接口层次结构的 Grain 维护独立的状态，
        /// 且通过继承接口执行的状态操作能够正确工作。
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
