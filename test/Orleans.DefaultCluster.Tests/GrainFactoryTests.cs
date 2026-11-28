using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// Orleans Grain 工厂与 Grain 类型解析机制的测试。
    /// 验证 Grain 工厂如何根据接口解析 Grain 实现，
    /// 处理歧义类型映射、支持 Grain 类名前缀和完整名称，
    /// 以及在 Grain 类型解析中处理继承层次结构。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GrainFactoryTests : HostedTestClusterEnsureDefaultStarted
    {
        public GrainFactoryTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试存在歧义的 Grain 类型解析是否引发异常。
        /// 当一个接口存在多个 Grain 实现且没有显式解析方式时，
        /// Orleans 应引发 ArgumentException，以防止未定义的行为。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public void GetGrain_Ambiguous()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId());
            });
        }

        /// <summary>
        /// 测试指定默认实现时的 Grain 解析。
        /// 验证存在多个实现但其中一个标记为默认时，
        /// Orleans 能正确使用默认 Grain 实现。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Ambiguous_WithDefault()
        {
            var g = this.GrainFactory.GetGrain<IBase4>(GetRandomGrainId());
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 测试使用完全限定类型名称进行 Grain 解析。
        /// 验证存在歧义时，指定完整 Grain 类名能够正确解析
        /// 到预期实现。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_WithFullName()
        {
            var grainFullName = typeof(BaseGrain).FullName;
            var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), grainFullName);
            Assert.True(await g.Foo());
        }

        /// <summary>
        /// 测试使用 Grain 类名前缀进行 Grain 解析。
        /// 验证 Orleans 能使用部分类型名称（前缀）解析 Grain，
        /// 以方便标识 Grain。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_WithPrefix()
        {
            var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), BaseGrain.GrainPrefix);
            Assert.True(await g.Foo());
        }

        /// <summary>
        /// 测试存在歧义的 Grain 前缀是否引发异常。
        /// 当前缀匹配多个 Grain 实现时，Orleans 应
        /// 引发 ArgumentException，以防止解析歧义。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public void GetGrain_AmbiguousPrefix()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), "UnitTests.Grains");
            });
        }

        /// <summary>
        /// 测试不存在的 Grain 前缀是否引发异常。
        /// 验证尝试使用不匹配任何已注册实现的前缀解析 Grain 时，
        /// 能够正确处理错误。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public void GetGrain_WrongPrefix()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), "Foo");
            });
        }

        /// <summary>
        /// 测试不使用前缀解析派生 Grain 接口。
        /// 验证 Orleans 能正确解析继承层次结构中
        /// 实现派生接口的 Grain 实现。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Derived_NoPrefix()
        {
            var g = this.GrainFactory.GetGrain<IDerivedFromBase>(GetRandomGrainId());
            Assert.False(await g.Foo());
            Assert.True(await g.Bar());
        }

        /// <summary>
        /// 测试使用完整类型名称解析派生 Grain。
        /// 验证通过完整类型名称指定派生 Grain 实现时，
        /// 能够进行显式解析。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Derived_WithFullName()
        {
            var grainFullName = typeof(DerivedFromBaseGrain).FullName;
            var g = this.GrainFactory.GetGrain<IDerivedFromBase>(GetRandomGrainId(), grainFullName);
            Assert.False(await g.Foo());
            Assert.True(await g.Bar());
        }

        /// <summary>
        /// 测试通过基接口访问派生 Grain 实现。
        /// 验证通过完整名称显式指定派生 Grain 时，可以经由其基接口访问，
        /// 从而支持多态的 Grain 使用方式。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Derived_WithFullName_FromBase()
        {
            var grainFullName = typeof(DerivedFromBaseGrain).FullName;
            var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), grainFullName);
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 测试使用前缀解析派生 Grain。
        /// 验证基于前缀的解析能够正确处理
        /// Grain 继承层次结构。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Derived_WithPrefix()
        {
            var g = this.GrainFactory.GetGrain<IDerivedFromBase>(GetRandomGrainId(), "UnitTests.Grains");
            Assert.False(await g.Foo());
            Assert.True(await g.Bar());
        }

        /// <summary>
        /// 测试使用无效前缀解析派生 Grain 的错误处理。
        /// 验证即使在继承场景中，错误前缀也会引发
        /// 适当的异常。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public void GetGrain_Derived_WithWrongPrefix()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var g = this.GrainFactory.GetGrain<IDerivedFromBase>(GetRandomGrainId(), "Foo");
            });
        }

        /// <summary>
        /// 测试仅存在一个实现时的自动 Grain 解析。
        /// 验证 Orleans 能自动解析接口的唯一实现，
        /// 而无需前缀或完整名称。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_OneImplementation_NoPrefix()
        {
            var g = this.GrainFactory.GetGrain<IBase1>(GetRandomGrainId());
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 测试单一实现时的显式 Grain 解析。
        /// 验证即使自动解析已足够（单一实现的场景），
        /// 完整类型名称也能正常工作。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_OneImplementation_Prefix()
        {
            var grainFullName = typeof(BaseGrain1).FullName;
            var g = this.GrainFactory.GetGrain<IBase1>(GetRandomGrainId(), grainFullName);
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 测试实现多个不相关接口的 Grain。
        /// 验证 Orleans 能正确处理实现多个独立接口的 Grain，
        /// 允许通过其实现的任意接口访问。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_MultipleUnrelatedInterfaces()
        {
            var g1 = this.GrainFactory.GetGrain<IBase3>(GetRandomGrainId());
            Assert.False(await g1.Foo());
            var g2 = this.GrainFactory.GetGrain<IBase2>(GetRandomGrainId());
            Assert.True(await g2.Bar());
        }

        /// <summary>
        /// 测试使用字符串键创建 Grain。
        /// 验证 Grain 工厂能正确处理以下方式标识的 Grain：
        /// 使用字符串键而非整数或 GUID 键。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetStringGrain()
        {
            var g = this.GrainFactory.GetGrain<IStringGrain>(Guid.NewGuid().ToString());
            Assert.True(await g.Foo());
        }

        /// <summary>
        /// 测试使用 GUID 键创建 Grain。
        /// 验证 Grain 工厂能正确处理以下方式标识的 Grain：
        /// 使用 GUID 键，以支持不同的 Grain 键类型场景。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGuidGrain()
        {
            var g = this.GrainFactory.GetGrain<IGuidGrain>(Guid.NewGuid());
            Assert.True(await g.Foo());
        }

        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrainIdSpanGeneric()
        {
            var g = GrainFactory.GetGrain<IDerivedFromBase>(GrainIdKeyExtensions.CreateIntegerKey(0));
            await g.Foo();
        }

        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrainIdSpan()
        {
            var g = GrainFactory.GetGrain(typeof(IDerivedFromBase), GrainIdKeyExtensions.CreateIntegerKey(0));
            await g.AsReference<IBase>().Foo();
        }

        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrainIdSpanGenericPrefix()
        {
            var g = GrainFactory.GetGrain<IBase>(GrainIdKeyExtensions.CreateIntegerKey(0), BaseGrain.GrainPrefix);
            await g.Foo();
        }

        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrainIdSpanPrefix()
        {
            var g = GrainFactory.GetGrain(typeof(IBase), GrainIdKeyExtensions.CreateIntegerKey(0), BaseGrain.GrainPrefix);
            await g.AsReference<IBase>().Foo();
        }
    }
}
