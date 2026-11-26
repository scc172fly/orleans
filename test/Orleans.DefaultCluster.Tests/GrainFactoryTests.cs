using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// Orleans' grain 工厂和 grain 型分辨率机制的测试。
    /// 验证 grain 工厂如何从接口解决 grain 执行问题,
    /// 处理模糊类型映射,支持 grain 类前缀和全名,
    /// 管理 grain 型式解决办法中的继承等级。
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
        /// 测试模棱两可的 grain 类型分辨率会排除一个例外。
        /// 当存在多个 grain 执行点时,当接口的落实点没有明确解决时,
        /// Orleans 应该扔一个 ArgumentException ,以防止未定义的行为。
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
        /// 当指定默认执行时测试 grain 分辨率。
        /// 验证 Orleans 正确使用默认 grain 执行
        /// 当存在多个执行时, 但有一个被标记为默认 。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Ambiguous_WithDefault()
        {
            var g = this.GrainFactory.GetGrain<IBase4>(GetRandomGrainId());
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 使用完全合格的型号名称测试 grain 分辨率。
        /// 验证指定完整 grain 类名称正确解决的参数
        /// 在存在模棱两可之处时,对预定执行的意向实施产生影响。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_WithFullName()
        {
            var grainFullName = typeof(BaseGrain).FullName;
            var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), grainFullName);
            Assert.True(await g.Foo());
        }

        /// <summary>
        /// 测试 grain 分辨率使用 grain 类名前缀。
        /// 验证 Orleans 能够使用部分类型名称(前缀)解决 grains
        /// 用作识别 grain 的便利特征。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_WithPrefix()
        {
            var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), BaseGrain.GrainPrefix);
            Assert.True(await g.Foo());
        }

        /// <summary>
        /// 测试模糊 grain 前缀会丢出一个例外 。
        /// 当前缀与多个 grain 履行相匹配时, Orleans 应
        /// 扔一个 ArgumentException 防止模糊不清的解决。
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
        /// 测试不存在的 grain 前缀会排除例外 。
        /// 试图解决 grains时验证正确的错误处理
        /// 与任何登记的执行不匹配的前缀。
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
        /// 测试 grain 用于计算 grain 界面,无前缀。
        /// 验证 Orleans 正确解决了 grain 履行
        /// 在继承等级体系中实施衍生界面。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Derived_NoPrefix()
        {
            var g = this.GrainFactory.GetGrain<IDerivedFromBase>(GetRandomGrainId());
            Assert.False(await g.Foo());
            Assert.True(await g.Bar());
        }

        /// <summary>
        /// 测试 grain 用于计算 grains 使用全型名。
        /// 验证衍生 grain 执行的清晰分辨率
        /// 以其完整类型名称指定。
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
        /// 通过基界面获取衍生 grain 实施结果的测试。
        /// 验证从导出 grain 可以通过基界面访问
        /// 使用全名,支持多形态 grain 使用。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Derived_WithFullName_FromBase()
        {
            var grainFullName = typeof(DerivedFromBaseGrain).FullName;
            var g = this.GrainFactory.GetGrain<IBase>(GetRandomGrainId(), grainFullName);
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 测试 grain 用于计算 grains 使用前缀。
        /// 校验基于前置字头分辨率正确使用
        /// grain 继承等级。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_Derived_WithPrefix()
        {
            var g = this.GrainFactory.GetGrain<IDerivedFromBase>(GetRandomGrainId(), "UnitTests.Grains");
            Assert.False(await g.Foo());
            Assert.True(await g.Bar());
        }

        /// <summary>
        /// 用于处理导出 grains无效前缀的测试错误处理。
        /// 校验错误的前缀会丢出合适的例外
        /// 即使在继承情况下也是如此。
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
        /// 当只有一个执行时测试自动 grain 分辨率。
        /// 验证 Orleans 能够自动解决单一执行
        /// 中,接口无需前缀或全名。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_OneImplementation_NoPrefix()
        {
            var g = this.GrainFactory.GetGrain<IBase1>(GetRandomGrainId());
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 测试单项执行的清晰 grain 分辨率。
        /// 即使自动验证全型名是否正确
        /// 解决能力就足够了(单一执行设想方案)。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetGrain_OneImplementation_Prefix()
        {
            var grainFullName = typeof(BaseGrain1).FullName;
            var g = this.GrainFactory.GetGrain<IBase1>(GetRandomGrainId(), grainFullName);
            Assert.False(await g.Foo());
        }

        /// <summary>
        /// 测试 grains 安装多个不相关的接口。
        /// 验证 Orleans 正确处理 grains 执行多个
        /// 独立接口,允许通过任何已安装的接口访问。
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
        /// 测试以字符串键为基础的 grain 创建。
        /// 验证 grain 工厂正确处理所指定的 grains
        /// 中键,而不是整数或图形和数字界面键。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Factory"), TestCategory("GetGrain")]
        public async Task GetStringGrain()
        {
            var g = this.GrainFactory.GetGrain<IStringGrain>(Guid.NewGuid().ToString());
            Assert.True(await g.Foo());
        }

        /// <summary>
        /// 使用基于 GUID 的密钥测试 grain 创建 。
        /// 验证 grain 工厂正确处理所指定的 grains
        /// 键,支持不同的 grain 关键类型情景。
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
