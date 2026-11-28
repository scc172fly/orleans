using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.TestingHost;
using TestExtensions;
using UnitTestGrains;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.TimerTests
{
    public static class TimerOrleansTestCollection
    {
        public const string Name = nameof(TimerOrleansTest);
    }

    [CollectionDefinition(TimerOrleansTestCollection.Name)]
    public sealed class TimerOrleansTestCollectionDefinition : ICollectionFixture<TimerOrleansTest.Fixture>
    {
    }

    /// <summary>
    /// Orleans 定时器功能测试。
    /// 定时器提供 Grain 本地的、非持久化的周期性回调。与提醒器不同，
    /// 定时器仅在 Grain 处于激活状态时有效，不会跨
    /// 停用保留。它们适合短期周期性任务、轮询、
    /// 超时及其他无需持久化的场景。
    /// 对于高频操作，定时器比提醒器更高效。
    /// </summary>
    [Collection(TimerOrleansTestCollection.Name)]
    public class TimerOrleansTest : OrleansTestingBase
    {
        private const int ExpectedDefaultTimerTicks = 10;
        private static readonly TimeSpan OneShotTimerDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan TimerCallbackDelay = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan TimerOverloadDueTime = TimeSpan.FromMilliseconds(25);
        private static readonly TimeSpan TimerDiagnosticTimeout = TimeSpan.FromSeconds(10);
        private readonly Fixture fixture;
        private readonly ITestOutputHelper output;

        public TimerOrleansTest(ITestOutputHelper output, Fixture fixture)
        {
            this.fixture = fixture;
            this.output = output;
        }

        private IGrainFactory GrainFactory => fixture.GrainFactory;

        private async Task<int> AdvanceDefaultTimerTicksAsync(ITimerGrain grain, TimeSpan period)
        {
            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            for (var expectedTickCount = 1; expectedTickCount <= ExpectedDefaultTimerTicks; expectedTickCount++)
            {
                await AdvanceTimerToTickCountAsync(grain, timerObserver, period, expectedTickCount);
                await grain.GetCounter();
            }

            return await grain.GetCounter();
        }

        private async Task AdvanceDefaultTimerTicksAsync<TGrain>(IReadOnlyList<TGrain> grains, TimeSpan period)
            where TGrain : ITimerGrain
        {
            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            for (var expectedTickCount = 1; expectedTickCount <= ExpectedDefaultTimerTicks; expectedTickCount++)
            {
                var waitForTicks = Task.WhenAll(grains.Select(g => timerObserver.WaitForTickCountAsync(g, expectedTickCount, TimerDiagnosticTimeout)));
                await fixture.AdvanceTimeAsync(period);
                await waitForTicks;
                await Task.WhenAll(grains.Select(g => g.GetCounter()));
            }
        }

        private async Task AdvanceTimerToTickCountAsync(IAddressable grain, TimerDiagnosticObserver timerObserver, TimeSpan dueTime, int expectedTickCount)
        {
            var waitForTick = timerObserver.WaitForTickCountAsync(grain, expectedTickCount, TimerDiagnosticTimeout);
            await fixture.AdvanceTimeAsync(dueTime);
            await waitForTick;
        }

        private async Task AdvanceTimerToTickCountAsync(
            IAddressable grain,
            TimerDiagnosticObserver timerObserver,
            TimeSpan dueTime,
            TimeSpan callbackDelay,
            int expectedTickCount)
        {
            using var callbackObserver = TimerCallbackDiagnosticObserver.Create();
            var grainId = grain.GetGrainId();
            var delayScheduledCount = callbackObserver.GetDelayScheduledCount(grainId);

            await fixture.AdvanceTimeAsync(dueTime);
            for (var tickCount = timerObserver.GetTickCount(grain.GetGrainId()) + 1; tickCount <= expectedTickCount; tickCount++)
            {
                await timerObserver.WaitForTickStartCountAsync(grain, tickCount, TimerDiagnosticTimeout);
                await callbackObserver.WaitForDelayScheduledCountAsync(grainId, ++delayScheduledCount, TimerDiagnosticTimeout);

                var waitForTickStop = timerObserver.WaitForTickCountAsync(grain, tickCount, TimerDiagnosticTimeout);
                await fixture.AdvanceTimeAsync(callbackDelay);
                await waitForTickStop;
            }
        }

        private async Task<int> DriveExternalTickUntilTimerTicks(INonReentrantTimerCallGrain grain, TimerDiagnosticObserver timerObserver, TimeSpan dueTime, int expectedTimerTicks)
        {
            using var callbackObserver = TimerCallbackDiagnosticObserver.Create();
            var grainId = grain.GetGrainId();
            var waitForDelay = callbackObserver.WaitForDelayScheduledCountAsync(
                grainId, callbackObserver.GetDelayScheduledCount(grainId) + 1, TimerDiagnosticTimeout);
            var externalTick = grain.ExternalTick("external");
            await waitForDelay;
            await fixture.AdvanceTimeAsync(TimerCallbackDelay);
            await externalTick;
            await AdvanceTimerToTickCountAsync(grain, timerObserver, dueTime, TimerCallbackDelay, expectedTimerTicks);
            return 1;
        }

        private async Task RunSelfDisposingTimerAsync(ITimerCallGrain grain)
        {
            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            var runTimer = grain.RunSelfDisposingTimer();
            var created = await timerObserver.WaitForTimerCreatedAsync(grain, TimerDiagnosticTimeout);
            using var cts = new CancellationTokenSource(TimerDiagnosticTimeout);
            await fixture.WaitForTimerChangeAsync(created.Timer, changeCount: 1, cts.Token);
            await AdvanceTimerToTickCountAsync(grain, timerObserver, created.DueTime, TimeSpan.FromMilliseconds(100), expectedTickCount: 1);
            await runTimer;
        }

        private sealed class TimerCallbackDiagnosticObserver : IDisposable, IObserver<TimerGrainCallbackEvents.CallbackEvent>
        {
            private readonly ConcurrentBag<TimerGrainCallbackEvents.DelayScheduled> delayScheduledEvents = new();
            private readonly object changeLock = new();
            private TaskCompletionSource changed = CreateCompletionSource();
            private IDisposable? subscription;

            public static TimerCallbackDiagnosticObserver Create()
            {
                var observer = new TimerCallbackDiagnosticObserver();
                observer.subscription = TimerGrainCallbackEvents.AllEvents.Subscribe(observer);
                return observer;
            }

            public int GetDelayScheduledCount(GrainId grainId)
            {
                return delayScheduledEvents.Count(e => e.GrainId == grainId);
            }

            public async Task WaitForDelayScheduledCountAsync(GrainId grainId, int expectedCount, TimeSpan timeout)
            {
                using var cts = new CancellationTokenSource(timeout);

                while (true)
                {
                    Task changedTask;
                    lock (changeLock)
                    {
                        var currentCount = GetDelayScheduledCount(grainId);
                        if (currentCount >= expectedCount)
                        {
                            return;
                        }

                        changedTask = changed.Task;
                    }

                    try
                    {
                        await changedTask.WaitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                var finalCount = GetDelayScheduledCount(grainId);
                if (finalCount >= expectedCount)
                {
                    return;
                }

                throw new TimeoutException($"Timed out waiting for {expectedCount} timer callbacks to schedule their fake delay on grain {grainId}. Current count: {finalCount} after {timeout}");
            }

            void IObserver<TimerGrainCallbackEvents.CallbackEvent>.OnNext(TimerGrainCallbackEvents.CallbackEvent value)
            {
                if (value is TimerGrainCallbackEvents.DelayScheduled delayScheduled)
                {
                    delayScheduledEvents.Add(delayScheduled);
                    SignalChanged();
                }
            }

            void IObserver<TimerGrainCallbackEvents.CallbackEvent>.OnError(Exception error)
            {
            }

            void IObserver<TimerGrainCallbackEvents.CallbackEvent>.OnCompleted()
            {
            }

            public void Dispose()
            {
                subscription?.Dispose();
            }

            private static TaskCompletionSource CreateCompletionSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

            private void SignalChanged()
            {
                TaskCompletionSource previous;
                lock (changeLock)
                {
                    previous = changed;
                    changed = CreateCompletionSource();
                }

                previous.TrySetResult();
            }
        }

        public sealed class Fixture : BaseInProcessTestClusterFixture
        {
            private readonly TrackingFakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
            {
                builder.ConfigureSilo((_, siloBuilder) =>
                {
                    siloBuilder
                        .Configure<SiloMessagingOptions>(o => o.ClientGatewayShutdownNotificationTimeout = default)
                        .UseInMemoryReminderService()
                        .AddMemoryGrainStorageAsDefault()
                        .AddMemoryGrainStorage("MemoryStore");

                    siloBuilder.UseFakeTimeProviderForGrainTimers(timeProvider);
                });
            }

            public Task AdvanceTimeAsync(TimeSpan duration) => FakeTimeSilo.AdvanceAsync(timeProvider, duration);

            public Task WaitForTimerChangeAsync(object timer, int changeCount, CancellationToken cancellationToken) =>
                timeProvider.WaitForTimerChangeAsync(timer, changeCount, cancellationToken);
        }

        /// <summary>
        /// 测试定时器基本功能，包括启动与停止操作。
        /// 验证定时器按预期时间间隔触发、可以停止，
        /// 且停止定时器可以阻止后续触发。这展示了
        /// Grain 激活实例内定时器的基本生命周期。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Basic()
        {
            for (int i = 0; i < 10; i++)
            {
                var grain = GrainFactory.GetGrain<ITimerGrain>(GetRandomGrainId());
                var period = await grain.GetTimerPeriod();
                var last = await AdvanceDefaultTimerTicksAsync(grain, period);

                output.WriteLine("value = " + last);
                Assert.Equal(ExpectedDefaultTimerTicks, last);

                await grain.StopDefaultTimer();
                await fixture.AdvanceTimeAsync(period.Multiply(2));
                var curr = await grain.GetCounter();
                Assert.Equal(last, curr);
            }
        }

        /// <summary>
        /// 测试多个 Grain 的定时器并行运行。
        /// 验证每个 Grain 维护各自独立的定时器，
        /// 且不同 Grain 激活实例的定时器互不干扰，
        /// 展示各 Grain 的定时器隔离。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Parallel()
        {
            TimeSpan period = TimeSpan.Zero;
            List<ITimerGrain> grains = new List<ITimerGrain>();
            for (int i = 0; i < 10; i++)
            {
                ITimerGrain grain = GrainFactory.GetGrain<ITimerGrain>(GetRandomGrainId());
                grains.Add(grain);
                period = await grain.GetTimerPeriod(); // 激活 Grain
            }

            await AdvanceDefaultTimerTicksAsync(grains, period);
            for (int i = 0; i < grains.Count; i++)
            {
                ITimerGrain grain = grains[i];
                var last = await grain.GetCounter();
                output.WriteLine("value = " + last);
                Assert.Equal(ExpectedDefaultTimerTicks, last);
                await grain.StopDefaultTimer();
            }
        }

        /// <summary>
        /// 测试 Grain 停用与重新激活时的定时器行为。
        /// 验证定时器不会跨 Grain 生命周期保留：当 Grain
        /// 停用并重新激活时，定时器会重新开始。这展示了
        /// 定时器与提醒器相比不具备持久化特性。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Migration()
        {
            ITimerGrain grain = GrainFactory.GetGrain<ITimerGrain>(GetRandomGrainId());
            TimeSpan period = await grain.GetTimerPeriod();

            // 确保 Grain 正常工作。
            var last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);
            output.WriteLine("value = " + last);

            // 重新激活 Grain。
            await grain.Deactivate();
            last = await grain.GetCounter();
            Assert.True(last == 0, "Restarted grains should have zero ticks. Actual: " + last);
            period = await grain.GetTimerPeriod();

            // 调用 Grain 以确认其仍然正常工作。
            last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);

            output.WriteLine(
                "Virtual elapsed time = " + (period.Multiply(ExpectedDefaultTimerTicks).TotalSeconds) + " sec. Expected ticks = " + ExpectedDefaultTimerTicks +
                ". Actual ticks = " + last);
        }

        /// <summary>
        /// 测试在回调中发起异步 Grain 调用的定时器。
        /// 验证定时器回调可以执行 Grain 间通信，
        /// 且定时器回调中的异常会被捕获，不会导致
        /// Grain 崩溃。这对于基于定时器的编排模式很重要。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task AsyncTimerTest_GrainCall()
        {
            const string testName = "AsyncTimerTest_GrainCall";
            TimeSpan delay = OneShotTimerDelay;

            ITimerCallGrain? grain = null;

            Exception? error = null;
            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            try
            {
                grain = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());

                await grain.StartTimer(testName, delay);

                await AdvanceTimerToTickCountAsync(grain, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);

                int tickCount = await grain.GetTickCount();
                Assert.Equal(1, tickCount);

                Exception? err = await grain.GetException();
                Assert.Null(err); // 定时器回调期间不应出现异常
            }
            catch (Exception exc)
            {
                output.WriteLine(exc);
                error = exc;
            }

            try
            {
                if (grain != null) await grain.StopTimer(testName);
            }
            catch (Exception exc)
            {
                // 忽略。
                output.WriteLine("Ignoring exception from StopTimer : {0}", exc);
            }

            if (error != null)
            {
                Assert.Fail($"Test {testName} failed with error {error}");
            }
        }

        /// <summary>
        /// 测试所有创建定时器的重载及其行为。
        /// 验证不同定时器注册方法（有无状态、
        /// 不同的周期指定方式）均能正常工作，且所有定时器
        /// 变体均按预期执行回调。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task GrainTimer_TestAllOverloads()
        {
            var grain = GrainFactory.GetGrain<ITimerRequestGrain>(GetRandomGrainId());

            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            var numTimers = await grain.TestAllTimerOverloads();
            var waitForTimers = timerObserver.WaitForTickCountAsync(grain, numTimers, TimerDiagnosticTimeout);
            await fixture.AdvanceTimeAsync(TimerOverloadDueTime);
            await waitForTimers;
            await grain.TestCompletedTimerResults();
        }

        /// <summary>
        /// 测试定时器能否在自身回调中安全地释放自身。
        /// 验证自行释放不会导致死锁或异常，
        /// 这对于一次性定时器模式或需要根据条件
        /// 取消自身的定时器很重要。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task GrainTimer_DisposeFromCallback()
        {
            // 安排一个会在自身回调中释放自身的定时器。
            var grain = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());
            await RunSelfDisposingTimerAsync(grain);

            var pocoGrain = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());
            await RunSelfDisposingTimerAsync(pocoGrain);
        }

        /// <summary>
        /// 测试不可重入 Grain 中的定时器行为。
        /// 验证不可重入 Grain 中的多个定时器遵守
        /// Grain 的并发约束：定时器回调不会重叠，
        /// 并与其他 Grain 方法串行执行，保持
        /// 单线程执行模型。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task NonReentrantGrainTimer_Test()
        {
            const string testName = "NonReentrantGrainTimer_Test";
            var delay = OneShotTimerDelay;

            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            var grain = GrainFactory.GetGrain<INonReentrantTimerCallGrain>(GetRandomGrainId());

            // 安排多个具有相同延迟的定时器
            await grain.StartTimer(testName, delay);
            await grain.StartTimer($"{testName}_1", delay);
            await grain.StartTimer($"{testName}_2", delay);

            // 等待定时器回调时调用一些不可交错执行的方法。
            var externalTicks = await DriveExternalTickUntilTimerTicks(grain, timerObserver, delay, expectedTimerTicks: 3);

            var tickCount = await grain.GetTickCount();

            Assert.Equal(3 + externalTicks, tickCount);

            var err = await grain.GetException();
            Assert.Null(err); // 定时器回调期间不应出现异常

            await grain.StopTimer(testName);
            await grain.StopTimer($"{testName}_1");
            await grain.StopTimer($"{testName}_2");
        }

        /// <summary>
        /// 测试创建定时器之后更改周期和到期时间。
        /// 验证 timer.Change() 能正确更新时间参数，
        /// 处理边界情况（无限、零、负值），并且
        /// 可以在定时器自身的回调中安全地修改定时器。
        /// 这对于自适应计时场景至关重要。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task GrainTimer_Change()
        {
            const string testName = nameof(GrainTimer_Change);
            TimeSpan delay = OneShotTimerDelay;

            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            var grain = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());

            await grain.StartTimer(testName, delay);

            await AdvanceTimerToTickCountAsync(grain, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);

            int tickCount = await grain.GetTickCount();
            Assert.Equal(1, tickCount);

            await grain.RestartTimer(testName, delay);

            await AdvanceTimerToTickCountAsync(grain, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 2);

            tickCount = await grain.GetTickCount();
            Assert.Equal(2, tickCount);

            await grain.TestTimerChangeArguments();

            Exception err = await grain.GetException();
            Assert.Null(err); // 定时器回调期间不应出现异常

            // 在定时器内部调用的有效操作：更新周期和释放定时器。
            var grain2 = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());
            await grain2.StartTimer(testName, delay, "update_period");
            await AdvanceTimerToTickCountAsync(grain2, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            Assert.Null(await grain2.GetException()); // 定时器回调期间不应出现异常
            Assert.Equal(1, await grain2.GetTickCount());

            var grain3 = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());
            await grain3.StartTimer(testName, delay, "dispose_timer");
            await AdvanceTimerToTickCountAsync(grain3, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            var grain3TickCount = await grain3.GetTickCount();

            Assert.Null(await grain3.GetException()); // 定时器回调期间不应出现异常
            Assert.Equal(1, grain3TickCount);

            await grain.StopTimer(testName);
        }

        /// <summary>
        /// 测试 POCO（普通 CLR 对象）Grain 的基本定时器功能。
        /// 验证定时器在 POCO Grain 中的行为与
        /// 传统的派生自 Grain 的类相同，展示定时器
        /// 基础设施独立于 Grain 的实现方式。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Basic_Poco()
        {
            for (int i = 0; i < 10; i++)
            {
                var grain = GrainFactory.GetGrain<IPocoTimerGrain>(GetRandomGrainId());
                var period = await grain.GetTimerPeriod();
                var last = await AdvanceDefaultTimerTicksAsync(grain, period);

                output.WriteLine("value = " + last);
                Assert.Equal(ExpectedDefaultTimerTicks, last);

                await grain.StopDefaultTimer();
                await fixture.AdvanceTimeAsync(period.Multiply(2));
                var curr = await grain.GetCounter();
                Assert.Equal(last, curr);
            }
        }

        /// <summary>
        /// 测试多个 POCO Grain 实例之间并行执行定时器。
        /// 验证 POCO Grain 与传统 Grain 一样保持定时器隔离，
        /// 每个实例都有独立的定时器，
        /// 彼此不干扰。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Parallel_Poco()
        {
            TimeSpan period = TimeSpan.Zero;
            List<IPocoTimerGrain> grains = new List<IPocoTimerGrain>();
            for (int i = 0; i < 10; i++)
            {
                IPocoTimerGrain grain = GrainFactory.GetGrain<IPocoTimerGrain>(GetRandomGrainId());
                grains.Add(grain);
                period = await grain.GetTimerPeriod(); // 激活 Grain
            }

            await AdvanceDefaultTimerTicksAsync(grains, period);
            for (int i = 0; i < grains.Count; i++)
            {
                IPocoTimerGrain grain = grains[i];
                var last = await grain.GetCounter();
                output.WriteLine("value = " + last);
                Assert.Equal(ExpectedDefaultTimerTicks, last);
                await grain.StopDefaultTimer();
            }
        }

        /// <summary>
        /// 测试 POCO Grain 停用及重新激活时的定时器行为。
        /// 验证 POCO Grain 与传统 Grain 具有相同的非持久化定时器
        /// 行为：停用时丢失定时器，
        /// 重新激活时重新开始。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Migration_Poco()
        {
            IPocoTimerGrain grain = GrainFactory.GetGrain<IPocoTimerGrain>(GetRandomGrainId());
            TimeSpan period = await grain.GetTimerPeriod();

            // 确保 Grain 正常工作。
            var last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);
            output.WriteLine("value = " + last);

            // 重新激活 Grain。
            await grain.Deactivate();
            last = await grain.GetCounter();
            Assert.True(last == 0, "Restarted grains should have zero ticks. Actual: " + last);
            period = await grain.GetTimerPeriod();

            // 调用 Grain 以确认其仍然正常工作。
            last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);

            output.WriteLine(
                "Virtual elapsed time = " + (period.Multiply(ExpectedDefaultTimerTicks).TotalSeconds) + " sec. Expected ticks = " + ExpectedDefaultTimerTicks +
                ". Actual ticks = " + last);
        }

        /// <summary>
        /// 测试在 POCO Grain 的定时器回调中发起异步 Grain 调用。
        /// 验证 POCO Grain 的定时器可以执行异步操作，
        /// 包括 Grain 间调用，并在定时器回调上下文中
        /// 正确处理异常。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task AsyncTimerTest_GrainCall_Poco()
        {
            const string testName = "AsyncTimerTest_GrainCall";
            TimeSpan delay = OneShotTimerDelay;

            IPocoTimerCallGrain? grain = null;

            Exception? error = null;
            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            try
            {
                grain = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());

                await grain.StartTimer(testName, delay);

                await AdvanceTimerToTickCountAsync(grain, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);

                int tickCount = await grain.GetTickCount();
                Assert.Equal(1, tickCount);

                Exception? err = await grain.GetException();
                Assert.Null(err); // 定时器回调期间不应出现异常
            }
            catch (Exception exc)
            {
                output.WriteLine(exc);
                error = exc;
            }

            try
            {
                if (grain != null) await grain.StopTimer(testName);
            }
            catch (Exception exc)
            {
                // 忽略。
                output.WriteLine("Ignoring exception from StopTimer : {0}", exc);
            }

            if (error != null)
            {
                Assert.Fail($"Test {testName} failed with error {error}");
            }
        }

        /// <summary>
        /// 测试 POCO Grain 的所有定时器注册重载。
        /// 确保 POCO Grain 支持完整的定时器注册方法，
        /// 保持与传统 Grain 实现的
        /// API 兼容性。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task GrainTimer_TestAllOverloads_Poco()
        {
            var grain = GrainFactory.GetGrain<IPocoTimerRequestGrain>(GetRandomGrainId());

            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            var numTimers = await grain.TestAllTimerOverloads();
            var waitForTimers = timerObserver.WaitForTickCountAsync(grain, numTimers, TimerDiagnosticTimeout);
            await fixture.AdvanceTimeAsync(TimerOverloadDueTime);
            await waitForTimers;
            await grain.TestCompletedTimerResults();
        }

        /// <summary>
        /// 测试不可重入 POCO Grain 的定时器并发约束。
        /// 验证 POCO Grain 遵守可重入性设置，确保
        /// 不可重入 Grain 中的定时器回调被正确串行执行，
        /// 而不受 Grain 实现方式影响。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task NonReentrantGrainTimer_Test_Poco()
        {
            const string testName = "NonReentrantGrainTimer_Test";
            var delay = OneShotTimerDelay;

            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            var grain = GrainFactory.GetGrain<IPocoNonReentrantTimerCallGrain>(GetRandomGrainId());

            // 安排多个具有相同延迟的定时器
            await grain.StartTimer(testName, delay);
            await grain.StartTimer($"{testName}_1", delay);
            await grain.StartTimer($"{testName}_2", delay);

            // 等待定时器回调时调用一些不可交错执行的方法。
            var externalTicks = await DriveExternalTickUntilTimerTicks(grain, timerObserver, delay, expectedTimerTicks: 3);

            var tickCount = await grain.GetTickCount();

            Assert.Equal(3 + externalTicks, tickCount);

            var err = await grain.GetException();
            Assert.Null(err); // 定时器回调期间不应出现异常

            await grain.StopTimer(testName);
            await grain.StopTimer($"{testName}_1");
            await grain.StopTimer($"{testName}_2");
        }

        /// <summary>
        /// 测试 POCO Grain 的定时器周期动态变更。
        /// 验证 POCO Grain 的定时器支持在运行时通过 Change 方法修改
        /// 时间参数，包括边界情况及
        /// 回调内发起的变更。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task GrainTimer_Change_Poco()
        {
            const string testName = nameof(GrainTimer_Change);
            TimeSpan delay = OneShotTimerDelay;

            using var timerObserver = TimerDiagnosticObserver.Create(fixture.HostedCluster);
            var grain = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());

            await grain.StartTimer(testName, delay);

            await AdvanceTimerToTickCountAsync(grain, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);

            int tickCount = await grain.GetTickCount();
            Assert.Equal(1, tickCount);

            await grain.RestartTimer(testName, delay);

            await AdvanceTimerToTickCountAsync(grain, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 2);

            tickCount = await grain.GetTickCount();
            Assert.Equal(2, tickCount);

            await grain.TestTimerChangeArguments();

            Exception err = await grain.GetException();
            Assert.Null(err); // 定时器回调期间不应出现异常

            // 在定时器内部调用的有效操作：更新周期和释放定时器。
            var grain2 = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());
            await grain2.StartTimer(testName, delay, "update_period");
            await AdvanceTimerToTickCountAsync(grain2, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            Assert.Null(await grain2.GetException()); // 定时器回调期间不应出现异常
            Assert.Equal(1, await grain2.GetTickCount());

            var grain3 = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());
            await grain3.StartTimer(testName, delay, "dispose_timer");
            await AdvanceTimerToTickCountAsync(grain3, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            Assert.Null(await grain3.GetException()); // 定时器回调期间不应出现异常
            Assert.Equal(1, await grain3.GetTickCount());

            await grain.StopTimer(testName);
        }
    }
}
