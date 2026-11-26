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
/// SystemTarget CancellationToken 功能测试,启用确认等待。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public sealed class SystemTargetCancellationTokenTests_WaitForAcknowledgement(SystemTargetCancellationTokenTests_WaitForAcknowledgement.Fixture fixture)
    : SystemTargetCancellationTokenTests(fixture), IClassFixture<SystemTargetCancellationTokenTests_WaitForAcknowledgement.Fixture>
{
    public sealed class Fixture : FixtureBase
    {
        public override bool WaitForCancellationAcknowledgement => true;
    }
}

/// <summary>
/// 测试 SystemTarget CancellationToken 功能,确认等待状态禁用。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public sealed class SystemTargetCancellationTokenTests_NoWaitForAcknowledgement(SystemTargetCancellationTokenTests_NoWaitForAcknowledgement.Fixture fixture)
    : SystemTargetCancellationTokenTests(fixture), IClassFixture<SystemTargetCancellationTokenTests_NoWaitForAcknowledgement.Fixture>
{
    public sealed class Fixture : FixtureBase
    {
        public override bool WaitForCancellationAcknowledgement => false;
    }
}

/// <summary>
/// 测试 CancellationToken 传播和处理 SystemTargets的基础等级。
/// 注:所有 SystemTarget 电话默认是连接的,它们立即执行
/// 不排队, 不同于常规 grain 调用 。
/// </summary>
public abstract class SystemTargetCancellationTokenTests(SystemTargetCancellationTokenTests.FixtureBase fixture)
{
    /// <summary>
    /// 取消测试系统目标的 grain 类型。
    /// </summary>
    public static readonly GrainType CancellationTestSystemTargetType = SystemTargetGrainId.CreateGrainType("cancellation-test-target");

    public abstract class FixtureBase : BaseInProcessTestClusterFixture
    {
        public abstract bool WaitForCancellationAcknowledgement { get; }

        protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
        {
            base.ConfigureTestCluster(builder);
            builder.ConfigureHost(hostBuilder => hostBuilder.Logging.SetMinimumLevel(LogLevel.Debug));
            builder.ConfigureSilo((options, siloBuilder) =>
            {
                siloBuilder.Configure<SiloMessagingOptions>(opts =>
                {
                    opts.WaitForCancellationAcknowledgement = WaitForCancellationAcknowledgement;
                });

                // 登记 CancellationTestSystemTarget 作为生命周期参与方
                siloBuilder.Services.AddSingleton<CancellationTestSystemTarget>();
                siloBuilder.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(sp => sp.GetRequiredService<CancellationTestSystemTarget>());
            });

            builder.ConfigureClient(clientBuilder =>
            {
                clientBuilder.Configure<ClientMessagingOptions>(opts =>
                {
                    opts.WaitForCancellationAcknowledgement = WaitForCancellationAcknowledgement;
                });
            });
        }

        /// <summary>
        /// 获取引用 CancellationTestSystemTarget 指定 silo.
        /// </summary>
        public ICancellationTestSystemTarget GetSystemTarget(SiloAddress siloAddress)
        {
            return ((IInternalGrainFactory)GrainFactory).GetSystemTarget<ICancellationTestSystemTarget>(CancellationTestSystemTargetType, siloAddress);
        }
    }

    public bool WaitForCancellationAcknowledgement => fixture.WaitForCancellationAcknowledgement;

    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(300)]
    public async Task SystemTargetTaskCancellation(int delay)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        var systemTarget = fixture.GetSystemTarget(siloAddress);

        using var cts = new CancellationTokenSource();
        var callId = Guid.NewGuid();
        var task = systemTarget.LongWait(TimeSpan.FromSeconds(10), callId, cts.Token);
        cts.CancelAfter(delay);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        if (delay > 0)
        {
            await WaitForCallCancellation(systemTarget, callId, cancellationToken);
        }
    }

    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(300)]
    public async Task MultipleSystemTargetsTaskCancellation(int delay)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        var callId = Guid.NewGuid();
        var systemTargets = fixture.HostedCluster.Silos
            .Select(s => fixture.GetSystemTarget(s.SiloAddress))
            .ToList();

        var tasks = systemTargets.Select(st =>
            Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                st.LongWait(TimeSpan.FromSeconds(10), callId, cts.Token)))
            .ToList();

        cts.CancelAfter(delay);
        await Task.WhenAll(tasks);
        if (delay > 0)
        {
            foreach (var st in systemTargets)
            {
                await WaitForCallCancellation(st, callId, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 测试同一 SystemTarget上多次同时取消。
    /// 毕竟 SystemTarget 电话在连接,他们同时运行
    /// </summary>
    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(300)]
    public async Task SystemTargetMultipleConcurrentCancellations(int delay)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        var systemTarget = fixture.GetSystemTarget(siloAddress);

        var callIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var tasks = callIds
            .Select(async callId =>
            {
                using var cts = new CancellationTokenSource();
                // 所有这些电话都是同时发生的,因为 SystemTarget 通话是相互交错的
                var task = systemTarget.LongWait(TimeSpan.FromSeconds(10), callId, cts.Token);
                cts.CancelAfter(delay);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            })
            .ToList();

        await Task.WhenAll(tasks);
        if (delay > 0)
        {
            foreach (var callId in callIds)
            {
                await WaitForCallCancellation(systemTarget, callId, cancellationToken);
            }
        }
    }

    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task TokenPassingWithoutCancellation_NoExceptionShouldBeThrown()
    {
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        var systemTarget = fixture.GetSystemTarget(siloAddress);

        using var cts = new CancellationTokenSource();
        try
        {
            await systemTarget.LongWait(TimeSpan.FromMilliseconds(1), Guid.Empty, cts.Token);
        }
        catch (Exception ex)
        {
            Assert.Fail("Expected no exception, but got: " + ex.Message);
        }
    }

    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task PreCancelledTokenPassing()
    {
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        var systemTarget = fixture.GetSystemTarget(siloAddress);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // 期待在牌标已被取消时被抛出 OperationCanceledException
        Assert.Throws<OperationCanceledException>(() =>
            systemTarget.LongWait(TimeSpan.FromSeconds(10), Guid.Empty, cts.Token).Ignore());
    }

    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task CancellationTokenCallbacksExecutionContext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        var systemTarget = fixture.GetSystemTarget(siloAddress);

        using var cts = new CancellationTokenSource();
        var callId = Guid.NewGuid();
        var task = systemTarget.CancellationTokenCallbackResolve(callId, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        if (WaitForCancellationAcknowledgement)
        {
            var result = await task;
            Assert.True(result);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }

        await WaitForCallCancellation(systemTarget, callId, cancellationToken);
    }

    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task CancellationTokenCallbacksTaskSchedulerContext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var silos = fixture.HostedCluster.Silos.ToArray();
        var sourceTarget = fixture.GetSystemTarget(silos[0].SiloAddress);
        var destinationTarget = fixture.GetSystemTarget(silos.Length > 1 ? silos[1].SiloAddress : silos[0].SiloAddress);

        var callId = Guid.NewGuid();
        var task = sourceTarget.CallOtherCancellationTokenCallbackResolve(destinationTarget, callId);

        if (WaitForCancellationAcknowledgement)
        {
            var result = await task;
            Assert.True(result);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }

        await WaitForCallCancellation(destinationTarget, callId, cancellationToken);
    }

    [Fact, TestCategory("Cancellation")]
    public async Task CancellationTokenCallbacksThrow_ExceptionDoesNotPropagate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        var systemTarget = fixture.GetSystemTarget(siloAddress);

        using var cts = new CancellationTokenSource();
        var callId = Guid.NewGuid();
        systemTarget.CancellationTokenCallbackThrow(callId, cts.Token).Ignore();
        // 取消是一个合作机制 所以我们不期望例外会传播
        cts.CancelAfter(100);
        await WaitForCallCancellation(systemTarget, callId, cancellationToken);
    }

    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(300)]
    public async Task InSiloCancellation(int delay)
    {
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        await CancellationTestCore(siloAddress, siloAddress, delay, TestContext.Current.CancellationToken);
    }

    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(300)]
    public async Task InterSiloCancellation(int delay)
    {
        var silos = fixture.HostedCluster.Silos.ToArray();
        if (silos.Length < 2)
        {
            // 如果只有一个 silo,跳过测试
            return;
        }

        await CancellationTestCore(silos[0].SiloAddress, silos[1].SiloAddress, delay, TestContext.Current.CancellationToken);
    }

    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(300)]
    public async Task InterSiloClientCancellationTokenPassing(int delay)
    {
        var silos = fixture.HostedCluster.Silos.ToArray();
        if (silos.Length < 2)
        {
            // 如果只有一个 silo,跳过测试
            return;
        }

        await ClientCancellationTokenPassing(
            delay,
            silos[0].SiloAddress,
            silos[1].SiloAddress,
            TestContext.Current.CancellationToken);
    }

    [Theory, TestCategory("BVT"), TestCategory("Cancellation")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(300)]
    public async Task InSiloClientCancellationTokenPassing(int delay)
    {
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        await ClientCancellationTokenPassing(delay, siloAddress, siloAddress, TestContext.Current.CancellationToken);
    }

    private async Task ClientCancellationTokenPassing(
        int delay,
        SiloAddress sourceAddress,
        SiloAddress targetAddress,
        CancellationToken cancellationToken)
    {
        var sourceTarget = fixture.GetSystemTarget(sourceAddress);
        var target = fixture.GetSystemTarget(targetAddress);

        using var cts = new CancellationTokenSource();
        var callId = Guid.NewGuid();
        var task = sourceTarget.CallOtherLongRunningTask(target, TimeSpan.FromSeconds(10), callId, cts.Token);
        cts.CancelAfter(delay);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        if (delay > 0)
        {
            await WaitForCallCancellation(target, callId, cancellationToken);
        }
    }

    private async Task CancellationTestCore(
        SiloAddress sourceAddress,
        SiloAddress targetAddress,
        int delay,
        CancellationToken cancellationToken)
    {
        var sourceTarget = fixture.GetSystemTarget(sourceAddress);
        var target = fixture.GetSystemTarget(targetAddress);

        var callId = Guid.NewGuid();
        var task = sourceTarget.CallOtherLongRunningTaskWithLocalCancellation(target, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(delay), callId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        if (delay > 0)
        {
            await WaitForCallCancellation(target, callId, cancellationToken);
        }
    }

    private static async Task WaitForCallCancellation(
        ICancellationTestSystemTarget systemTarget,
        Guid callId,
        CancellationToken cancellationToken)
    {
        var (wasCancelled, error) = await systemTarget
            .WaitForCancellation(callId, TimeSpan.FromSeconds(30))
            .WaitAsync(cancellationToken);
        if (!wasCancelled)
        {
            Assert.Fail($"Did not encounter the expected call id {callId}");
        }

        if (error is not null)
        {
            throw new Exception("Expected no error, but found an error", error);
        }
    }

    /// <summary>
    /// 测试多个同时提出的请求可独立取消。
    /// 毕竟 SystemTarget 电话在连接,他们同时执行
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("Cancellation")]
    public async Task MultipleConcurrentSystemTargetRequestsCancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var siloAddress = fixture.HostedCluster.Silos.First().SiloAddress;
        var systemTarget = fixture.GetSystemTarget(siloAddress);

        // 启动多个同时提出请求( 所有 SystemTarget 电话都在中断)
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        var callId1 = Guid.NewGuid();
        var callId2 = Guid.NewGuid();
        var callId3 = Guid.NewGuid();

        var task1 = systemTarget.LongWait(TimeSpan.FromSeconds(10), callId1, cts1.Token);
        var task2 = systemTarget.LongWait(TimeSpan.FromSeconds(10), callId2, cts2.Token);
        var task3 = systemTarget.LongWait(TimeSpan.FromSeconds(10), callId3, cts3.Token);

        // 等待所有运行
        await Task.Delay(100, cancellationToken);

        // 只取消第二个请求
        await cts2.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task2);
        await WaitForCallCancellation(systemTarget, callId2, cancellationToken);

        // 第一和第二个应该还在运行,取消
        await cts1.CancelAsync();
        await cts3.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task3);

        await WaitForCallCancellation(systemTarget, callId1, cancellationToken);
        await WaitForCallCancellation(systemTarget, callId3, cancellationToken);
    }
}

/// <summary>
/// A SystemTarget 测试应用 CancellationToken 功能。
/// 注:所有 SystemTarget 电话都在相互交错中,它们不排队即刻执行。
/// </summary>
internal sealed class CancellationTestSystemTarget : SystemTarget, ICancellationTestSystemTarget, ILifecycleParticipant<ISiloLifecycle>
{
    private readonly ConcurrentDictionary<Guid, (TaskCompletionSource<bool> Tcs, Exception? Error)> _cancelledCalls = new();
    private readonly ILocalSiloDetails _localSiloDetails;

    public CancellationTestSystemTarget(
        ILocalSiloDetails localSiloDetails,
        SystemTargetShared shared)
        : base(SystemTargetCancellationTokenTests.CancellationTestSystemTargetType, shared)
    {
        _localSiloDetails = localSiloDetails;
        // 使用激活目录注册,以便接收信件
        shared.ActivationDirectory.RecordNewTarget(this);
    }

    void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
    {
        // 无需生命周期登记 -- -- 我们在建筑商注册
    }

    /// <inheritdoc />
    public Task<string> GetRuntimeInstanceId()
    {
        return Task.FromResult(_localSiloDetails.SiloAddress.ToString());
    }

    /// <inheritdoc />
    public async Task LongWait(TimeSpan delay, Guid callId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RecordCancellation(callId, null);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CallOtherLongRunningTask(ICancellationTestSystemTarget target, TimeSpan delay, Guid callId, CancellationToken cancellationToken)
    {
        await target.LongWait(delay, callId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CallOtherLongRunningTaskWithLocalCancellation(ICancellationTestSystemTarget target, TimeSpan delay, TimeSpan delayBeforeCancel, Guid callId)
    {
        using var cts = new CancellationTokenSource();
        var task = target.LongWait(delay, callId, cts.Token);
        cts.CancelAfter(delayBeforeCancel);
        await task;
    }

    /// <inheritdoc />
    public Task<bool> CancellationTokenCallbackResolve(Guid callId, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        var orleansTs = TaskScheduler.Current;
        cancellationToken.Register(() =>
        {
            if (TaskScheduler.Current != orleansTs)
            {
                var exception = new Exception("Callback executed on wrong thread");
                RecordCancellation(callId, exception);
                tcs.SetException(exception);
            }
            else
            {
                RecordCancellation(callId, null);
                tcs.SetResult(true);
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc />
    public async Task<bool> CallOtherCancellationTokenCallbackResolve(ICancellationTestSystemTarget target, Guid callId)
    {
        using var cts = new CancellationTokenSource();
        var task = target.CancellationTokenCallbackResolve(callId, cts.Token);
        cts.CancelAfter(300);
        return await task;
    }

    /// <inheritdoc />
    public async Task CancellationTokenCallbackThrow(Guid callId, CancellationToken cancellationToken)
    {
        cancellationToken.Register(() =>
        {
            RecordCancellation(callId, null);
            throw new InvalidOperationException("From cancellation token callback");
        });

        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> WasCallCancelled(Guid callId)
    {
        return Task.FromResult(_cancelledCalls.ContainsKey(callId));
    }

    /// <inheritdoc />
    public async Task<(bool WasCancelled, Exception? Error)> WaitForCancellation(Guid callId, TimeSpan timeout)
    {
        var entry = _cancelledCalls.GetOrAdd(callId, _ => (new TaskCompletionSource<bool>(), null));

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await entry.Tcs.Task.WaitAsync(cts.Token);
            return (true, _cancelledCalls.TryGetValue(callId, out var result) ? result.Error : null);
        }
        catch (OperationCanceledException)
        {
            return (false, null);
        }
    }

    private readonly object _cancelledCallsLock = new();

    private void RecordCancellation(Guid callId, Exception? error)
    {
        var entry = _cancelledCalls.GetOrAdd(callId, _ => (new TaskCompletionSource<bool>(), error));
        if (error is not null)
        {
            lock (_cancelledCallsLock)
            {
                // 重新检查锁内以确保原子化
                var currentEntry = _cancelledCalls.GetOrAdd(callId, _ => (entry.Tcs, error));
                if (currentEntry.Error is null)
                {
                    _cancelledCalls[callId] = (entry.Tcs, error);
                }
            }
        }

        entry.Tcs.TrySetResult(true);
    }
}
