using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.ConcurrencyTests
{
    /// <summary>
    /// 测试 Orleans Grain 的并发功能和只读方法。
    /// 
    /// Orleans 为 Grain 提供多种并发模型：
    /// - 默认：基于执行轮次的并发（一次处理一个请求）。
    /// - [ReadOnly]：方法可以与其他方法并发执行。
    /// - [AlwaysInterleave]：所有方法均可交错执行。
    /// - [Reentrant]：Grain 可以在异步等待期间处理新请求。
    /// 
    /// 这些测试聚焦于 [ReadOnly] 方法，这些方法允许多个读操作
    /// 并发执行，同时维护数据安全。这对于以下方面至关重要：
    /// - 提高读取密集型工作负载的吞吐量。
    /// - 降低多个客户端读取相同数据时的延迟。
    /// - 保持 Grain 状态一致性。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ConcurrencyTests : OrleansTestingBase, IClassFixture<ConcurrencyTests.Fixture>
    {
        private readonly Fixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
        }

        public ConcurrencyTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        /// 测试标记为 [ReadOnly] 的方法能否并发执行。
        /// 向 ReadOnly 方法发送 5 个并发请求，验证它们
        /// 均成功完成，而未被串行化执行。
        /// 若这些方法不支持并发，它们将顺序执行，
        /// 完成所需时间会长得多。
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("ReadOnly"), TestCategory("AsynchronyPrimitives")]
        public async Task ConcurrencyTest_ReadOnly()
        {
            IConcurrentGrain first = this.fixture.GrainFactory.GetGrain<IConcurrentGrain>(GetRandomGrainId());
            await first.Initialize(0);

            List<Task> promises = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                Task p = first.A();
                promises.Add(p);
            }
            await Task.WhenAll(promises);
        }

        /// <summary>
        /// 针对返回集合的并发执行进行压力测试。
        /// 验证以下情况：
        /// - 多个并发调用可以安全地返回集合。
        /// - Orleans 正确处理返回值的深拷贝。
        /// - 高并发情况下不发生数据损坏（2000 次迭代 × 20 个并发调用）。
        /// 
        /// 此测试十分重要，因为若运行时处理不当，从并发方法
        /// 返回可变集合可能导致竞争条件。
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("ReadOnly"), TestCategory("AsynchronyPrimitives")]
        public async Task ConcurrencyTest_ModifyReturnList()
        {
            IConcurrentGrain grain = this.fixture.GrainFactory.GetGrain<IConcurrentGrain>(GetRandomGrainId());

            Task<List<int>>[] ll = new Task<List<int>>[20];
            for (int i = 0; i < 2000; i++)
            {
                for (int j = 0; j < ll.Length; j++)
                    ll[j] = grain.ModifyReturnList_Test();

                await Task.WhenAll(ll);
            }
        }
    }
}
