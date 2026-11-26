using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 多元测试 grain 功能在 Orleans.
    /// 多元 grains 可实施多个接口并访问
    /// 使用 AsReference<T>() 通过不同的界面引用。
    /// 这使 grains 能够向不同的消费者披露不同的API
    /// 中,同时保留一个 grain 实例。
    /// </summary>
    //using ValueUpdateEventArgs = MultifacetGrainClient.ValueUpdateEventArgs;
    [TestArea("Runtime")]
    public class MultifacetGrainTest : HostedTestClusterEnsureDefaultStarted
    {
        private IMultifacetWriter writer = null!;
        private IMultifacetReader reader = null!;

        //以 eventCounter;
        private const int EXPECTED_NUMBER_OF_EVENTS = 4;
        private readonly TimeSpan timeout = TimeSpan.FromSeconds(5);

        public MultifacetGrainTest(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试通过不同接口的方方面面访问 grain 。
        /// 证明一个 grain 实施多个接口(撰稿人和读者)
        /// 可以通过两个接口中的任何一个接口使用 AsReference<T>()访问,
        /// 并且两个参考文献在相同的 grain 基本状态下运作。
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
        /// 证明无效的界面投影会丢弃适当的例外。
        /// 试图投放 grain 引用界面的测试
        /// 它不执行结果 在 InvalidCastException,
        /// 保证多面模式中的类型安全。
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
        /// 测试工厂的方法,以返回 grain的不同方面。
        /// 证明工厂 grains 能够创建和返回参考文件
        /// 同一 grain、扶持型
        /// 何 地 grain 创建逻辑返回合适的界面到调用者。
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
        /// 测试通过 grain 界面引用作为方法参数。
        /// 证明 grain 的不同接口面可以通过
        /// 将其他 grains储存,这些参考文献仍保留
        /// 在同一基础 grain 实例上有效并运行。
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
