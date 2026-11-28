using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 简单 Grain 功能的基础测试。
    /// 这些测试验证基本 Grain 操作，包括激活、
    /// 方法调用、状态管理和基本控制流。
    /// SimpleGrain 表示最基本的 Grain 模式，包含 getter/setter
    /// 方法，展示 Orleans 编程模型的核心概念。
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
        /// 测试基本 Grain 激活和方法调用。
        /// 验证能够从工厂获取 Grain，
        /// 并成功调用其方法，展示
        /// 基本 Grain 生命周期和 RPC 机制。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task SimpleGrainGetGrain()
        {
            ISimpleGrain grain = GetSimpleGrain();
            _ = await grain.GetAxB();
        }

        /// <summary>
        /// 测试顺序 Grain 方法调用和状态保留。
        /// 验证通过不同的方法调用设置值，随后根据
        /// 已保留的状态计算结果时，
        /// Grain 状态能在调用之间得到维护。
        /// 展示 Grain 激活实例在会话内的持续性。
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
        /// 测试并发 Grain 方法调用及数据一致性。
        /// 验证可以并发发出多个方法调用
        /// （使用 Task.WhenAll），且 Grain 能正确处理
        /// 并发操作，同时保持状态一致性。
        /// 展示 Orleans 基于执行轮次的并发模型。
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
        /// 测试具有多个构造函数的 Grain 的激活。
        /// 此测试本应验证具有多个构造函数的 Grain 会使用
        /// 默认（无参）构造函数激活。
        /// 注意：当前跳过此测试，因为在当前 Orleans 版本中，具有多个构造函数的 Grain
        /// 需要显式注册。
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
