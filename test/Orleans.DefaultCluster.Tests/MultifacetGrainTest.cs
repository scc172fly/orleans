using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 多面 Grain 功能测试。
    /// 多面 Grain 可以实现多个接口，并可使用不同接口引用，
    /// 通过 AsReference&lt;T&gt;() 访问。
    /// 这使 Grain 能向不同消费者公开不同的 API，
    /// 同时保持单一 Grain 实例。
    /// </summary>
    //using ValueUpdateEventArgs = MultifacetGrainClient.ValueUpdateEventArgs;
    [TestArea("Runtime")]
    public class MultifacetGrainTest : HostedTestClusterEnsureDefaultStarted
    {
        private IMultifacetWriter writer = null!;
        private IMultifacetReader reader = null!;

        //int eventCounter;
        private const int EXPECTED_NUMBER_OF_EVENTS = 4;
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(5);

        public MultifacetGrainTest(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试通过不同接口侧面访问 Grain。
        /// 验证实现多个接口（写入器和读取器）的 Grain
        /// 可以使用 AsReference&lt;T&gt;() 通过任一接口访问，
        /// 且两个引用均操作同一底层 Grain 状态。
        /// </summary>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional"), TestCategory("Cast")]
        public async Task RWReferences()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            writer = this.GrainFactory.GetGrain<IMultifacetWriter>(GetRandomGrainId());
            reader = writer.AsReference<IMultifacetReader>();

            int x = 1234;
            await writer.SetValue(x).WaitAsync(timeout, cancellationToken);
            int y = await reader.GetValue();
            Assert.Equal(x, y);
        }

        /// <summary>
        /// 验证无效接口转换引发适当的异常。
        /// 测试尝试将 Grain 引用转换为该 Grain 未实现的接口时，
        /// 会引发 InvalidCastException，
        /// 从而确保多面模式中的类型安全。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void RWReferencesInvalidCastException()
        {
            Assert.Throws<InvalidCastException>(() =>
            {
                reader = this.GrainFactory.GetGrain<IMultifacetReader>(GetRandomGrainId());
                writer = (IMultifacetWriter)reader;
            });
        }

        /// <summary>
        /// 测试返回 Grain 不同侧面的工厂方法。
        /// 验证工厂 Grain 可以创建并返回对同一个 Grain 的不同接口的引用，
        /// 从而支持 Grain 创建逻辑向调用方返回
        /// 适当接口的模式。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task MultifacetFactory()
        {
            IMultifacetFactoryTestGrain factory = this.GrainFactory.GetGrain<IMultifacetFactoryTestGrain>(GetRandomGrainId());
            IMultifacetTestGrain grain = this.GrainFactory.GetGrain<IMultifacetTestGrain>(GetRandomGrainId());
            IMultifacetWriter writer = await factory.GetWriter(grain /*"MultifacetFactory"*/);
            IMultifacetReader reader = await factory.GetReader(grain /*"MultifacetFactory"*/);
            await writer.SetValue(5);
            int v = await reader.GetValue();
            Assert.Equal(5, v);

        }

        /// <summary>
        /// 测试将 Grain 接口引用作为方法参数传递。
        /// 验证 Grain 的不同接口侧面可以传递给其他 Grain 并由其保存，
        /// 且这些引用仍然有效，并操作
        /// 同一底层 Grain 实例。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Multifacet_InterfacesAsArguments()
        {
            IMultifacetFactoryTestGrain factory = this.GrainFactory.GetGrain<IMultifacetFactoryTestGrain>(GetRandomGrainId());
            IMultifacetTestGrain grain = this.GrainFactory.GetGrain<IMultifacetTestGrain>(GetRandomGrainId());
            await factory.SetReader(grain);
            await factory.SetWriter(grain);
            IMultifacetWriter writer = await factory.GetWriter();
            IMultifacetReader reader = await factory.GetReader();
            await writer.SetValue(10);
            int v = await reader.GetValue();
            Assert.Equal(10, v);
        }
    }
}
