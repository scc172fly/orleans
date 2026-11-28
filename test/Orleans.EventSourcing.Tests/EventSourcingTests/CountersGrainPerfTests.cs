using TestGrainInterfaces;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Tester.EventSourcingTests
{
    /// <summary>
    /// 比较不同同步策略及可重入策略下事件溯源计数器 Grain 的性能。
    /// </summary>
    [TestMethodOrderer(typeof(SimplePriorityOrderer))]
    public partial class CountersGrainTests
    {

        // 可以查看以下各项测试所用的时间，
        // 粗略了解同步方式、配置参数
        // 及一致性提供程序如何影响吞吐量。

        // 要在 Visual Studio 中运行这些性能测试，首先在搜索框中输入
        // "CountersGrainTests.Perf"，然后选择 "Run All"。
        // 这将在同一个测试集群中运行预热和全部测试，随后报告
        // 每项测试的近似耗时。这实际上不是用于断言的测试，只是
        // 用来说明不同选择如何影响 JournaledGrain 的性能。

        // 预期会观察到以下现象：
        // - 保守方式（确认每次更新、禁止重入）较慢。
        // - 仅在结束时确认，而不是每次更新后确认，速度较快。
        // - 允许重入，同时仍在每次更新后进行确认，也较快。

        private const int iterations = 400;

        [TestArea("EventSourcing")]
        [Fact, RunThisFirst, TestSuite("Nightly"), TestCategory("EventSourcing")]
        public Task Perf_Warmup()
        {
            // 对每个 Grain 调用 reset，以确保每个测试激活实例已加载并完成预热。
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
