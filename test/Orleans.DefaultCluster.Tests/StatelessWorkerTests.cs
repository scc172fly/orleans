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
    /// Orleans 无状态工作者 Grain 的测试。
    /// 无状态工作者是一种特殊的 Grain 类型，每个 Silo 可以拥有多个激活实例，
    /// 用于并行处理。它们适合
    /// 不在调用之间维护状态的函数式操作。[StatelessWorker] 特性
    /// 控制本地激活实例的最大数量，支持在 Silo 内部针对
    /// CPU 密集型或 I/O 密集型操作进行水平扩展。
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
        /// 测试构造函数引发异常时无状态工作者的行为。
        /// 验证构造失败能正确报告给调用方，
        /// 且即使工作者实例化失败，系统也保持稳定。
        /// 这确保工作者初始化期间的错误得到可靠处理。
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
        /// 验证无状态工作者的激活实例遵守每个 Silo 的数量上限。
        /// 测试针对无状态工作者发出多个并发请求时，
        /// 本地激活实例数量不会超过配置的最大值。
        /// 这确保强制实施 [StatelessWorker(maxLocalWorkers)] 特性的限制，
        /// 防止无限并行导致资源耗尽。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task StatelessWorkerActivationsPerSiloDoNotExceedMaxLocalWorkersCount()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var gatewayOptions = this.Fixture.Client.ServiceProvider.GetRequiredService<IOptions<StaticGatewayListProviderOptions>>();
            var gatewaysCount = gatewayOptions.Value.Gateways.Count;
            // 发起额外调用，以触发 ExpectedMaxLocalActivations 个本地激活实例的激活
            int numberOfCalls = ExpectedMaxLocalActivations * 3 * gatewaysCount;

            IStatelessWorkerGrain grain = this.GrainFactory.GetGrain<IStatelessWorkerGrain>(GetRandomGrainId());
            List<Task> promises = new List<Task>();

            // 预热
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
                statsTasks.Add(grain.GetCallStats());  // 收集统计信息
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
                        continue; // 来自同一激活实例的重复响应

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
        /// 测试工作者激活实例受限时系统在高并发下的稳定性。
        /// 验证并发调用数量远超本地工作者上限时，
        /// 系统仍能正确处理消息转发而不发生
        /// 失败。这针对问题 #6795，即高并发下
        /// 消息转发次数过多。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("StatelessWorker")]
        public async Task ManyConcurrentInvocationsOnActivationLimitedStatelessWorkerDoesNotFail()
        {
            // 问题 #6795：并发调用数量远超本地工作者上限，导致过多的
            // 消息转发。该问题发生时，本测试会引发异常。

            // 我们试图触发竞态条件，需要多次尝试才能可靠复现问题。
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var grain = this.GrainFactory.GetGrain<IStatelessWorkerGrain>(attempt);
                await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => grain.DummyCall()));
            }
        }

        /// <summary>
        /// 测试多 Silo 部署中的无状态工作者行为。
        /// 验证跨多个 Silo 快速并发激活
        /// 不会导致转发失败。这确保工作者分布在集群中时，
        /// 负载分布能够正常工作。
        /// 注意：由于已知的转发问题，当前跳过此测试。
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

            // 发起额外调用，以触发 ExpectedMaxLocalActivations 个本地激活实例的激活
            int numberOfCalls = ExpectedMaxLocalActivations * 3 * gatewaysCount;

            IStatelessWorkerGrain grain = this.GrainFactory.GetGrain<IStatelessWorkerGrain>(GetRandomGrainId());
            List<Task> promises = new List<Task>();

            for (int i = 0; i < numberOfCalls; i++)
                promises.Add(grain.LongCall());
            await Task.WhenAll(promises);

            // 这些调用不应引发 ForwardingFailed 异常。
        }

        /// <summary>
        /// 测试无状态工作者能否对特定方法使用 [MayInterleave]。
        /// 验证标记为 MayInterleave 的方法即使在同一个激活实例内，
        /// 也能够并发处理，从而为无状态工作者提供超越默认基于执行轮次模型的
        /// 细粒度并发控制。
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
        /// 测试无状态工作者的 MayInterleave 谓词能否正确求值。
        /// 验证 MayInterleave 谓词返回 false 时，调用不会交错执行，
        /// 而是顺序执行，即使在无状态工作者中也为这些特定调用保持
        /// Orleans 基于执行轮次的并发语义。
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
        /// 测试谓词匹配时 MayInterleave 是否允许并发执行。
        /// 验证 MayInterleave 谓词返回 true 时，多个调用可以
        /// 在同一无状态工作者激活实例内并发执行，
        /// 使适合的操作达到最大的并发程度。
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

            // 等待所有回调同时触发；如果它们未在超时前发出信号，则放弃等待。
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
