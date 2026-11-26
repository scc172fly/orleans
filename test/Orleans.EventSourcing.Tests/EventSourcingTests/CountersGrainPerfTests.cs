using TestGrainInterfaces;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Tester.EventSourcingTests
{
    /// <summary>
    /// 事件源对面器 grain 的性能测试,比较不同的同步和再生战略。
    /// </summary>
    [TestMethodOrderer(typeof(SimplePriorityOrderer))]
    public partial class CountersGrainTests
    {

        // 您可以查看以下每次测试的时间
        // 来获得一个粗略的想法 关于同步选择, 和配置参数,
        // 以及一致性提供方,影响输送量

        // 使用视觉摄影棚进行这些孔径测试
        // "CountersGrainTests.Perf"在搜索箱中,然后是"全部运行"
        // 这将运行热热电流, 然后所有测试, 在同一测试组群中。 它随后报告
        // 每种试验的大约时间。这不是一个测试,只是
        // 说明 JournaledGrain 绩效如何随所作选择而变化。

        // 你该看到的是:
        // - 保守方法(确认每次更新,不允许重新使用)缓慢。
        // - 仅确认结尾,而不是每次更新后,是快速的。
        // - 允许再生,虽然每次更新后仍然确认,但速度也很快。

        private const int iterations = 400;

        [TestArea("EventSourcing")]
        [Fact, RunThisFirst, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public Task Perf_Warmup()
        {
            // 在每个 grain 上重新调用,以确保每次测试激活都装上并启动。
            return Task.WhenAll(
                GetGrain(PerfGrain.MemoryStateStoreNonReentrantConfirmEach, "TestGrains.CountersGrain_StateStore_NonReentrant").Reset(true),
                GetGrain(PerfGrain.MemoryStateStoreNonReentrantConfirmAtEnd, "TestGrains.CountersGrain_StateStore_NonReentrant").Reset(true),
                GetGrain(PerfGrain.MemoryLogStoreNonReentrantConfirmEach, "TestGrains.CountersGrain_LogStore_NonReentrant").Reset(true),
                GetGrain(PerfGrain.MemoryLogStoreNonReentrantConfirmAtEnd, "TestGrains.CountersGrain_LogStore_NonReentrant").Reset(true),
                GetGrain(PerfGrain.MemoryStateStoreReentrantConfirmEach, "TestGrains.CountersGrain_StateStore_Reentrant").Reset(true),
                GetGrain(PerfGrain.MemoryStateStoreReentrantConfirmAtEnd, "TestGrains.CountersGrain_StateStore_Reentrant").Reset(true),
                GetGrain(PerfGrain.MemoryLogStoreReentrantConfirmEach, "TestGrains.CountersGrain_LogStore_Reentrant").Reset(true),
                GetGrain(PerfGrain.MemoryLogStoreReentrantConfirmAtEnd, "TestGrains.CountersGrain_LogStore_Reentrant").Reset(true)
            );
        }

        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmEachUpdate_MemoryStateStore_NonReentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryStateStoreNonReentrantConfirmEach, "TestGrains.CountersGrain_StateStore_NonReentrant");
            await ConcurrentIncrementsRunner(grain, iterations, true);
        }
        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmAtEndOnly_MemoryStateStore_NonReentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryStateStoreNonReentrantConfirmAtEnd, "TestGrains.CountersGrain_StateStore_NonReentrant");
            await ConcurrentIncrementsRunner(grain, iterations, false);
        }
        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmEachUpdate_MemoryLogStore_NonReentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryLogStoreNonReentrantConfirmEach, "TestGrains.CountersGrain_LogStore_NonReentrant");
            await ConcurrentIncrementsRunner(grain, iterations, true);
        }
        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmAtEndOnly_MemoryLogStore_NonReentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryLogStoreNonReentrantConfirmAtEnd, "TestGrains.CountersGrain_LogStore_NonReentrant");
            await ConcurrentIncrementsRunner(grain, iterations, false);
        }
        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmEachUpdate_MemoryStateStore_Reentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryStateStoreReentrantConfirmEach, "TestGrains.CountersGrain_StateStore_Reentrant");
            await ConcurrentIncrementsRunner(grain, iterations, true);
        }
        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmAtEndOnly_MemoryStateStore_Reentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryStateStoreReentrantConfirmAtEnd, "TestGrains.CountersGrain_StateStore_Reentrant");
            await ConcurrentIncrementsRunner(grain, iterations, false);
        }
        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmEachUpdate_MemoryLogStore_Reentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryLogStoreReentrantConfirmEach, "TestGrains.CountersGrain_LogStore_Reentrant");
            await ConcurrentIncrementsRunner(grain, iterations, true);
        }
        [TestArea("EventSourcing")]
        [Fact, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public async Task Perf_ConfirmAtEndOnly_MemoryLogStore_Reentrant()
        {
            var grain = GetGrain(PerfGrain.MemoryLogStoreReentrantConfirmAtEnd, "TestGrains.CountersGrain_LogStore_Reentrant");
            await ConcurrentIncrementsRunner(grain, iterations, false);
        }

        private ICountersGrain GetGrain(PerfGrain grain, string grainClassName) =>
            this.fixture.GrainFactory.GetGrain<ICountersGrain>((long)grain, grainClassName);

        private enum PerfGrain
        {
            MemoryStateStoreNonReentrantConfirmEach,
            MemoryStateStoreNonReentrantConfirmAtEnd,
            MemoryLogStoreNonReentrantConfirmEach,
            MemoryLogStoreNonReentrantConfirmAtEnd,
            MemoryStateStoreReentrantConfirmEach,
            MemoryStateStoreReentrantConfirmAtEnd,
            MemoryLogStoreReentrantConfirmEach,
            MemoryLogStoreReentrantConfirmAtEnd,
        }
    }

    internal class RunThisFirstAttribute : Attribute
    {
    }

    public class SimplePriorityOrderer : ITestMethodOrderer
    {
        private static bool HasRunThisFirstAttribute(ITestMethod? testMethod)
        {
            return testMethod is IXunitTestMethod xunitTestMethod
                && xunitTestMethod.Method.GetCustomAttributes(typeof(RunThisFirstAttribute), inherit: true).Any();
        }

        public IReadOnlyCollection<TTestMethod?> OrderTestMethods<TTestMethod>(IReadOnlyCollection<TTestMethod?> testMethods) where TTestMethod : notnull, ITestMethod
        {
            return
            [
                .. testMethods.Where(testMethod => HasRunThisFirstAttribute(testMethod)),
                .. testMethods.Where(testMethod => !HasRunThisFirstAttribute(testMethod)),
            ];
        }
    }

}
