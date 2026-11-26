using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.ConcurrencyTests
{
    /// <summary>
    /// Orleans grain 货币货币特征和只读方法的测试。
    /// 
    /// Orleans 为 grains提供了几种同值货币模型:
    /// - 默认:以转折为基础的货币(一次提出一个请求)
    /// - [ReadOnly:方法可以与其他方法同时使用
    /// - [AlwaysInterleave:所有方法都可以相互间
    /// - [重新批准]: Grain 可在等待期间处理新的请求
    /// 
    /// 这些测试侧重于[ReadOnly]方法,允许多读操作
    /// 保持数据安全的同时同时执行。 这对以下各点至关重要:
    /// - 改进重读工作量的处理量
    /// - 当多个客户阅读相同数据时减少潜伏
    /// - 保持 grain 国家一致性
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
        /// 标有ReadOnly的方法可以同时执行的测试。
        /// 将5项同时提出的请求发送 ReadOnly 方法并进行核实
        /// 全部完整, 没有序列化, 成功完成 。
        /// 如果这些方法不同时使用,它们将依次执行,
        /// 完成需要更长的时间。
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
        /// 压力测试,以同时执行与收集返回值。
        /// 证明:
        /// - 多个同时通话可以安全返回收藏
        /// - Orleans 正确处理回程值的深重复制
        /// - 在高通货率下没有发生数据腐败(2000年重复电话x20次同时通话)
        /// 
        /// 这个测试很重要,因为从同时返回的可变收藏
        /// 如果在运行时间没有适当处理,方法可能导致种族条件。
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
