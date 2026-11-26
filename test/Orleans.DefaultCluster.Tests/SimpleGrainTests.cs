using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 简单 grain 功能在 Orleans中的基本测试。
    /// 这些测试核查基本 grain 操作,包括激活、
    /// 职业技术、国家管理和基本控制流动。
    /// SimpleGrain 表示最基本 grain 模式,使用拾取器/制更
    /// 并展示核心 Orleans 方案拟订模式概念。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class SimpleGrainTests : HostedTestClusterEnsureDefaultStarted
    {
        public SimpleGrainTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        public ISimpleGrain GetSimpleGrain()
        {
            return this.GrainFactory.GetGrain<ISimpleGrain>(GetRandomGrainId(), SimpleGrain.SimpleGrainNamePrefix);
        }

        /// <summary>
        /// 基本 grain 激活和方法调试测试。
        /// 证明可从工厂和工厂获得 grain
        /// 证明方法可以成功使用,
        /// 基本 grain 生命周期和RPC机制。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task SimpleGrainGetGrain()
        {
            ISimpleGrain grain = GetSimpleGrain();
            _ = await grain.GetAxB();
        }

        /// <summary>
        /// 测试顺序 grain 方法调用和状态持久性。
        /// 证明: grain 保持状态,在调用时间之间保持
        /// 通过不同的方法调用设定数值,然后
        /// 计算基于持续状态的结果。
        /// 在会话中显示 grain 激活的持久性。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task SimpleGrainControlFlow()
        {
            ISimpleGrain grain = GetSimpleGrain();

            Task setPromise = grain.SetA(2);
            await setPromise;

            setPromise = grain.SetB(3);
            await setPromise;

            Task<int> intPromise = grain.GetAxB();
            Assert.Equal(6, await intPromise);
        }

        /// <summary>
        /// 测试同时 grain 方法调用和数据一致性。
        /// 证明可同时发出多方法电话
        /// 使用 Task.WhenAll,而且 grain 正确处理
        /// 保持国家一致性的同时同时运行。
        /// 演示 Orleans的转折货币模式。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task SimpleGrainDataFlow()
        {
            ISimpleGrain grain = GetSimpleGrain();

            Task setAPromise = grain.SetA(3);
            Task setBPromise = grain.SetB(4);
            await Task.WhenAll(setAPromise, setBPromise);
            var x = await grain.GetAxB();

            Assert.Equal(12, x);
        }

        /// <summary>
        /// 与多个构造器一起进行测试 grain 激活。
        /// 将核实 grains 多建构器激活
        /// 使用默认(无参数)构建器。
        /// 注:目前被多个建构器跳过,作为 grains 。
        /// 要求在当前 Orleans 版本中明确注册。
        /// </summary>
        [Fact(Skip = "Grains with multiple constructors are not supported without being explicitly registered.")]
        [TestCategory("BVT")]
        public async Task GettingGrainWithMultipleConstructorsActivesViaDefaultConstructor()
        {
            ISimpleGrain grain = this.GrainFactory.GetGrain<ISimpleGrain>(GetRandomGrainId(), grainClassNamePrefix: MultipleConstructorsSimpleGrain.MultipleConstructorsSimpleGrainPrefix);

            var actual = await grain.GetA();
            Assert.Equal(MultipleConstructorsSimpleGrain.ValueUsedByParameterlessConstructor, actual);
        }
    }
}
