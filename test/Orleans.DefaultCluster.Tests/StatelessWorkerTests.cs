using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 无国籍工人 grains的测试。
    /// 无国籍工人是一种特殊 grain 类型,可具有多重活化
    /// 百分比/ 百 silo 用于平行处理。它们对于功能操作是理想的
    /// 无法保持通话状态。 [StatelessWorker]属性
    /// 允许水平
    /// CPU 或 I/O 限制操作的 silo 范围内的缩放。
    /// </summary>
    [TestArea("Runtime")]
    public class StatelessWorkerTests : HostedTestClusterEnsureDefaultStarted
    {
        private readonly int ExpectedMaxLocalActivations = StatelessWorkerGrain.MaxLocalWorkers; // System.Environment.ProcessorCount;
        private readonly ITestOutputHelper output;

        public StatelessWorkerTests(DefaultClusterFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            this.output = output;
        }

        /// <summary>
        /// 建筑商抛出例外时测试无国籍工人的行为。
        /// 证明向打电话者适当报告建筑失修情况
        /// 并且即使工人未能即时地工作,这个制度也保持稳定。
        /// 这确保了工人初始化时处理稳健错误。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task StatelessWorkerThrowExceptionConstructor()
        {
            var grain = this.GrainFactory.GetGrain<IStatelessWorkerExceptionGrain>(0);

            for (int i = 0; i < 5; i++)
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => grain.Ping());
                Assert.Contains("Failed to create an instance of grain type", ex.Message);
                var iex = Assert.IsType<Exception>(ex.InnerException);
                Assert.Equal("oops", iex.Message);
            }
        }

        /// <summary>
        /// 证明无国籍工人启动的无国籍工人遵守了每个工人的最高限制 silo.
        /// 测试,当同时向无国籍工人提出多重请求时,
        /// 本地激活的数量不超过配置的最大数量 。
        /// 这确保[StatelessWorker(maxLocalWorkers) 属性得到强制执行,
        /// 防止资源耗竭,
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task StatelessWorkerActivationsPerSiloDoNotExceedMaxLocalWorkersCount()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var gatewayOptions = this.Fixture.Client.ServiceProvider.GetRequiredService<IOptions<StaticGatewayListProviderOptions>>();
            var gatewaysCount = gatewayOptions.Value.Gateways.Count;
            // 额外调用触发本地 ExpectedMaxLocalActivations 启动
            int numberOfCalls = ExpectedMaxLocalActivations * 3 * gatewaysCount;

            IStatelessWorkerGrain grain = this.GrainFactory.GetGrain<IStatelessWorkerGrain>(GetRandomGrainId());
            List<Task> promises = new List<Task>();

            // 热暖和
            for (int i = 0; i < gatewaysCount; i++)
                promises.Add(grain.LongCall());
            await Task.WhenAll(promises);

            await Task.Delay(2000, cancellationToken);

            promises.Clear();
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < numberOfCalls; i++)
                promises.Add(grain.LongCall());
            await Task.WhenAll(promises);

            stopwatch.Stop();

            promises.Clear();

            var statsTasks = new List<Task<Tuple<Guid, string, List<Tuple<DateTime, DateTime>>>>>();
            for (int i = 0; i < numberOfCalls; i++)
                statsTasks.Add(grain.GetCallStats());  // 正在收集统计数
            await Task.WhenAll(statsTasks);

#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
            var responsesPerSilo = statsTasks.Select(t => t.Result).GroupBy(s => s.Item2);
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method
            foreach (var siloGroup in responsesPerSilo)
            {
                var silo = siloGroup.Key;

                HashSet<Guid> activations = new HashSet<Guid>();

                foreach (var response in siloGroup)
                {
                    if (activations.Contains(response.Item1))
                        continue; // 同一激活的重复响应

                    activations.Add(response.Item1);

                    output.WriteLine($"Silo {silo} with {activations.Count} activations: Activation {response.Item1}");
                    int count = 1;
                    foreach (Tuple<DateTime, DateTime> call in response.Item3)
                        output.WriteLine($"\t{count++}: {LogFormatter.PrintDate(call.Item1)} - {LogFormatter.PrintDate(call.Item2)}");
                }

                Assert.True(activations.Count <= ExpectedMaxLocalActivations, $"activations.Count = {activations.Count} in silo {silo} but expected no more than {ExpectedMaxLocalActivations}");
            }
        }

        /// <summary>
        /// 测试系统稳定性,在高通货率下与有限的工人活化作用进行。
        /// 证明当同时援引时大大超过当地
        /// 工人限制, 系统可以正确处理信件转发, 而没有
        /// 失败。 这解决了有关过度信息前方问题的# 6795
        /// 高通货货币之下。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task ManyConcurrentInvocationsOnActivationLimitedStatelessWorkerDoesNotFail()
        {
            // 问题#6795:比当地工人的限额多得多的同时从事的职业导致太多
            // 向前传递信件。 当问题发生时, 此测试将会丢弃一个例外 。

            // 我们试图引发种族状况,需要不止一次努力可靠地复制这一问题。
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var grain = this.GrainFactory.GetGrain<IStatelessWorkerGrain>(attempt);
                await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => grain.DummyCall()));
            }
        }

        /// <summary>
        /// 测试多silo 部署中的无国籍工人行为。
        /// 证明在多个 silos之间快速同时启动
        /// 不会造成转发失败。 这可以确保负载分布
        /// 当工人分布在组群中时, 工作正常。
        /// 注:由于一个已知问题,现因已转交而跳过。
        /// </summary>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact(Skip = "Skipping test for now, since there seems to be a bug"), TestCategory("Functional"), TestCategory("StatelessWorker")]
        public async Task StatelessWorkerFastActivationsDontFailInMultiSiloDeployment()
        {
            var gatewayOptions = this.Fixture.Client.ServiceProvider.GetRequiredService<IOptions<StaticGatewayListProviderOptions>>();
            var gatewaysCount = gatewayOptions.Value.Gateways.Count;

            if (gatewaysCount < 2)
            {
                throw Xunit.Sdk.SkipException.ForSkip("This test was created to run with more than 1 gateway. 2 is the default at the time of this writing");
            }

            // 额外调用触发本地 ExpectedMaxLocalActivations 启动
            int numberOfCalls = ExpectedMaxLocalActivations * 3 * gatewaysCount;

            IStatelessWorkerGrain grain = this.GrainFactory.GetGrain<IStatelessWorkerGrain>(GetRandomGrainId());
            List<Task> promises = new List<Task>();

            for (int i = 0; i < numberOfCalls; i++)
                promises.Add(grain.LongCall());
            await Task.WhenAll(promises);

            // 不应提出过要求 ForwardingFailed 例外。
        }

        /// <summary>
        /// 无国籍工人可以对具体方法MayInterleave进行测试。
        /// 证明标有 MayInterleave 的方法可以同时处理
        /// 即使在单一激活范围内,允许精制同价货币控制
        /// 超过默认的转折模式的无国籍工人。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task StatelessWorker_DoesNotThrow_IfMarkedWithMayInterleave()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = GrainFactory.GetGrain<IStatelessWorkerWithMayInterleaveGrain>(0);
            var observer = new CallbackObserver();
            var reference = GrainFactory.CreateObjectReference<ICallbackGrainObserver>(observer);
            var task = grain.GoFast(reference);
            await observer.OnCallback.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            observer.Signal();
            await task.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 检验 MayInterleave 上游情况是否为无国籍工人得到适当评估。
        /// 当它被证实的时候, MayInterleave 自动返回假的上游回错,调用是
        /// 不中断和按顺序执行, 维持 Orleans的转式
        /// 即使是无国籍工人也用同种货币来响应这些具体呼唤。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task StatelessWorker_ShouldNotInterleaveCalls_IfMayInterleavePredicatedDoesntMatch()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = GrainFactory.GetGrain<IStatelessWorkerWithMayInterleaveGrain>(0);

            List<CallbackObserver> callbacks = [new(), new(), new()];
            var callbackReferences = callbacks.Select(c => GrainFactory.CreateObjectReference<ICallbackGrainObserver>(c)).ToList();
            List<Task> completions = [grain.GoSlow(callbackReferences[0]), grain.GoSlow(callbackReferences[1]), grain.GoSlow(callbackReferences[2])];
            var callbackSignals = callbacks.Select(c => c.OnCallback).ToList();

            var triggered = await Task.WhenAny(callbackSignals).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            callbackSignals.Remove(triggered);
            try
            {
                await Task.WhenAny(callbackSignals).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                Assert.Fail("A second callback should not have been triggered.");
            }
            catch (TimeoutException)
            {
            }

            callbacks.ForEach(c => c.Signal());
            await Task.WhenAll(completions).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        /// <summary>
        /// 测试 MayInterleave 允许在上游匹配时同时执行。
        /// 证明如果 MayInterleave 上游返回为真时,多次调用
        /// 在同一无国籍工人激活过程中可同时执行,
        /// 使适当的作业具有最大限度的平行性。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task StatelessWorker_ShouldInterleaveCalls_IfMayInterleavePredicatedMatches()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var grain = GrainFactory.GetGrain<IStatelessWorkerWithMayInterleaveGrain>(0);

            List<CallbackObserver> callbacks = [new(), new(), new()];
            var callbackReferences = callbacks.Select(c => GrainFactory.CreateObjectReference<ICallbackGrainObserver>(c)).ToList();
            var completion = Task.WhenAll(grain.GoFast(callbackReferences[0]), grain.GoFast(callbackReferences[1]), grain.GoFast(callbackReferences[2]));

            // 等待所有的回调同时启动, 如果它们没有在超时前发出信号就放弃。
            await Task.WhenAll(callbacks.Select(c => c.OnCallback)).WaitAsync(cts.Token);
            callbacks.ForEach(c => c.Signal());

            await completion.WaitAsync(cts.Token);
        }

        private sealed class CallbackObserver : ICallbackGrainObserver
        {
            private readonly TaskCompletionSource _waitAsyncTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _wasCalledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Signal() => _waitAsyncTcs.TrySetResult();
            public Task OnCallback => _wasCalledTcs.Task;
            async Task ICallbackGrainObserver.WaitAsync()
            {
                _wasCalledTcs.TrySetResult();
                await _waitAsyncTcs.Task;
            }
        }
    }
}
