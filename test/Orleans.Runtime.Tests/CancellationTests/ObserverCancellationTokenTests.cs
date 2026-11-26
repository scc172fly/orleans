#nullable enable
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.CancellationTests;

/// <summary>
/// 观察员测试 CancellationToken 功能, 启用确认等待 。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public sealed class ObserverCancellationTokenTests_WaitForAcknowledgement(ObserverCancellationTokenTests_WaitForAcknowledgement.Fixture fixture)
    : ObserverCancellationTokenTests(fixture), IClassFixture<ObserverCancellationTokenTests_WaitForAcknowledgement.Fixture>
{
    public sealed class Fixture : FixtureBase
    {
        public override bool WaitForCancellationAcknowledgement => true;
    }
}

/// <summary>
/// 观察员 CancellationToken 功能测试,确认等待识别障碍。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public sealed class ObserverCancellationTokenTests_NoWaitForAcknowledgement(ObserverCancellationTokenTests_NoWaitForAcknowledgement.Fixture fixture)
    : ObserverCancellationTokenTests(fixture), IClassFixture<ObserverCancellationTokenTests_NoWaitForAcknowledgement.Fixture>
{
    public sealed class Fixture : FixtureBase
    {
        public override bool WaitForCancellationAcknowledgement => false;
    }
}

/// <summary>
/// 观察员(当地物体)的传播和处理测试 CancellationToken 基准等级。
/// </summary>
public abstract class ObserverCancellationTokenTests(ObserverCancellationTokenTests.FixtureBase fixture)
{
    public abstract class FixtureBase : BaseInProcessTestClusterFixture
    {
        public abstract bool WaitForCancellationAcknowledgement { get; }

        protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
        {
            base.ConfigureTestCluster(builder);
            builder.ConfigureHost(hostBuilder => hostBuilder.Logging.SetMinimumLevel(LogLevel.Debug));
            builder.ConfigureSilo((options, siloBuilder) =>
            {
                siloBuilder.Configure<SiloMessagingOptions>(options =>
                {
                    options.WaitForCancellationAcknowledgement = WaitForCancellationAcknowledgement;
                });
            });

            builder.ConfigureClient(clientBuilder =>
            {
                clientBuilder.Configure<ClientMessagingOptions>(options =>
                {
                    options.WaitForCancellationAcknowledgement = WaitForCancellationAcknowledgement;
                });
            });
        }
    }

    /// <summary>
    /// 测试正在运行的观察员操作可以通过 CancellationToken取消。
    /// </summary>
    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ObserverTaskCancellation(bool cancelImmediately)
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        try
        {
            await grain.Subscribe(reference);

            using var cts = new CancellationTokenSource();
            var callId = Guid.NewGuid();
            var grainTask = grain.NotifyLongWait(TimeSpan.FromSeconds(10), callId, cts.Token);

            if (cancelImmediately)
            {
                await cts.CancelAsync();
            }
            else
            {
                await observer.WaitForCallToStart(callId);
                await cts.CancelAsync();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grainTask);
            if (!cancelImmediately)
            {
                await observer.WaitForCancellation(callId);
            }
        }
        finally
        {
            await grain.Unsubscribe(reference);
            fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
            GC.KeepAlive(observer);
        }
    }

    /// <summary>
    /// 测试在调用观察者时,预取消的牌子是否得到妥善处理。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task PreCancelledTokenPassing()
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        await grain.Subscribe(reference);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // 期待在牌标已被取消时被抛出 OperationCanceledException
        Assert.Throws<OperationCanceledException>(() =>
            grain.NotifyLongWait(TimeSpan.FromSeconds(10), Guid.Empty, cts.Token).Ignore());

        await grain.Unsubscribe(reference);
        fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
        GC.KeepAlive(observer);
    }

    /// <summary>
    /// 测试通过一个不取消的 CancellationToken 不丢弃。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task TokenPassingWithoutCancellation_NoExceptionShouldBeThrown()
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        await grain.Subscribe(reference);

        using var cts = new CancellationTokenSource();
        try
        {
            await grain.NotifyLongWait(TimeSpan.FromMilliseconds(1), Guid.Empty, cts.Token);
        }
        catch (Exception ex)
        {
            Assert.Fail("Expected no exception, but got: " + ex.Message);
        }

        await grain.Unsubscribe(reference);
        fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
        GC.KeepAlive(observer);
    }

    /// <summary>
    /// 测试取消象征性回召在观察员正确执行的情况下执行。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task CancellationTokenCallbacksExecutionContext()
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        await grain.Subscribe(reference);

        using var cts = new CancellationTokenSource();
        var callId = Guid.NewGuid();
        var grainTask = grain.NotifyCancellationTokenCallbackResolve(callId, cts.Token);

        await observer.WaitForCallToStart(callId);
        await cts.CancelAsync();

        if (fixture.WaitForCancellationAcknowledgement)
        {
            var result = await grainTask;
            Assert.True(result);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grainTask);
        }

        await observer.WaitForCancellation(callId);
        await grain.Unsubscribe(reference);
        fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
        GC.KeepAlive(observer);
    }

    /// <summary>
    /// 当多个观察员被签署并全部获得取消时,测试即被取消。
    /// </summary>
    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultipleObserversCancellation(bool cancelImmediately)
    {
        // 创建多个 grains , 每个都使用观察者
        using var cts = new CancellationTokenSource();
        var grains = new List<(IObserverWithCancellationGrain Grain, LongRunningObserver Observer, ILongRunningObserver Reference, Guid CallId)>();
        try
        {
            for (int i = 0; i < 5; i++)
            {
                var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
                var observer = new LongRunningObserver();
                var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
                grains.Add((grain, observer, reference, Guid.NewGuid()));
                await grain.Subscribe(reference);
            }

            var notifyTasks = grains
                .Select(g => g.Grain.NotifyLongWait(TimeSpan.FromSeconds(10), g.CallId, cts.Token))
                .ToList();

            if (cancelImmediately)
            {
                await cts.CancelAsync();
            }
            else
            {
                await Task.WhenAll(grains.Select(g => g.Observer.WaitForCallToStart(g.CallId)));
                await cts.CancelAsync();
            }

            foreach (var task in notifyTasks)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }

            if (!cancelImmediately)
            {
                for (int i = 0; i < grains.Count; i++)
                {
                    await grains[i].Observer.WaitForCancellation(grains[i].CallId);
                }
            }
        }
        finally
        {
            foreach (var g in grains)
            {
                await g.Grain.Unsubscribe(g.Reference);
            }
            foreach (var g in grains)
            {
                fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(g.Reference);
                GC.KeepAlive(g.Observer);
            }
        }
    }

    /// <summary>
    /// 测试观察员取消等候(排队)请求是否得到正确处理。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task CancelWaitingRequest()
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        await grain.Subscribe(reference);

        // 启动一个长期请求, 将阻塞观察者
        using var blockingCts = new CancellationTokenSource();
        var blockingCallId = Guid.NewGuid();
        var blockingTask = grain.NotifyLongWait(TimeSpan.FromSeconds(30), blockingCallId, blockingCts.Token);

        // 等待屏蔽请求启动
        await observer.WaitForCallToStart(blockingCallId);

        // 启动将排队的另一项请求
        using var queuedCts = new CancellationTokenSource();
        var queuedCallId = Guid.NewGuid();
        var queuedTask = grain.NotifyLongWait(TimeSpan.FromSeconds(10), queuedCallId, queuedCts.Token);

        // 取消队列后排的请求
        await queuedCts.CancelAsync();

        // 队列任务应该丢弃 OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedTask);

        // 取消清除阻屏请求
        await blockingCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockingTask);
        await observer.WaitForCancellation(blockingCallId);

        await grain.Unsubscribe(reference);
        fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
        GC.KeepAlive(observer);
    }

    /// <summary>
    /// 测试运行中的中断端观察操作可以通过 CancellationToken取消。
    /// 互接请求同时进行,不排队,也应可以撤销。
    /// </summary>
    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterleavingObserverTaskCancellation(bool cancelImmediately)
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        try
        {
            await grain.Subscribe(reference);

            using var cts = new CancellationTokenSource();
            var callId = Guid.NewGuid();
            var grainTask = grain.NotifyInterleavingLongWait(TimeSpan.FromSeconds(10), callId, cts.Token);

            if (cancelImmediately)
            {
                await cts.CancelAsync();
            }
            else
            {
                await observer.WaitForCallToStart(callId);
                await cts.CancelAsync();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grainTask);
            if (!cancelImmediately)
            {
                await observer.WaitForCancellation(callId);
            }
        }
        finally
        {
            await grain.Unsubscribe(reference);
            fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
            GC.KeepAlive(observer);
        }
    }

    /// <summary>
    /// 测试多个同时连接的请求可以独立取消。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task MultipleInterleavingRequestsCancellation()
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        await grain.Subscribe(reference);

        // 同时启动多个互连请求
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        var callId1 = Guid.NewGuid();
        var callId2 = Guid.NewGuid();
        var callId3 = Guid.NewGuid();

        var task1 = grain.NotifyInterleavingLongWait(TimeSpan.FromSeconds(10), callId1, cts1.Token);
        var task2 = grain.NotifyInterleavingLongWait(TimeSpan.FromSeconds(10), callId2, cts2.Token);
        var task3 = grain.NotifyInterleavingLongWait(TimeSpan.FromSeconds(10), callId3, cts3.Token);

        // 等待所有运行
        await Task.WhenAll(
            observer.WaitForCallToStart(callId1),
            observer.WaitForCallToStart(callId2),
            observer.WaitForCallToStart(callId3));

        // 只取消第二个请求
        await cts2.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task2);
        await observer.WaitForCancellation(callId2);

        // 第一和第二个应该还在运行,取消
        await cts1.CancelAsync();
        await cts3.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task3);

        await observer.WaitForCancellation(callId1);
        await observer.WaitForCancellation(callId3);

        await grain.Unsubscribe(reference);
        fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
        GC.KeepAlive(observer);
    }

    /// <summary>
    /// 测试在正常请求同时运行时, 中断请求可以取消 。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task CancelInterleavingWhileRegularRequestRunning()
    {
        var grain = fixture.GrainFactory.GetGrain<IObserverWithCancellationGrain>(Guid.NewGuid());
        var observer = new LongRunningObserver();
        var reference = fixture.GrainFactory.CreateObjectReference<ILongRunningObserver>(observer);
        await grain.Subscribe(reference);

        // 启动常规( 非inter-) 长期请求
        using var regularCts = new CancellationTokenSource();
        var regularCallId = Guid.NewGuid();
        var regularTask = grain.NotifyLongWait(TimeSpan.FromSeconds(30), regularCallId, regularCts.Token);

        // 等待正常请求开始
        await observer.WaitForCallToStart(regularCallId);

        // 启动一个连接请求( 此请求应同时运行)
        using var interleavingCts = new CancellationTokenSource();
        var interleavingCallId = Guid.NewGuid();
        var interleavingTask = grain.NotifyInterleavingLongWait(TimeSpan.FromSeconds(10), interleavingCallId, interleavingCts.Token);

        // 等待中断请求启动
        await observer.WaitForCallToStart(interleavingCallId);

        // 取消间断请求
        await interleavingCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interleavingTask);
        await observer.WaitForCancellation(interleavingCallId);

        // 清理清理 - 取消常规请求
        await regularCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => regularTask);
        await observer.WaitForCancellation(regularCallId);

        await grain.Unsubscribe(reference);
        fixture.GrainFactory.DeleteObjectReference<ILongRunningObserver>(reference);
        GC.KeepAlive(observer);
    }

    /// <summary>
    /// 客户方观察员执行,以测试注销的长期业务。
    /// </summary>
    private sealed class LongRunningObserver : ILongRunningObserver
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _callStartedTcs = new();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _callCancelledTcs = new();

        /// <inheritdoc />
        public async Task LongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken)
        {
            var startedTcs = _callStartedTcs.GetOrAdd(callId, _ => new TaskCompletionSource());
            var cancelledTcs = _callCancelledTcs.GetOrAdd(callId, _ => new TaskCompletionSource());

            startedTcs.TrySetResult();

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelledTcs.TrySetResult();
                throw;
            }
        }

        /// <inheritdoc />
        public Task<bool> CancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken)
        {
            var startedTcs = _callStartedTcs.GetOrAdd(callId, _ => new TaskCompletionSource());
            var cancelledTcs = _callCancelledTcs.GetOrAdd(callId, _ => new TaskCompletionSource());
            var resultTcs = new TaskCompletionSource<bool>();

            startedTcs.TrySetResult();

            cancellationToken.Register(() =>
            {
                cancelledTcs.TrySetResult();
                resultTcs.TrySetResult(true);
            });

            return resultTcs.Task;
        }

        /// <inheritdoc />
        public async Task InterleavingLongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken)
        {
            var startedTcs = _callStartedTcs.GetOrAdd(callId, _ => new TaskCompletionSource());
            var cancelledTcs = _callCancelledTcs.GetOrAdd(callId, _ => new TaskCompletionSource());

            startedTcs.TrySetResult();

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelledTcs.TrySetResult();
                throw;
            }
        }

        /// <summary>
        /// 等待一个电话开始。
        /// </summary>
        public Task WaitForCallToStart(Guid callId)
        {
            var tcs = _callStartedTcs.GetOrAdd(callId, _ => new TaskCompletionSource());
            return tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// 等待取消呼叫 。
        /// </summary>
        public Task WaitForCancellation(Guid callId)
        {
            var tcs = _callCancelledTcs.GetOrAdd(callId, _ => new TaskCompletionSource());
            return tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
