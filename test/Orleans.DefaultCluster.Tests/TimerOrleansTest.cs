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
    /// 测试 Orleans 计时器功能。
    /// 计时器提供 grain- 本地的、不可耐用定期回调。与提醒不同,
    /// 时间计仅在一个 grain 被激活且不持续时时才有效
    /// 它们是短寿命定期任务、投票、
    /// 以及不需要持久性的其他假设
    /// 高频操作的计时器比提醒更有效率。
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
        /// 测试基本的定时器功能,包括启动和停止操作。
        /// 证明定时器按预期间隔计时,可以停止;
        /// 并且阻止定时器,以阻断进一步的勾点。这显示
        /// 启动时 grain 范围内的初始定时器生命周期。
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
        /// 测试多个 grains ,且计时器平行运行。
        /// 证明 grain 保持自己的独立计时器
        /// 不同 grain 激活的定时器不干扰
        /// 显示每个 grain的计时器隔离。
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
                period = await grain.GetTimerPeriod(); // 激活 grains
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
        /// 测试 grain 停止和重新启动的全程时间行为。
        /// 证明计时器不会持续整个 grain 生命周期,当一个 grain周期
        /// 停止使用和重新启动,计时器开始新时间。
        /// 与提醒相比,计时器不可耐久的性质。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Migration()
        {
            ITimerGrain grain = GrainFactory.GetGrain<ITimerGrain>(GetRandomGrainId());
            TimeSpan period = await grain.GetTimerPeriod();

            // 确保 grain 按其应有的效果运作。
            var last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);
            output.WriteLine("value = " + last);

            // 重新启动 grain。
            await grain.Deactivate();
            last = await grain.GetCounter();
            Assert.True(last == 0, "Restarted grains should have zero ticks. Actual: " + last);
            period = await grain.GetTimerPeriod();

            // 点燃 grain ,确保它仍然能正常工作。
            last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);

            output.WriteLine(
                "Virtual elapsed time = " + (period.Multiply(ExpectedDefaultTimerTicks).TotalSeconds) + " sec. Expected ticks = " + ExpectedDefaultTimerTicks +
                ". Actual ticks = " + last);
        }

        /// <summary>
        /// 测试使非同步化的计时器 grain 呼唤着他们的回击
        /// 证明时器回调可进行 grain- -grain 通信
        /// 并且时间回调的例外被捕获,不崩溃
        /// 排 grain。对于基于定时器的管弦模式很重要。
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
                Assert.Null(err); // 在调时回调期间不应有任何例外
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
                // 忽略忽略
                output.WriteLine("Ignoring exception from StopTimer : {0}", exc);
            }

            if (error != null)
            {
                Assert.Fail($"Test {testName} failed with error {error}");
            }
        }

        /// <summary>
        /// 测试所有时间器创造超载及其行为
        /// 证明不同的计时器登记方法(有/无状态,
        /// 不同时期规格)工作正确,且所有定时器
        /// 变量执行预期的回调。
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
        /// 测试定时器能安全地 摆脱自己的回击
        /// 证明自我处置不会造成僵局或例外
        /// 这对于一瞬时针或
        /// 需要根据条件注销。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task GrainTimer_DisposeFromCallback()
        {
            // 计划一个自调时便处置自己的计时器。
            var grain = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());
            await RunSelfDisposingTimerAsync(grain);

            var pocoGrain = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());
            await RunSelfDisposingTimerAsync(pocoGrain);
        }

        /// <summary>
        /// 测试非返回时的计时行为 grains。
        /// 证明在非返回状态 grain 中多次计时器
        /// grain的同值货币约束 - 计时回调不重叠
        /// 并与其他 grain 方法相序列,以保持
        /// 单行执行模型。
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

            // 排队多个时间计时器, 同一延迟
            await grain.StartTimer(testName, delay);
            await grain.StartTimer($"{testName}_1", delay);
            await grain.StartTimer($"{testName}_2", delay);

            // 开始一些非互连方法, 等待计时器回调 。
            var externalTicks = await DriveExternalTickUntilTimerTicks(grain, timerObserver, delay, expectedTimerTicks: 3);

            var tickCount = await grain.GetTickCount();

            Assert.Equal(3 + externalTicks, tickCount);

            var err = await grain.GetException();
            Assert.Null(err); // 在调时回调期间不应有任何例外

            await grain.StopTimer(testName);
            await grain.StopTimer($"{testName}_1");
            await grain.StopTimer($"{testName}_2");
        }

        /// <summary>
        /// 测试变化的时段和创建后的到期时间。
        /// 证明 timer.Change() 正确更新了时间参数,
        /// 处理边缘情况(无限、零、负值),并且
        /// 时间器可以在自己的回调中安全地修改。
        /// 适应性时间安排情景所必不可少的。
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
            Assert.Null(err); // 在调时回调期间不应有任何例外

            // 定时器内调用的有效操作:更新周期并处理计时器。
            var grain2 = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());
            await grain2.StartTimer(testName, delay, "update_period");
            await AdvanceTimerToTickCountAsync(grain2, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            Assert.Null(await grain2.GetException()); // 在调时回调期间不应有任何例外
            Assert.Equal(1, await grain2.GetTickCount());

            var grain3 = GrainFactory.GetGrain<ITimerCallGrain>(GetRandomGrainId());
            await grain3.StartTimer(testName, delay, "dispose_timer");
            await AdvanceTimerToTickCountAsync(grain3, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            var grain3TickCount = await grain3.GetTickCount();

            Assert.Null(await grain3.GetException()); // 在调时回调期间不应有任何例外
            Assert.Equal(1, grain3TickCount);

            await grain.StopTimer(testName);
        }

        /// <summary>
        /// 与 POCO(Plain Old CLR 对象) grains测试基本定时器功能。
        /// 证明计时器与PCO grains 的同值工作
        /// Grain-衍生类别,表明计时器
        /// 基础设施独立于 grain 执行样式。
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
        /// 在多个 POCO grain 实例中测试平行计时器执行。
        /// 证明POCO grains 保持时计隔离状态,就像
        /// grains,每个情况都有独立的计时员
        /// 互不干涉
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
                period = await grain.GetTimerPeriod(); // 激活 grains
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
        /// 测试整个POCO grain 解除/激活的计时行为。
        /// 证明POCO grains 显示同一非持久性定时器
        /// 传统 grains - 停止作用时丢失计时器
        /// 重新开始重新启动。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Timers")]
        public async Task TimerOrleansTest_Migration_Poco()
        {
            IPocoTimerGrain grain = GrainFactory.GetGrain<IPocoTimerGrain>(GetRandomGrainId());
            TimeSpan period = await grain.GetTimerPeriod();

            // 确保 grain 按其应有的效果运作。
            var last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);
            output.WriteLine("value = " + last);

            // 重新启动 grain。
            await grain.Deactivate();
            last = await grain.GetCounter();
            Assert.True(last == 0, "Restarted grains should have zero ticks. Actual: " + last);
            period = await grain.GetTimerPeriod();

            // 点燃 grain ,确保它仍然能正常工作。
            last = await AdvanceDefaultTimerTicksAsync(grain, period);
            Assert.Equal(ExpectedDefaultTimerTicks, last);

            output.WriteLine(
                "Virtual elapsed time = " + (period.Multiply(ExpectedDefaultTimerTicks).TotalSeconds) + " sec. Expected ticks = " + ExpectedDefaultTimerTicks +
                ". Actual ticks = " + last);
        }

        /// <summary>
        /// 试验不同步 grain 波哥公司打来电话 grain 时针回调
        /// 证明POCO grain 计时器能够进行同步操作
        /// 包括 grain-至-grain 电话,但适当处理除外
        /// 时针回调上下文。
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
                Assert.Null(err); // 在调时回调期间不应有任何例外
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
                // 忽略忽略
                output.WriteLine("Ignoring exception from StopTimer : {0}", exc);
            }

            if (error != null)
            {
                Assert.Fail($"Test {testName} failed with error {error}");
            }
        }

        /// <summary>
        /// 测试所有使用 POCO grains的计时器登记超载。
        /// 确保POCO grains 支持全范围计时器
        /// 与 API
        /// 传统 grain 执行。
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
        /// 测试非留存的极低汇率 grains中的时间计时器同货币限制。
        /// 证明POCO grains 尊重再入环境,确保
        /// 时间再回调在非返回时适当序列 grains
        /// 不论 grain 执行样式如何。
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

            // 排队多个时间计时器, 同一延迟
            await grain.StartTimer(testName, delay);
            await grain.StartTimer($"{testName}_1", delay);
            await grain.StartTimer($"{testName}_2", delay);

            // 开始一些非互连方法, 等待计时器回调 。
            var externalTicks = await DriveExternalTickUntilTimerTicks(grain, timerObserver, delay, expectedTimerTicks: 3);

            var tickCount = await grain.GetTickCount();

            Assert.Equal(3 + externalTicks, tickCount);

            var err = await grain.GetException();
            Assert.Null(err); // 在调时回调期间不应有任何例外

            await grain.StopTimer(testName);
            await grain.StopTimer($"{testName}_1");
            await grain.StopTimer($"{testName}_2");
        }

        /// <summary>
        /// 测试POCO grains中的动态定时周期变化。
        /// 证明 POCO grain 计时器支持运行时间的修改
        /// 通过“变化方法”的计时参数,包括
        /// 边缘情况以及回调引发的更改。
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
            Assert.Null(err); // 在调时回调期间不应有任何例外

            // 定时器内调用的有效操作:更新周期并处理计时器。
            var grain2 = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());
            await grain2.StartTimer(testName, delay, "update_period");
            await AdvanceTimerToTickCountAsync(grain2, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            Assert.Null(await grain2.GetException()); // 在调时回调期间不应有任何例外
            Assert.Equal(1, await grain2.GetTickCount());

            var grain3 = GrainFactory.GetGrain<IPocoTimerCallGrain>(GetRandomGrainId());
            await grain3.StartTimer(testName, delay, "dispose_timer");
            await AdvanceTimerToTickCountAsync(grain3, timerObserver, delay, TimerCallbackDelay, expectedTickCount: 1);
            Assert.Null(await grain3.GetException()); // 在调时回调期间不应有任何例外
            Assert.Equal(1, await grain3.GetTickCount());

            await grain.StopTimer(testName);
        }
    }
}
