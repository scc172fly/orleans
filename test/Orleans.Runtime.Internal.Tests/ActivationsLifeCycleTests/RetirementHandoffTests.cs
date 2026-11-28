using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.Metadata;
using Orleans.Placement;
using Orleans.Placement.Repartitioning;
using Orleans.Runtime;
using Orleans.Runtime.Diagnostics;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.Placement;
using Orleans.Runtime.Scheduler;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Session;
using Orleans.TestingHost;
using Orleans.TestingHost.Diagnostics;
using Xunit;

namespace UnitTests.ActivationsLifeCycleTests;

/// <summary>
/// 验证正在退役的 Silo 将具有排队请求的激活连同这些请求一起迁移，
/// 并且这些已迁移请求的调用方在退役 Silo 离开后仍继续等待。
/// </summary>
[TestSuite("BVT"), TestProvider("None"), TestCategory("BVT")]
public sealed class RetirementHandoffTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedRequest_MigratesWithActivation_AndSurvivesRetiringSiloDeparture(bool directoryOwnedByRetiringSilo)
    {
        var builder = new InProcessTestClusterBuilder(3);
        await using var cluster = builder.Build();
        await cluster.DeployAsync(Cancellation);
        await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(Cancellation);
        await cluster.WaitForClusterManifestToStabilizeAsync().WaitAsync(Cancellation);

        var retiring = cluster.Silos[1];
        var caller = cluster.Silos[0];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = SelectKey(grainFactory, caller, retiring, directoryOwnedByRetiringSilo);
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);

        RequestContext.Set(IPlacementDirector.PlacementHintKey, retiring.SiloAddress);
        try
        {
            Assert.Equal(retiring.SiloAddress, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            RequestContext.Remove(IPlacementDirector.PlacementHintKey);
        }

        // 占用激活，然后在正在运行的请求之后将一个请求入队。
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, grain.GetGrainId()) == 1);

        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => FindActivation(retiring, grain.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 排队请求在新激活上运行。将其阻塞至退役 Silo 离开，
        // 以便在调用未重定向时，调用方针对目标 Silo 的故障清理扫描会结束该调用。
        var destination = await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation);
        Assert.NotEqual(retiring.SiloAddress, destination);
        await stopping.WaitAsync(Timeout, Cancellation);
        await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(Timeout, Cancellation);
        Assert.False(queued.IsCompleted, $"{queued.Status}: {queued.Exception?.InnerException}");

        state.ReleaseHold.TrySetResult();
        Assert.Equal(destination, await queued.WaitAsync(Timeout, Cancellation));
        Assert.Equal(1, state.HoldCount);
        Assert.Equal(destination, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
    }

    [Fact]
    public async Task MigratabilityChecker_ResolvesConstructedGenericGrainsByTheirClass()
    {
        await using var cluster = await DeployAsync();
        var services = cluster.Silos[1].ServiceProvider;
        var checker = services.GetRequiredService<GrainMigratabilityChecker>();
        var grainFactory = services.GetRequiredService<IGrainFactory>();

        // 已构造的泛型 Grain 由未构造的泛型类描述，此处该类可迁移。
        Assert.True(checker.IsMigratable(grainFactory.GetGrain<IGenericRetirementHandoffGrain<int>>(Guid.NewGuid()).GetGrainId().Type, Orleans.Placement.ImmovableKind.Any));
        Assert.True(checker.IsMigratable(grainFactory.GetGrain<IGenericRetirementHandoffGrain<string>>(Guid.NewGuid()).GetGrainId().Type, Orleans.Placement.ImmovableKind.Any));

        // 不可移动的 Grain、无状态工作者 Grain，以及此 Silo 未知的 Grain 类型仍不可迁移。
        Assert.True(checker.IsMigratable(grainFactory.GetGrain<IRetirementHandoffGrain>(Guid.NewGuid(), typeof(RetirementHandoffGrain).FullName).GetGrainId().Type, Orleans.Placement.ImmovableKind.Any));
        Assert.False(checker.IsMigratable(grainFactory.GetGrain<IImmovableRetirementHandoffGrain>(Guid.NewGuid()).GetGrainId().Type, Orleans.Placement.ImmovableKind.Any));
        Assert.False(checker.IsMigratable(grainFactory.GetGrain<IStatelessRetirementHandoffGrain>(Guid.NewGuid()).GetGrainId().Type, Orleans.Placement.ImmovableKind.Any));
        Assert.False(checker.IsMigratable(GrainType.Create("unknown-grain-type"), Orleans.Placement.ImmovableKind.Any));
    }

    [Fact]
    public async Task QueuedRequestForGenericGrain_MigratesWithActivation_AndSurvivesRetiringSiloDeparture()
    {
        await using var cluster = await DeployAsync();
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IGenericRetirementHandoffGrain<int>>(key);
        await PlaceOnAsync(retiring, () => grain.GetLocation());

        // 除非其类不可移动，否则已构造的泛型 Grain 可以迁移。
        Assert.True(retiring.ServiceProvider.GetRequiredService<GrainMigratabilityChecker>().IsMigratable(grain.GetGrainId().Type, Orleans.Placement.ImmovableKind.Any));

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, grain.GetGrainId()) == 1);

        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => FindActivation(retiring, grain.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        try
        {
            // 排队请求随激活迁移，调用方在退役 Silo 离开后仍继续等待。
            var destination = await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation);
            Assert.NotEqual(retiring.SiloAddress, destination);
            await stopping.WaitAsync(Timeout, Cancellation);
            await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(Timeout, Cancellation);
            Assert.False(queued.IsCompleted, $"{queued.Status}: {queued.Exception?.InnerException}");

            state.ReleaseHold.TrySetResult();
            Assert.Equal(destination, await queued.WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            state.ReleaseHold.TrySetResult();
        }
    }

    [Fact]
    public async Task QueuedRequestFromCallerOnRetiringSilo_CompletesBeforeCallerDeactivates()
    {
        var builder = new InProcessTestClusterBuilder(3);
        await using var cluster = builder.Build();
        await cluster.DeployAsync(Cancellation);
        await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(Cancellation);
        await cluster.WaitForClusterManifestToStabilizeAsync().WaitAsync(Cancellation);

        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var target = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        var sender = grainFactory.GetGrain<IRetirementHandoffCallerGrain>(Guid.NewGuid());

        // 将发送方和目标均托管在正在退役的 Silo 上。
        RequestContext.Set(IPlacementDirector.PlacementHintKey, retiring.SiloAddress);
        try
        {
            Assert.Equal(retiring.SiloAddress, await target.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
            Assert.Equal(retiring.SiloAddress, await sender.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            RequestContext.Remove(IPlacementDirector.PlacementHintKey);
        }

        var running = target.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var call = sender.CallHoldThenGetLocation(key).AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, target.GetGrainId()) == 1);

        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => FindActivation(retiring, target.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 目标已连同排队请求迁移。发送方仍在运行等待
        // 已迁移请求的请求，因此退役 Silo 保留发送方及其挂起调用，直到响应到达。
        var destination = await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation);
        Assert.NotEqual(retiring.SiloAddress, destination);
        Assert.False(stopping.IsCompleted);
        Assert.False(call.IsCompleted);

        state.ReleaseHold.TrySetResult();
        Assert.Equal(destination, await call.WaitAsync(Timeout, Cancellation));
        await stopping.WaitAsync(Timeout, Cancellation);
        Assert.Equal(1, state.HoldCount);
    }

    [Fact]
    public async Task QueuedRequestForImmovableGrain_FailsWhenRetiringSiloDeparts()
    {
        var builder = new InProcessTestClusterBuilder(3);
        await using var cluster = builder.Build();
        await cluster.DeployAsync(Cancellation);
        await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(Cancellation);
        await cluster.WaitForClusterManifestToStabilizeAsync().WaitAsync(Cancellation);

        var retiring = cluster.Silos[1];
        var caller = cluster.Silos[0];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IImmovableRetirementHandoffGrain>(key);

        RequestContext.Set(IPlacementDirector.PlacementHintKey, retiring.SiloAddress);
        try
        {
            Assert.Equal(retiring.SiloAddress, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            RequestContext.Remove(IPlacementDirector.PlacementHintKey);
        }

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, grain.GetGrainId()) == 1);

        // 不发生迁移时，排队的请求会被转发，但调用方不会被重定向。
        // 若退役 Silo 在被转发请求完成之前离开，调用将立即失败，而不是
        // 等待响应超时。调用方的应用代码决定是否重试。
        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => FindActivation(retiring, grain.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);
        await stopping.WaitAsync(Timeout, Cancellation);

        try
        {
            await Assert.ThrowsAsync<SiloUnavailableException>(() => queued.WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            state.ReleaseHold.TrySetResult();
        }
    }

    [Fact]
    public async Task QueuedRequestFromCallerOnRetiringSilo_ReceivesCancellationAfterHandoff()
    {
        // 等待确认使发送方的请求保持运行，直到被取消的请求响应，
        // 因此退役 Silo 无法在信号到达前离开，也就不会因调用方已离开而取消该请求。
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.WaitForCancellationAcknowledgement = true)));
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var target = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        var sender = grainFactory.GetGrain<IRetirementHandoffCallerGrain>(Guid.NewGuid());
        await PlaceOnAsync(retiring, () => target.GetLocation());
        await PlaceOnAsync(retiring, () => sender.GetLocation());

        var running = target.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var call = sender.CallHoldUntilCanceled(key).AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, target.GetGrainId()) == 1);

        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => FindActivation(retiring, target.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 发送方的挂起调用与退役激活的队列共享其请求消息。
        // 移交完成后，取消调用会向请求所在的新 Silo 发送取消信号。
        Assert.NotEqual(retiring.SiloAddress, await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation));
        await WaitUntilAsync(() => FindActivation(retiring, target.GetGrainId()) is null);
        Assert.False(stopping.IsCompleted);
        state.Cancellation.Cancel();
        await state.HoldCanceled.Task.WaitAsync(Timeout, Cancellation);
        Assert.True(await call.WaitAsync(Timeout, Cancellation));
        await stopping.WaitAsync(Timeout, Cancellation);
    }

    [Fact]
    public async Task QueuedRequestWithExhaustedForwards_IsRejectedInsteadOfHandedOff()
    {
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.MaxForwardCount = 0)));
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(retiring, () => grain.GetLocation());

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, grain.GetGrainId()) == 1);

        // 移交计为一次转发，因此请求保持排队；当无法继续转发时将被拒绝。
        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => FindActivation(retiring, grain.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        try
        {
            // 拒绝与退役 Silo 离开的通知竞争到达调用方。无论哪种情况，调用都会立即失败，不会在其他位置运行。
            await Assert.ThrowsAnyAsync<OrleansMessageRejectionException>(() => queued.WaitAsync(Timeout, Cancellation));
            await stopping.WaitAsync(Timeout, Cancellation);
            Assert.Equal(0, state.HoldCount);
        }
        finally
        {
            state.ReleaseHold.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitMigration_CarriesQueuedRequestsAndUpdatesTheCallersCache(bool migrateDirectly)
    {
        var carriedRequest = new TaskCompletionSource<CorrelationId>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                var package = UnpackMigration(context);
                carriedRequest.TrySetResult(Assert.Single(package.Requests!).Id);
            }

            await context.Invoke();
        })));
        var caller = cluster.Silos[0];
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var locator = caller.ServiceProvider.GetRequiredService<Orleans.Runtime.GrainDirectory.GrainLocator>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        var activation = Assert.IsType<ActivationData>(FindActivation(source, grain.GetGrainId()));
        Assert.True(locator.TryLookupInCache(grain.GetGrainId(), out var cached));
        Assert.Equal(source.SiloAddress, cached.SiloAddress);

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.Record("queued").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);
        var requestId = FindPendingCallback(caller, grain.GetGrainId(), nameof(IRetirementHandoffGrain.Record)).Message.Id;
        using var events = new DiagnosticEventCollector(GrainLifecycleEvents.ListenerName);
        var deactivating = events.WaitForEventAsync(nameof(GrainLifecycleEvents.Deactivating),
            e => e.Payload is GrainLifecycleEvents.Deactivating d && ReferenceEquals(d.GrainContext, activation), Timeout, Cancellation);

        // Grain 连同排队请求迁移，而源仍留在集群中。
        try
        {
            if (migrateDirectly)
            {
                activation.Migrate(new() { [IPlacementDirector.PlacementHintKey] = target.SiloAddress }, Cancellation);
            }
            else
            {
                await grain.MigrateOnIdleTo(target.SiloAddress);
            }

            Assert.Equal(ActivationState.Deactivating, activation.State);
            var reason = Assert.IsType<GrainLifecycleEvents.Deactivating>((await deactivating).Payload).Reason;
            Assert.Equal(DeactivationReasonCode.Migrating, reason.ReasonCode);
        }
        finally
        {
            state.ReleaseBlock.TrySetResult();
        }

        await running.WaitAsync(Timeout, Cancellation);
        Assert.Equal(requestId, await carriedRequest.Task.WaitAsync(Timeout, Cancellation));
        await queued.WaitAsync(Timeout, Cancellation);
        Assert.Equal(["queued"], state.Calls);

        // 已迁移请求的响应会替换调用方缓存中的旧激活，因此其下一次调用直接发往新激活。
        Assert.True(locator.TryLookupInCache(grain.GetGrainId(), out cached));
        Assert.Equal(target.SiloAddress, cached.SiloAddress);
    }

    [Fact]
    public async Task RequestMigratedTwice_KeepsItsFirstCacheUpdate()
    {
        await using var cluster = await DeployAsync();
        var caller = cluster.Silos[0];
        var first = cluster.Silos[1];
        var second = cluster.Silos[2];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var locator = caller.ServiceProvider.GetRequiredService<Orleans.Runtime.GrainDirectory.GrainLocator>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(first, () => grain.GetLocation());

        // 两个请求排在第一个激活正在运行的请求之后。
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var hold = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(first, grain.GetGrainId()) == 1);
        var queued = grain.Record("queued").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(first, grain.GetGrainId()) == 2);

        // 两者都迁移至第二个 Silo，其中第一个运行，而另一个等待。
        await grain.MigrateOnIdleTo(second.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);
        Assert.Equal(second.SiloAddress, await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation));
        await WaitUntilAsync(() => GetWaitingCount(second, grain.GetGrainId()) == 1);

        // 等待的请求再次迁移回第一个 Silo，第三个激活在激活过程中被阻塞，
        // 使其响应在第二个激活的响应之后到达。
        state.HoldActivationOn = first.SiloAddress;
        await grain.MigrateOnIdleTo(first.SiloAddress);
        state.ReleaseHold.TrySetResult();
        Assert.Equal(second.SiloAddress, await hold.WaitAsync(Timeout, Cancellation));
        Assert.True(locator.TryLookupInCache(grain.GetGrainId(), out var cached));
        Assert.Equal(second.SiloAddress, cached.SiloAddress);

        // 只有响应携带的缓存更新才能重新填充调用方缓存。
        locator.InvalidateCache(grain.GetGrainId());
        try
        {
            await state.ActivationHeld.Task.WaitAsync(Timeout, Cancellation);
        }
        finally
        {
            state.ReleaseActivation.TrySetResult();
        }

        // 一条消息针对每个 Grain 只保留首次缓存更新，因此响应携带从原始激活到第二个激活的更新。
        await queued.WaitAsync(Timeout, Cancellation);
        Assert.True(locator.TryLookupInCache(grain.GetGrainId(), out cached));
        Assert.Equal(second.SiloAddress, cached.SiloAddress);
    }

    [Fact]
    public async Task MigrationWhoseAcknowledgementIsLost_RejectsItsRequestsWithoutResendingThem()
    {
        // 目标接受迁移及其请求，但源没有收到确认。
        var retargets = 0;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            switch (context.Request.GetMethodName())
            {
                case nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains):
                    await context.Invoke();
                    throw new InvalidOperationException("The acknowledgement is lost.");
                case nameof(IActivationMigrationManagerSystemTarget.RetargetMigratedRequests):
                    Interlocked.Increment(ref retargets);
                    break;
            }

            await context.Invoke();
        })));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        var activation = Assert.IsType<ActivationData>(FindActivation(source, grain.GetGrainId()));

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);

        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 目标可能已取得请求所有权，因此源拒绝请求而不重新发送，并完成停用。
        await activation.Deactivated.WaitAsync(Timeout, Cancellation);
        Assert.Equal(0, activation.GetRequestCount());

        try
        {
            // 目标已开始执行但暂时无法响应，因此必须由源的拒绝结束调用。
            Assert.Equal(target.SiloAddress, await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation));
            await Assert.ThrowsAsync<OrleansMessageRejectionException>(() => queued.WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            state.ReleaseHold.TrySetResult();
        }

        // 源转发的请求会先于源随后发出的调用到达目标，并首先运行。
        var fromSource = source.ServiceProvider.GetRequiredService<IGrainFactory>().GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        Assert.Equal(target.SiloAddress, await fromSource.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
        Assert.Equal(1, state.HoldCount);
        Assert.Equal(0, Volatile.Read(ref retargets));
    }

    [Fact]
    public async Task MigrationOutcome_IsAwaitedPastTheDeactivationTimeout()
    {
        // 目标将迁移阻塞至超过源激活的停用超时，然后使迁移失败。
        var acceptArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
        {
            silo.Configure<GrainCollectionOptions>(options => options.DeactivationTimeout = DeactivationTimeout);
            silo.AddIncomingGrainCallFilter(async context =>
            {
                if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
                {
                    acceptArrived.TrySetResult();
                    await releaseFailure.Task;
                    throw new InvalidOperationException("The migration fails.");
                }

                await context.Invoke();
            });
        }));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        var activation = Assert.IsType<ActivationData>(FindActivation(source, grain.GetGrainId()));

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);

        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        try
        {
            // 目标在作出响应之前仍可能接管请求，因此源激活在停用超时后仍留在 Catalog 中，
            // 且不会启动该 Grain 的其他激活。
            await acceptArrived.Task.WaitAsync(Timeout, Cancellation);
            await Task.Delay(DeactivationTimeout * 2, Cancellation);
            Assert.False(activation.Deactivated.IsCompleted);
            Assert.Same(activation, FindActivation(source, grain.GetGrainId()));
            Assert.Null(FindActivation(target, grain.GetGrainId()));
            Assert.False(state.HoldEntered.Task.IsCompleted);
        }
        finally
        {
            releaseFailure.TrySetResult();
        }

        // 迁移失败后，源拒绝未执行的请求，以便调用方重试。
        await activation.Deactivated.WaitAsync(Timeout, Cancellation);
        await Assert.ThrowsAsync<OrleansMessageRejectionException>(() => queued.WaitAsync(Timeout, Cancellation));
        Assert.Equal(0, state.HoldCount);
    }

    [Fact]
    public async Task MigrationAcceptance_HeldOutgoingFilterTimesOutAndAllowsDeactivation()
    {
        var responseTimeout = TimeSpan.FromSeconds(10);
        var filterEntered = new TaskCompletionSource<GrainMigrationPackage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFilter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var filterFinished = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = 0;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
        {
            silo.Services.AddKeyedSingleton<TimeProvider>(TimeProviderNames.Messaging, new FakeTimeProvider());
            silo.Configure<SiloMessagingOptions>(options => options.SystemResponseTimeout = responseTimeout);
            silo.AddOutgoingGrainCallFilter(async context =>
            {
                if (context.Request.GetMethodName() != nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
                {
                    await context.Invoke();
                    return;
                }

                filterEntered.TrySetResult(Assert.Single((List<GrainMigrationPackage>)context.Request.GetArgument(0)!));
                await releaseFilter.Task;
                try
                {
                    await context.Invoke();
                    filterFinished.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    filterFinished.TrySetResult(exception);
                    throw;
                }
            });
            silo.AddIncomingGrainCallFilter(async context =>
            {
                if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
                {
                    Interlocked.Increment(ref accepted);
                }

                await context.Invoke();
            });
        }));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var manager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var time = Assert.IsType<FakeTimeProvider>(source.ServiceProvider.GetRequiredKeyedService<TimeProvider>(TimeProviderNames.Messaging));
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>()
            .GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        var activation = Assert.IsType<ActivationData>(FindActivation(source, grain.GetGrainId()));
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.Record("queued").AsTask();
        await WaitUntilAsync(() => activation.WaitingCount == 1);
        try
        {
            await grain.MigrateOnIdleTo(target.SiloAddress);
            state.ReleaseBlock.TrySetResult();
            await running.WaitAsync(Timeout, Cancellation);
            var package = await filterEntered.Task.WaitAsync(Timeout, Cancellation);
            var request = Assert.Single(package.Requests!);

            // 完成泵的本轮执行，使其接受等待已启动，然后推进其时钟。
            await manager.RunOrQueueTask(() => Task.CompletedTask);
            time.Advance(responseTimeout - TimeSpan.FromTicks(1));
            await manager.RunOrQueueTask(() => Task.CompletedTask);
            Assert.False(package.Source!.Completion.IsCompleted);
            Assert.False(activation.Deactivated.IsCompleted);
            Assert.IsType<Orleans.Runtime.Messaging.MessageReadRequest>(request._bodyObject);

            time.Advance(TimeSpan.FromTicks(1));
            await activation.Deactivated.WaitAsync(Timeout, Cancellation);
            await Assert.ThrowsAsync<TimeoutException>(() => package.Source.Completion);
            await Assert.ThrowsAsync<OrleansMessageRejectionException>(() => queued.WaitAsync(Timeout, Cancellation));
            Assert.Null(FindActivation(source, grain.GetGrainId()));
            Assert.Null(request._bodyObject);
            Assert.Empty(package.MigrationContext.Keys);
            Assert.Empty(state.Calls);
        }
        finally
        {
            state.ReleaseBlock.TrySetResult();
            releaseFilter.TrySetResult();
            if (filterEntered.Task.IsCompletedSuccessfully)
            {
                await filterFinished.Task.WaitAsync(Timeout, Cancellation);
            }
        }

        Assert.IsAssignableFrom<OperationCanceledException>(await filterFinished.Task);
        Assert.Equal(0, Volatile.Read(ref accepted));
        Assert.Empty(state.Calls);
        Assert.Null(FindActivation(target, grain.GetGrainId()));
    }

    [Fact]
    public async Task MigrationCanceledBeforeItIsSent_IsNotSentAndForwardsItsRequests()
    {
        var migrationsReceived = 0;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                Interlocked.Increment(ref migrationsReceived);
            }

            await context.Invoke();
        })));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var grainId = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>().GetGrain<IRetirementHandoffGrain>(Guid.NewGuid(), typeof(RetirementHandoffGrain).FullName).GetGrainId();
        var original = CreateRequest(cluster, grainId, 1, CreatePooledBuffer(Enumerable.Repeat((byte)0x5A, 20_000).ToArray()));
        List<Message> requests = [original];

        // 激活在管理器发送迁移之前停止等待，因此管理器不会发送迁移。
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var context = new MigrationContext(source.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        Assert.True(context.TryAddValue("pooled", new byte[20_000]));
        var migration = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, grainId), context, requests, canceled.Token);
        Assert.NotNull(migration.Package.Requests);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => migration.Completion.WaitAsync(Timeout, Cancellation));
        Assert.Equal(0, Volatile.Read(ref migrationsReceived));
        Assert.Empty(context.Keys);

        // 请求随迁移保留，直到激活停止接收请求，随后原样转发。
        migration.Deliver();
        Assert.Same(original, Assert.Single(requests));
        Assert.Same(requests, migration.Package.Requests);
        Assert.Same(original, Assert.Single(migration.Package.Requests!));
        Assert.Equal(20_000, ((Orleans.Serialization.Buffers.PooledBuffer)original.BodyObject!).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateRequestsStayWithTheActivationAndFollowTheOriginalHandoff(bool acceptMigration)
    {
        // 将迁移阻塞至超过源激活的停用超时。
        var acceptArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcceptance = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CorrelationId? carriedRequestId = null;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
        {
            silo.Configure<GrainCollectionOptions>(options => options.DeactivationTimeout = DeactivationTimeout);
            silo.AddIncomingGrainCallFilter(async context =>
            {
                if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
                {
                    var package = Assert.Single((List<GrainMigrationPackage>)context.Request.GetArgument(0)!);
                    carriedRequestId = Assert.Single(package.Requests!).Id;
                    acceptArrived.TrySetResult();
                    await releaseAcceptance.Task;
                    if (!acceptMigration)
                    {
                        throw new InvalidOperationException("The migration fails.");
                    }
                }

                await context.Invoke();
            });
        }));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>().GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        var activation = Assert.IsType<ActivationData>(FindActivation(source, grain.GetGrainId()));

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.Record("migrated").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);
        var requestId = FindPendingCallback(cluster.Silos[0], grain.GetGrainId(), nameof(IRetirementHandoffGrain.Record)).Message.Id;

        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        Task later;
        try
        {
            // 源激活停用超时后，来自源 Silo 的调用到达仍在 Catalog 中的源激活，
            // 而非新激活，并在迁移等待目标时继续在那里等待。
            await acceptArrived.Task.WaitAsync(Timeout, Cancellation);
            Assert.Equal(requestId, carriedRequestId);
            await Task.Delay(DeactivationTimeout, Cancellation);
            var fromSource = source.ServiceProvider.GetRequiredService<IGrainFactory>().GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
            later = fromSource.Record("later").AsTask();
            await WaitUntilAsync(() => activation.WaitingCount == 1);
            Assert.Same(activation, FindActivation(source, grain.GetGrainId()));
            Assert.Null(FindActivation(target, grain.GetGrainId()));
        }
        finally
        {
            releaseAcceptance.TrySetResult();
        }

        if (acceptMigration)
        {
            await queued.WaitAsync(Timeout, Cancellation);
        }
        else
        {
            await Assert.ThrowsAsync<OrleansMessageRejectionException>(() => queued.WaitAsync(Timeout, Cancellation));
        }

        await later.WaitAsync(Timeout, Cancellation);
        await activation.Deactivated.WaitAsync(Timeout, Cancellation);
        Assert.Equal(acceptMigration ? ["migrated", "later"] : new[] { "later" }, state.Calls);
    }

    [Fact]
    public async Task MigrationQueuedBehindABlockedBatch_IsCanceledWithoutBeingSent()
    {
        // 目标阻塞第一次迁移的批次，因此迁移管理器向目标发送的泵等待该批次。
        var acceptArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAccept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new ConcurrentQueue<GrainId>();
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                foreach (var package in (List<GrainMigrationPackage>)context.Request.GetArgument(0)!)
                {
                    received.Enqueue(package.GrainId);
                }

                acceptArrived.TrySetResult();
                await releaseAccept.Task;
            }

            await context.Invoke();
        })));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var pool = source.ServiceProvider.GetRequiredService<SerializerSessionPool>();
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var blockedGrain = grainFactory.GetGrain<IRetirementHandoffGrain>(Guid.NewGuid(), typeof(RetirementHandoffGrain).FullName).GetGrainId();
        var queuedGrain = grainFactory.GetGrain<IRetirementHandoffGrain>(Guid.NewGuid(), typeof(RetirementHandoffGrain).FullName).GetGrainId();

        var blocked = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, blockedGrain), new MigrationContext(pool), [CreateOneWayRequest(blockedGrain, 1)], CancellationToken.None);
        await acceptArrived.Task.WaitAsync(Timeout, Cancellation);

        // 第二次迁移在第一次之后等待。泵处理它之前，激活的截止时间已过，因此迁移在未发送的情况下完成。
        using var deadline = new CancellationTokenSource();
        var queued = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, queuedGrain), new MigrationContext(pool), [CreateOneWayRequest(queuedGrain, 2)], deadline.Token);
        try
        {
            deadline.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.Completion.WaitAsync(Timeout, Cancellation));
            Assert.False(blocked.Completion.IsCompleted);
        }
        finally
        {
            releaseAccept.TrySetResult();
        }

        await blocked.Completion.WaitAsync(Timeout, Cancellation);
        Assert.Equal([blockedGrain], received);
    }

    [Fact]
    public async Task AcceptedMigrationWhoseRetargetIsHeld_CompletesAtItsDeadline()
    {
        // 目标接管迁移携带的请求，调用方所在的 Silo 阻塞这些调用的重定向。
        var retargetArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetarget = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.RetargetMigratedRequests))
            {
                retargetArrived.TrySetResult();
                await releaseRetarget.Task;
            }

            await context.Invoke();
        })));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var grainId = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>().GetGrain<IRetirementHandoffGrain>(Guid.NewGuid(), typeof(RetirementHandoffGrain).FullName).GetGrainId();

        using var deadline = new CancellationTokenSource();
        var migration = migrationManager.StartMigration(
            target.SiloAddress,
            CreateSourceAddress(source, grainId),
            new MigrationContext(source.ServiceProvider.GetRequiredService<SerializerSessionPool>()),
            [CreateRequest(cluster, grainId, 1, new List<string> { "value" })],
            deadline.Token);
        try
        {
            // 重定向被阻塞期间，激活的截止时间已过。目标拥有请求，因此迁移完成，请求不会重发。
            await retargetArrived.Task.WaitAsync(Timeout, Cancellation);
            deadline.Cancel();
            await migration.Completion.WaitAsync(Timeout, Cancellation);
            Assert.True(migration.Completion.IsCompletedSuccessfully);
        }
        finally
        {
            releaseRetarget.TrySetResult();
        }
    }

    [Fact]
    public async Task FailedMigrationOfRequestFromCallerOnTheSource_IsRejectedToItsCaller()
    {
        // 目标使迁移失败。
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                throw new InvalidOperationException("The migration fails.");
            }

            await context.Invoke();
        })));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = source.ServiceProvider.GetRequiredService<IGrainFactory>().GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());

        // 源上的调用方传入池化缓冲区，请求持有它直到迁移发送请求。发送时将缓冲区的存储归还给池。
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var payload = Enumerable.Repeat((byte)0x5A, 20_000).ToArray();
        var echo = grain.EchoPooledPayload(CreatePooledBuffer(payload)).AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);

        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 请求没有运行，因此源拒绝请求，调用方可以重试。
        await Assert.ThrowsAsync<OrleansMessageRejectionException>(() => echo.WaitAsync(Timeout, Cancellation));
    }

    [Fact]
    public async Task DestinationWithoutMigratedRequestSupport_ReceivesAnOrdinaryMigrationAndRequestsAreForwarded()
    {
        // 源看到的目标清单缺少支持接收请求的 IActivationMigrationManagerSystemTarget 版本，就像目标 Silo 运行较早版本一样。
        var earlierVersionSilos = new ConcurrentDictionary<SiloAddress, bool>();
        var calls = new ConcurrentQueue<string>();
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
        {
            silo.Services.AddSingleton<IClusterManifestProvider>(services => new EarlierVersionManifestProvider(
                services.GetRequiredService<Orleans.Runtime.Metadata.ClusterManifestProvider>(),
                services.GetRequiredService<Orleans.Metadata.GrainInterfaceTypeResolver>().GetGrainInterfaceType(typeof(IActivationMigrationManagerSystemTarget)),
                earlierVersionSilos));
            silo.AddIncomingGrainCallFilter(async context =>
            {
                if (context.Request.GetMethodName() is nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains)
                    or nameof(IActivationMigrationManagerSystemTarget.RetargetMigratedRequests))
                {
                    calls.Enqueue(context.Request.GetMethodName());
                }

                if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
                {
                    var packages = (List<GrainMigrationPackage>)context.Request.GetArgument(0)!;
                    Assert.All(packages, package => Assert.Null(package.Requests));
                }

                await context.Invoke();
            });
        }));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        earlierVersionSilos[target.SiloAddress] = true;
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);

        // Grain 使用普通协议迁移，不携带请求。
        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 源保持请求不变并将其转发至迁移后的激活，不重定向调用方的挂起调用。
        Assert.Equal(target.SiloAddress, await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation));
        state.ReleaseHold.TrySetResult();
        Assert.Equal(target.SiloAddress, await queued.WaitAsync(Timeout, Cancellation));
        Assert.Equal(1, state.HoldCount);
        Assert.Equal([nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains)], calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigratedRequestsForAnExistingActivation_UseOrdinaryAdmissionAndCacheRepair(bool invalidPreviousAddress)
    {
        await using var cluster = await DeployAsync();
        var caller = cluster.Silos[0];
        var previous = cluster.Silos[2];
        var silo = cluster.Silos[1];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var locator = caller.ServiceProvider.GetRequiredService<Orleans.Runtime.GrainDirectory.GrainLocator>();
        var migrationManager = silo.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(silo, () => grain.GetLocation());
        var activation = Assert.IsType<ActivationData>(FindActivation(silo, grain.GetGrainId()));
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);

        // 调用方的缓存保存正在迁移的激活地址，该地址由迁移替换。
        var previousAddress = new GrainAddress { GrainId = grain.GetGrainId(), SiloAddress = previous.SiloAddress, ActivationId = ActivationId.NewId() };
        locator.UpdateCache(grain.GetGrainId(), previous.SiloAddress);

        // 迁移到达得太晚，无法创建激活，因此现有激活像接收转发请求一样接收该请求。
        var request = CreateOneWayRequest(grain.GetGrainId(), 1);
        request.SendingSilo = caller.SiloAddress;
        var context = new MigrationContext(silo.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        if (invalidPreviousAddress)
        {
            context.TryAddValue(ActivationData.GrainAddressMigrationContextKey, "unused invalid address");
        }
        else
        {
            context.TryAddValue(ActivationData.GrainAddressMigrationContextKey, previousAddress);
        }
        GrainMigrationPackage package = new()
        {
            GrainId = grain.GetGrainId(),
            MigrationContext = context,
            Requests = [request],
        };
        await migrationManager.AcceptMigratingGrains([package], Cancellation);
        Assert.Same(activation, FindActivation(silo, grain.GetGrainId()));
        Assert.Equal(1, activation.WaitingCount);
        Assert.Equal(1, request.ForwardCount);

        Assert.Empty(context.Keys);
        Assert.Null(request.CacheInvalidationHeader);

        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 下一次普通调用通过正常转发修复过时路由。
        Assert.Equal(silo.SiloAddress, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
        Assert.True(locator.TryLookupInCache(grain.GetGrainId(), out var cached));
        Assert.Equal(silo.SiloAddress, cached.SiloAddress);
        Assert.Same(activation, FindActivation(silo, grain.GetGrainId()));
    }

    [Fact]
    public async Task MigratedRequests_UseOrdinaryPerRequestOverloadAdmission()
    {
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.MaxEnqueuedRequestsHardLimit = 1)));
        var silo = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var migrationManager = silo.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grainId = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName).GetGrainId();

        // 新激活在激活过程中被阻塞，因此其接纳的请求保持排队。
        state.HoldActivationOn = silo.SiloAddress;
        GrainMigrationPackage package = new()
        {
            GrainId = grainId,
            MigrationContext = new MigrationContext(silo.ServiceProvider.GetRequiredService<SerializerSessionPool>()),
            Requests = [CreateOneWayRequest(grainId, 1), CreateOneWayRequest(grainId, 2), CreateOneWayRequest(grainId, 3)],
        };
        var accepting = migrationManager.AcceptMigratingGrains([package], Cancellation).AsTask();
        try
        {
            // 普通接纳机制在硬性限制为 1 时接纳两个请求，并拒绝第三个。
            await state.ActivationHeld.Task.WaitAsync(Timeout, Cancellation);
            var activation = Assert.IsType<ActivationData>(FindActivation(silo, grainId));
            // 接纳在调度激活之前完成，因此被阻塞的激活充当观察屏障。
            Assert.Equal(2, activation.WaitingCount);
        }
        finally
        {
            state.ReleaseActivation.TrySetResult();
        }

        await accepting.WaitAsync(Timeout, Cancellation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigratedRequests_UseOrdinaryAdmissionForAnExistingActivation(bool deactivating)
    {
        var arrived = new TaskCompletionSource<GrainMigrationPackage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcceptance = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                arrived.TrySetResult(Assert.Single((List<GrainMigrationPackage>)context.Request.GetArgument(0)!));
                await releaseAcceptance.Task;
            }

            await context.Invoke();
        })));
        var caller = cluster.Silos[0];
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        Task migrated;
        RequestContext.Set(IPlacementDirector.PlacementHintKey, caller.SiloAddress);
        try
        {
            // 若目标停用，普通转发会将该请求发送到第三个 Silo。
            migrated = grain.Record("migrated").AsTask();
        }
        finally
        {
            RequestContext.Remove(IPlacementDirector.PlacementHintKey);
        }

        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);
        state.HoldActivationOn = target.SiloAddress;
        try
        {
            await grain.MigrateOnIdleTo(target.SiloAddress);
            state.ReleaseBlock.TrySetResult();
            await running.WaitAsync(Timeout, Cancellation);
            var package = await arrived.Task.WaitAsync(Timeout, Cancellation);

            // 在迁移包投递前独立创建目标激活，并保留原始迁移包的上下文。
            Assert.True(package.MigrationContext.TryGetValue(ActivationData.GrainAddressMigrationContextKey, out GrainAddress? previous));
            var context = new MigrationContext(target.ServiceProvider.GetRequiredService<SerializerSessionPool>());
            Assert.True(context.TryAddValue(ActivationData.GrainAddressMigrationContextKey, previous));
            var activation = Assert.IsType<ActivationData>(target.ServiceProvider.GetRequiredService<Catalog>()
                .GetOrCreateActivation(package.GrainId, null, new GrainMigrationPackage { GrainId = package.GrainId, MigrationContext = context }));
            await state.ActivationHeld.Task.WaitAsync(Timeout, Cancellation);

            var local = target.ServiceProvider.GetRequiredService<IGrainFactory>()
                .GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
            var direct = local.Record("direct").AsTask();
            await WaitUntilAsync(() => activation.WaitingCount == 1);

            if (deactivating)
            {
                state.ReleaseActivation.TrySetResult();
                await direct.WaitAsync(Timeout, Cancellation);
                var holding = local.HoldThenGetLocation().AsTask();
                await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation);
                activation.Deactivate(new DeactivationReason(DeactivationReasonCode.ActivationIdle, "Test ordinary admission during deactivation"), Cancellation);
                Assert.Equal(ActivationState.Deactivating, activation.State);
                releaseAcceptance.TrySetResult();
                await WaitUntilAsync(() => activation.WaitingCount == 1);
                state.ReleaseHold.TrySetResult();
                await holding.WaitAsync(Timeout, Cancellation);
                await activation.Deactivated.WaitAsync(Timeout, Cancellation);
            }
            else
            {
                releaseAcceptance.TrySetResult();
                await WaitUntilAsync(() => activation.WaitingCount == 2);
                state.ReleaseActivation.TrySetResult();
                await direct.WaitAsync(Timeout, Cancellation);
            }

            await migrated.WaitAsync(Timeout, Cancellation);
            Assert.Equal(["direct", "migrated"], state.Calls);
            Assert.Equal(deactivating ? caller.SiloAddress : target.SiloAddress, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            releaseAcceptance.TrySetResult();
            state.ReleaseBlock.TrySetResult();
            state.ReleaseActivation.TrySetResult();
            state.ReleaseHold.TrySetResult();
        }
    }

    [Fact]
    public async Task MigratedRequests_AreRejectedWhenDestinationActivationFails()
    {
        await using var cluster = await DeployAsync();
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        state.FailActivationOn = target.SiloAddress;
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.Record("queued").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);
        try
        {
            await grain.MigrateOnIdleTo(target.SiloAddress);
            state.ReleaseBlock.TrySetResult();
            await running.WaitAsync(Timeout, Cancellation);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => queued.WaitAsync(Timeout, Cancellation));
            Assert.Equal("Destination activation failed.", error.Message);
            Assert.Empty(state.Calls);
        }
        finally
        {
            state.ReleaseBlock.TrySetResult();
        }
    }

    [Fact]
    public async Task MigratedRequestWhichExpiredInTransit_IsDroppedAndTheOthersAreAccepted()
    {
        var received = new TaskCompletionSource<(Message Expired, Message Live)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                var package = UnpackMigration(context);
                var requests = Assert.IsType<List<Message>>(package.Requests);
                Assert.Equal(2, requests.Count);
                requests[0].TimeToLive = TimeSpan.FromSeconds(-1);
                received.TrySetResult((requests[0], requests[1]));
            }

            await context.Invoke();
        })));
        var caller = cluster.Silos[0];
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var expiredCall = grain.Record("expired").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);
        var expiredCallback = FindPendingCallback(caller, grain.GetGrainId(), nameof(IRetirementHandoffGrain.Record));
        var liveCall = grain.Record("live").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 2);

        try
        {
            await grain.MigrateOnIdleTo(target.SiloAddress);
            state.ReleaseBlock.TrySetResult();
            await running.WaitAsync(Timeout, Cancellation);
            var requests = await received.Task.WaitAsync(Timeout, Cancellation);
            expiredCallback.OnTimeout();
            await Assert.ThrowsAsync<TimeoutException>(() => expiredCall.WaitAsync(Timeout, Cancellation));
            await liveCall.WaitAsync(Timeout, Cancellation);
            Assert.Null(requests.Expired._bodyObject);
            Assert.Equal(0, requests.Expired.ForwardCount);
            Assert.Equal(1, requests.Live.ForwardCount);
            Assert.Equal(["live"], state.Calls);
            Assert.Equal(target.SiloAddress, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            state.ReleaseBlock.TrySetResult();
        }
    }

    [Fact]
    public async Task MigrationBatch_TimingOutBeforePayloadSerializationReleasesAndDiscardsIt()
    {
        var firstSubmitted = new TaskCompletionSource<List<GrainMigrationPackage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSubmitted = new TaskCompletionSource<List<GrainMigrationPackage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serializing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseSerialization = new ManualResetEventSlim();
        var invocationCount = 0;
        var gate = new MigrationSerializationGate
        {
            BeforeSerialization = () =>
            {
                serializing.TrySetResult();
                releaseSerialization.Wait(Cancellation);
            }
        };
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
        {
            silo.Services.AddSingleton<MigrationSerializationCallbacks>();
            silo.AddOutgoingGrainCallFilter(async context =>
            {
                if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
                {
                    var batch = (List<GrainMigrationPackage>)context.Request.GetArgument(0)!;
                    switch (Interlocked.Increment(ref invocationCount))
                    {
                        case 1:
                            firstSubmitted.TrySetResult(batch);
                            // 在 RPC 的批次参数可被序列化之前阻塞头部。
                            RequestContext.Set("migration-serialization-gate", gate);
                            try
                            {
                                await context.Invoke();
                            }
                            finally
                            {
                                RequestContext.Remove("migration-serialization-gate");
                            }

                            return;
                        case 2:
                            secondSubmitted.TrySetResult(batch);
                            break;
                    }
                }

                await context.Invoke();
            });
        }));
        var caller = cluster.Silos[0];
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var firstState = RetirementHandoffState.Create(firstKey);
        var secondState = RetirementHandoffState.Create(secondKey);
        var first = grainFactory.GetGrain<IRetirementHandoffGrain>(firstKey, typeof(RetirementHandoffGrain).FullName);
        var second = grainFactory.GetGrain<IRetirementHandoffGrain>(secondKey, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => first.GetLocation());
        await PlaceOnAsync(source, () => second.GetLocation());
        var firstRunning = first.Block().AsTask();
        var secondRunning = second.Block().AsTask();
        await Task.WhenAll(firstState.BlockEntered.Task, secondState.BlockEntered.Task).WaitAsync(Timeout, Cancellation);
        var firstCall = first.Record("first").AsTask();
        var secondCall = second.Record("second").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, first.GetGrainId()) == 1 && GetWaitingCount(source, second.GetGrainId()) == 1);
        try
        {
            await first.MigrateOnIdleTo(target.SiloAddress);
            firstState.ReleaseBlock.TrySetResult();
            await firstRunning.WaitAsync(Timeout, Cancellation);
            var firstBatch = await firstSubmitted.Task.WaitAsync(Timeout, Cancellation);
            await serializing.Task.WaitAsync(Timeout, Cancellation);
            var carried = Assert.Single(Assert.Single(firstBatch).Requests!);
            Assert.IsType<Orleans.Runtime.Messaging.MessageReadRequest>(carried._bodyObject);

            // 写入器尚未取得迁移有效载荷的所有权。
            var remote = source.ServiceProvider.GetRequiredService<IInternalGrainFactory>()
                .GetSystemTarget<IActivationMigrationManagerSystemTarget>(Constants.ActivationMigratorType, target.SiloAddress);
            FindPendingCallback(source, remote.GetGrainId()).OnTimeout();
            await Assert.ThrowsAsync<OrleansMessageRejectionException>(() => firstCall.WaitAsync(Timeout, Cancellation));
            Assert.Null(carried._bodyObject);
            Assert.Empty(Assert.Single(firstBatch).MigrationContext.Keys);
            Assert.Throws<OperationCanceledException>(() => Assert.Single(firstBatch).Source!.BeginSerialization());

            await second.MigrateOnIdleTo(target.SiloAddress);
            secondState.ReleaseBlock.TrySetResult();
            await secondRunning.WaitAsync(Timeout, Cancellation);
            var secondBatch = await secondSubmitted.Task.WaitAsync(Timeout, Cancellation);
            Assert.NotSame(firstBatch, secondBatch);
            Assert.Equal(first.GetGrainId(), Assert.Single(firstBatch).GrainId);
            Assert.Equal(second.GetGrainId(), Assert.Single(secondBatch).GrainId);
        }
        finally
        {
            releaseSerialization.Set();
            firstState.ReleaseBlock.TrySetResult();
            secondState.ReleaseBlock.TrySetResult();
        }

        await secondCall.WaitAsync(Timeout, Cancellation);
        Assert.Empty(firstState.Calls);
        Assert.Equal(["second"], secondState.Calls);
        Assert.Equal(2, Volatile.Read(ref invocationCount));
    }

    [Fact]
    public async Task MigratedRequests_RunBeforeRequestsWhichReachTheNewActivationDirectly()
    {
        await using var cluster = await DeployAsync();
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.Record("queued").AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);

        // Grain 连同排队请求迁移；新激活在 Grain 目录中注册后，于激活过程中被阻塞。
        state.HoldActivationOn = target.SiloAddress;
        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);
        Task direct;
        try
        {
            await state.ActivationHeld.Task.WaitAsync(Timeout, Cancellation);

            // 来自目标 Silo 的调用通过 Grain 目录找到新激活，因此直接到达新激活。
            direct = target.ServiceProvider.GetRequiredService<IGrainFactory>()
                .GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName).Record("direct").AsTask();
            await WaitUntilAsync(() => GetWaitingCount(target, grain.GetGrainId()) == 2);
        }
        finally
        {
            state.ReleaseActivation.TrySetResult();
        }

        // 迁移携带的请求在目录注册前被接纳，因此这个后到的直接请求排在其后。
        await Task.WhenAll(queued, direct).WaitAsync(Timeout, Cancellation);
        Assert.Equal(["queued", "direct"], state.Calls);
    }

    [Fact]
    public async Task MigrationRoutes_DoNotOverwriteALaterRetarget()
    {
        // 阻塞目标的确认，使同一调用的后续路由在迁移等待接受时到达。
        var accepted = new TaskCompletionSource<(GrainId SendingGrain, CorrelationId Id)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAcceptance = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                var package = UnpackMigration(context);
                var request = Assert.Single(package.Requests!);
                await context.Invoke();
                accepted.TrySetResult((request.SendingGrain, request.Id));
                await releaseAcceptance.Task;
            }
            else
            {
                await context.Invoke();
            }
        })));
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var target = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        var sender = grainFactory.GetGrain<IRetirementHandoffCallerGrain>(Guid.NewGuid());
        await PlaceOnAsync(retiring, () => target.GetLocation());
        await PlaceOnAsync(retiring, () => sender.GetLocation());

        // 发送方和目标均运行于退役 Silo 上，因此发送方与目标队列共享请求消息。
        var running = target.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var call = sender.CallHoldThenGetLocation(key).AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, target.GetGrainId()) == 1);

        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => FindActivation(retiring, target.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        CallbackData callback;
        SiloAddress laterTarget;
        try
        {
            var request = await accepted.Task.WaitAsync(Timeout, Cancellation);
            var destination = await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation);
            laterTarget = cluster.Silos.Select(silo => silo.SiloAddress).First(silo => !silo.Equals(retiring.SiloAddress) && !silo.Equals(destination));

            // 同一请求的后续迁移（例如目标继而退役）先完成调用重定向。
            var runtimeClient = retiring.ServiceProvider.GetRequiredService<InsideRuntimeClient>();
            callback = GetCallback(runtimeClient, request.SendingGrain, request.Id);
            runtimeClient.RetargetCallback(request.SendingGrain, request.Id, laterTarget, forwardCount: 2);
        }
        finally
        {
            releaseAcceptance.TrySetResult();
        }

        // 较早迁移的路由已被取代，因此调用保留较新的目标。
        try
        {
            await WaitUntilAsync(() => FindActivation(retiring, target.GetGrainId()) is null);
            Assert.Equal(laterTarget, callback.Message.TargetSilo);
            Assert.Equal(2, callback.Message.ForwardCount);
        }
        finally
        {
            state.ReleaseHold.TrySetResult();
        }

        await call.WaitAsync(Timeout, Cancellation);
        await stopping.WaitAsync(Timeout, Cancellation);

        static CallbackData GetCallback(InsideRuntimeClient runtimeClient, GrainId sendingGrain, CorrelationId id)
        {
            var callbacks = (ConcurrentDictionary<(GrainId, CorrelationId), CallbackData>)typeof(InsideRuntimeClient)
                .GetField("callbacks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(runtimeClient)!;
            return callbacks[(sendingGrain, id)];
        }
    }

    [Fact]
    public async Task RetiringSilo_DoesNotAwaitAbandonedStuckActivation()
    {
        var maxRequestProcessingTime = TimeSpan.FromSeconds(1);
        await using var cluster = await DeployAsync(builder =>
        {
            builder.ConfigureSilo((_, silo) => silo.Configure<SiloMessagingOptions>(options => options.MaxRequestProcessingTime = maxRequestProcessingTime));
            ExtendShutdownTimeouts(builder);
        });
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(retiring, () => grain.GetLocation());

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var stuck = Assert.IsType<ActivationData>(FindActivation(retiring, grain.GetGrainId()));

        // 正在运行的请求超过处理时限后到达的新请求，导致卡住的激活被放弃。
        // 延迟只需构成正在运行的请求持续时间的下界。
        await Task.Delay(maxRequestProcessingTime * 2, Cancellation);
        await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation);
        Assert.Equal(ActivationState.Deactivating, stuck.State);

        try
        {
            // 被放弃的请求持续运行，且关闭超时超过测试超时，因此只有不等待被放弃的激活，关闭才能完成。
            await cluster.StopSiloAsync(retiring, Cancellation).WaitAsync(Timeout, Cancellation);
        }
        finally
        {
            state.ReleaseBlock.TrySetResult();
        }
    }

    [Fact]
    public async Task MigrationUnderwayWhenRetiring_CarriesQueuedRequests()
    {
        // 阻塞目标对迁移中激活的接受，使退役在迁移过程中开始。
        var acceptArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAccept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                acceptArrived.TrySetResult();
                await releaseAccept.Task;
            }

            await context.Invoke();
        })));
        var retiring = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(retiring, () => grain.GetLocation());

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var queued = grain.HoldThenGetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, grain.GetGrainId()) == 1);

        // 正在运行的请求完成后，普通迁移开始，并等待目标接受激活。
        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        Task stopping;
        try
        {
            await acceptArrived.Task.WaitAsync(Timeout, Cancellation);
            using var stages = new SiloStageObserver(retiring.SiloAddress, ServiceLifecycleStage.GrainDeactivation, releaseAccept.Task);
            stopping = cluster.StopSiloAsync(retiring, Cancellation);
            await stages.Stopping.WaitAsync(Timeout, Cancellation);
        }
        finally
        {
            releaseAccept.TrySetResult();
        }

        try
        {
            // 排队请求已随激活迁移，调用方在退役 Silo 离开后继续等待。
            Assert.Equal(target.SiloAddress, await state.HoldEntered.Task.WaitAsync(Timeout, Cancellation));
            await stopping.WaitAsync(Timeout, Cancellation);
            await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(Timeout, Cancellation);
            Assert.False(queued.IsCompleted, $"{queued.Status}: {queued.Exception?.InnerException}");

            state.ReleaseHold.TrySetResult();
            Assert.Equal(target.SiloAddress, await queued.WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            state.ReleaseHold.TrySetResult();
        }
    }

    [Fact]
    public async Task DeactivationUnderwayWhenRetiring_KeepsActivationInCatalogAndIsAwaited()
    {
        // 若没有出站队列延迟，停用所有激活的阶段在该停用操作完成后立即结束。
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.WaitForMessageToBeQueuedForOutboundTime = TimeSpan.Zero)));
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IDisposalHeldGrain>(key);
        await PlaceOnAsync(retiring, () => grain.GetLocation());
        var activation = Assert.IsType<ActivationData>(FindActivation(retiring, grain.GetGrainId()));
        var catalog = retiring.ServiceProvider.GetRequiredService<ActivationDirectory>();

        // 激活执行停用，其资源释放被阻塞。
        await grain.DeactivateNow();
        await state.DisposeEntered.Task.WaitAsync(Timeout, Cancellation);

        // 在停用完成之前，激活仍留在 Catalog 中，因此不会创建该 Grain 的其他本地激活。
        Assert.Same(activation, FindActivation(retiring, grain.GetGrainId()));

        using var stages = new SiloStageObserver(retiring.SiloAddress, ServiceLifecycleStage.GrainDeactivation, state.ReleaseDispose.Task);
        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        try
        {
            // 退役过程停用 Catalog 中的激活，包括正在停用的激活。
            await stages.Stopping.WaitAsync(Timeout, Cancellation);
            Assert.Same(activation, FindActivation(retiring, grain.GetGrainId()));

            // 资源释放被阻塞时，该阶段无法完成，因此当退役等待停用时，此限时等待不会因阶段提前完成而失败。
            // 若退役不等待停用，该阶段就会在此时间窗口内完成。
            var stoppedWhileHeld = await Task.WhenAny(stages.StoppedAfterRelease, Task.Delay(TimeSpan.FromSeconds(1), Cancellation));
            Assert.NotSame(stages.StoppedAfterRelease, stoppedWhileHeld);
        }
        finally
        {
            state.ReleaseDispose.TrySetResult();
        }

        // 停用所有激活的阶段（GrainDeactivation）仅在被阻塞的资源释放获准继续后完成。
        Assert.True(await stages.StoppedAfterRelease.WaitAsync(Timeout, Cancellation));
        await stopping.WaitAsync(Timeout, Cancellation);
        Assert.Null(catalog.FindTarget(grain.GetGrainId()));
    }

    [Fact]
    public async Task CancellationReachingTheActivatingNewActivation_CancelsTheMigratedRequestBeforeItRuns()
    {
        await using var cluster = await DeployAsync(ExtendShutdownTimeouts);
        var caller = cluster.Silos[0];
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var call = grain.HoldUntilCanceled(CancellationToken.None).AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);

        // Grain 连同排队请求迁移，新激活在激活过程中被阻塞，请求保持排队。
        state.HoldActivationOn = target.SiloAddress;
        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);
        var (sender, messageId) = FindPendingCall(caller, grain.GetGrainId());
        try
        {
            await state.ActivationHeld.Task.WaitAsync(Timeout, Cancellation);
            var activation = Assert.IsType<ActivationData>(FindActivation(target, grain.GetGrainId()));
            Assert.Equal(1, activation.WaitingCount);

            // 新激活在激活前将迁移的请求入队，因此此时到达的取消信号会在请求运行前将其取消。
            await ((IGrainCallCancellationExtension)activation).CancelRequestAsync(sender, messageId, CancellationToken.None).AsTask().WaitAsync(Timeout, Cancellation);
            Assert.Equal(0, activation.WaitingCount);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Timeout, Cancellation));

            // 激活完成后，该激活没有请求可运行。
            state.ReleaseActivation.TrySetResult();
            await WaitUntilAsync(() => activation.State is ActivationState.Valid);
            await activation.RunOrQueueTask(() => Task.CompletedTask);
            Assert.False(state.HoldEntered.Task.IsCompleted);
            Assert.Equal(0, activation.GetRequestCount());
        }
        finally
        {
            state.ReleaseActivation.TrySetResult();
            state.ReleaseHold.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedOneWayRequest_MigratesWithoutRetargetingItsCaller(bool retireSource)
    {
        var retargets = 0;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
        {
            silo.AddIncomingGrainCallFilter(async context =>
            {
                if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.RetargetMigratedRequests))
                {
                    Interlocked.Increment(ref retargets);
                }

                await context.Invoke();
            });
        }));
        var caller = cluster.Silos[0];
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var locator = caller.ServiceProvider.GetRequiredService<Orleans.Runtime.GrainDirectory.GrainLocator>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());
        Assert.True(locator.TryLookupInCache(grain.GetGrainId(), out var cached));
        Assert.Equal(source.SiloAddress, cached.SiloAddress);

        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        await grain.RecordOneWay();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 1);

        var stopping = Task.CompletedTask;
        if (retireSource)
        {
            stopping = cluster.StopSiloAsync(source, Cancellation);
            await WaitUntilAsync(() => FindActivation(source, grain.GetGrainId()) is ActivationData { State: ActivationState.Deactivating });
        }
        else
        {
            await grain.MigrateOnIdleTo(target.SiloAddress);
        }

        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);

        // 单向请求在新 Silo 上运行，其调用方没有挂起调用需要重定向。
        var destination = await state.OneWayReceived.Task.WaitAsync(Timeout, Cancellation);
        Assert.NotEqual(source.SiloAddress, destination);
        if (!retireSource)
        {
            // 源仍存活时，下一次普通调用会修复调用方的过时路由。
            Assert.Equal(destination, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
            Assert.Equal(target.SiloAddress, destination);
            Assert.True(locator.TryLookupInCache(grain.GetGrainId(), out cached));
            Assert.Equal(destination, cached.SiloAddress);
        }

        await stopping.WaitAsync(Timeout, Cancellation);
        Assert.Equal(0, Volatile.Read(ref retargets));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedRequestsTooLargeToMigrateTogether_AreForwardedToTheMigratedActivation(bool localCaller)
    {
        const int MaxMessageBodySize = 1024 * 1024;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.MaxMessageBodySize = MaxMessageBodySize)));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var grainFactory = (localCaller ? source : cluster.Silos[0]).ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        await PlaceOnAsync(source, () => grain.GetLocation());

        // 每个请求单独都能放入一条消息，但二者合在一起则不能。
        var running = grain.Block().AsTask();
        await state.BlockEntered.Task.WaitAsync(Timeout, Cancellation);
        var first = grain.GetLocationWithPayload(new byte[MaxMessageBodySize * 3 / 5]).AsTask();
        var second = grain.GetLocationWithPayload(new byte[MaxMessageBodySize * 3 / 5]).AsTask();
        await WaitUntilAsync(() => GetWaitingCount(source, grain.GetGrainId()) == 2);

        // Grain 不携带这些请求进行迁移，源保留排队请求，随后将其转发至迁移后的激活。
        await grain.MigrateOnIdleTo(target.SiloAddress);
        state.ReleaseBlock.TrySetResult();
        await running.WaitAsync(Timeout, Cancellation);
        Assert.Equal(target.SiloAddress, await first.WaitAsync(Timeout, Cancellation));
        Assert.Equal(target.SiloAddress, await second.WaitAsync(Timeout, Cancellation));
    }

    [Fact]
    public async Task CarryingRequests_LeavesTheMigrationContextIntact()
    {
        await using var cluster = await DeployAsync();
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);

        // 已处于活跃状态的目标通过普通接纳机制接收迁移携带的请求。
        await PlaceOnAsync(target, () => grain.GetLocation());
        // 大小计算保留出站 RPC 即将序列化的状态。
        var marker = new string('x', 10_000);
        var context = new MigrationContext(source.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        Assert.True(context.TryAddValue("marker", marker));
        using var request = new Message
        {
            Direction = Message.Directions.OneWay,
            Id = new CorrelationId(1),
            SendingGrain = GrainId.Create("test", "caller"),
            TargetGrain = grain.GetGrainId(),
            BodyObject = new byte[16],
        };

        // 迁移管理器的泵在管理器调度器上发送迁移包，因此在那里打包请求的操作在发送迁移包之前完成。
        // 计算迁移包大小不得消耗目标用来重建 Grain 的迁移上下文。
        ActivationMigration? migration = null;
        await migrationManager.RunOrQueueTask(() =>
        {
            migration = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, grain.GetGrainId()), context, [request], Cancellation);
            Assert.NotNull(migration.Package.Requests);
            Assert.True(context.TryGetValue("marker", out string? value));
            Assert.Equal(marker, value);
            return Task.CompletedTask;
        });

        await migration!.Completion.WaitAsync(Timeout, Cancellation);
    }

    [Fact]
    public async Task Migration_RetainsRequestsWhichDoNotFitWithTheMigrationContext()
    {
        const int MaxMessageBodySize = 1024 * 1024;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.MaxMessageBodySize = MaxMessageBodySize)));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        RetirementHandoffState.Create(key);
        var grainId = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName).GetGrainId();

        // 此 Silo 收到的请求单独可以放入一条消息，迁移上下文（大小主要来自其键）也可以，但二者不能合在一起。
        var context = new MigrationContext(source.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        context.TryAddValue(new string('k', MaxMessageBodySize / 2), 0);
        using var request = CreateReceivedRequest(cluster, source, grainId, 1, MaxMessageBodySize * 3 / 5);

        // 迁移携带上下文而不携带请求，改为转发该请求。
        var migration = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, grainId), context, [request], Cancellation);
        Assert.Null(migration.Package.Requests);
        await migration.Completion.WaitAsync(Timeout, Cancellation);
    }

    [Theory]
    [InlineData("serialization")]
    [InlineData("preparation")]
    [InlineData("before-message")]
    [InlineData("discard")]
    public async Task FailedOutgoingMigration_ReleasesCarriedRequests(string failure)
    {
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddOutgoingGrainCallFilter(async context =>
        {
            if (failure is "before-message" or "discard" && context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                if (failure == "discard")
                {
                    using var message = context.SourceContext!.ActivationServices.GetRequiredService<MessageFactory>()
                        .CreateMessage(context.Request, default, requestContextData: null);
                }

                throw new InvalidOperationException("Injected send failure.");
            }

            await context.Invoke();
        })));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        RetirementHandoffState.Create(key);
        var grainId = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName).GetGrainId();

        // 此 Silo 收到的请求在池化读取请求中持有其序列化消息体。其上下文无法序列化。
        using var request = CreateReceivedRequest(cluster, source, grainId, 1, 1);
        using var later = CreateReceivedRequest(cluster, source, grainId, 2, 20_000);
        var page = Assert.IsType<Orleans.Runtime.Messaging.MessageReadRequest>(later._bodyObject).Body.First!;
        var pageVersion = page.Version;
        if (failure == "serialization")
        {
            request.RequestContextData = new() { ["unserializable"] = new UnserializableValue() };
        }
        else if (failure == "preparation")
        {
            request.BodyObject = new UnserializableValue();
        }

        var migrationContext = new MigrationContext(source.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        Assert.True(migrationContext.TryAddValue("marker", new byte[20_000]));

        // 迁移消息无法序列化，因此迁移失败。
        var migration = migrationManager.StartMigration(
            target.SiloAddress,
            CreateSourceAddress(source, grainId),
            migrationContext,
            [request, later],
            Cancellation);
        Assert.NotNull(migration.Package.Requests);
        await Assert.ThrowsAnyAsync<Exception>(() => migration.Completion.WaitAsync(Timeout, Cancellation));
        Assert.Null(FindActivation(target, grainId));

        // 所有者释放携带的所有消息体，包括尚未进入序列化阶段的消息体。
        migration.Deliver();
        if (failure == "preparation")
        {
            Assert.IsType<UnserializableValue>(request._bodyObject);
        }
        else
        {
            Assert.Null(request._bodyObject);
        }
        Assert.Null(later._bodyObject);
        Assert.Empty(migrationContext.Keys);
        Assert.NotEqual(pageVersion, page.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationMessages_TimeoutLeavesClaimedPayloadOwnedByTheSerializer(bool failSerialization)
    {
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Services.AddSingleton<MigrationSerializationCallbacks>()));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var services = source.ServiceProvider;
        var manager = services.GetRequiredService<ActivationMigrationManager>();
        var serializer = services.GetRequiredService<Orleans.Serialization.Serializer>();
        var grainId = GrainId.Create("test", "serialization-ownership");
        var context = new MigrationContext(services.GetRequiredService<SerializerSessionPool>());
        var payload = Enumerable.Repeat((byte)0x5A, 20_000).ToArray();
        Assert.True(context.TryAddValue("payload", payload));
        using var expired = CreateReceivedRequest(cluster, source, grainId, 1, 20_000);
        using var live = CreateReceivedRequest(cluster, source, grainId, 2, 20_000);
        expired.TimeToLive = TimeSpan.FromMinutes(-1);
        live.TimeToLive = TimeSpan.FromMinutes(5);
        var page = Assert.IsType<Orleans.Runtime.Messaging.MessageReadRequest>(live._bodyObject).Body.First!;
        var version = page.Version;
        List<Message> requests = [expired, live];
        var migration = new ActivationMigration(manager, CreateSourceAddress(source, grainId), target.SiloAddress,
            new() { GrainId = grainId, MigrationContext = context, Requests = requests }, requests, routes: null, size: 0, Cancellation);
        Assert.True(migration.TrySend());
        var serializing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseSerialization = new ManualResetEventSlim();
        live.RequestContextData = new()
        {
            ["gate"] = new MigrationSerializationGate
            {
                BeforeSerialization = () =>
                {
                    serializing.TrySetResult();
                    releaseSerialization.Wait(Cancellation);
                    if (failSerialization)
                    {
                        throw new InvalidOperationException("Injected serialization failure.");
                    }
                }
            }
        };
        var writing = Task.Run(() => serializer.SerializeToArray(migration.Package), Cancellation);
        try
        {
            await serializing.Task.WaitAsync(Timeout, Cancellation);
            migration.Complete(new TimeoutException("Acknowledgement lost."));
            await Assert.ThrowsAsync<TimeoutException>(() => migration.Completion);
            Assert.Equal(version, page.Version);
            Assert.True(context.TryGetValue("payload", out byte[]? sourcePayload));
            Assert.Equal(payload, sourcePayload);
            Assert.All(requests, request => Assert.IsType<Orleans.Runtime.Messaging.MessageReadRequest>(request._bodyObject));
        }
        finally
        {
            releaseSerialization.Set();
        }

        if (failSerialization)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => writing.WaitAsync(Timeout, Cancellation));
        }
        else
        {
            var received = serializer.Deserialize<GrainMigrationPackage>(await writing.WaitAsync(Timeout, Cancellation));
            try
            {
                Assert.True(received.MigrationContext.TryGetValue("payload", out byte[]? actual));
                Assert.Equal(payload, actual);
                Assert.Equal(2, received.Requests!.Count);
                Assert.True(received.Requests[0].IsExpired);
                Assert.False(received.Requests[1].IsExpired);
                Assert.InRange(received.Requests[1].TimeToLive!.Value, TimeSpan.Zero, TimeSpan.FromMinutes(5));
                Assert.All(received.Requests, request =>
                    Assert.Equal(new byte[20_000], Assert.IsType<Orleans.Runtime.Messaging.MessageReadRequest>(request._bodyObject).Body.ToArray()));
            }
            finally
            {
                received.MigrationContext.Dispose();
                foreach (var request in received.Requests!)
                {
                    request.Dispose();
                }
            }
        }

        Assert.Null(expired._bodyObject);
        Assert.Null(live._bodyObject);
        Assert.Empty(context.Keys);
        Assert.NotEqual(version, page.Version);
    }

    [Fact]
    public async Task MigrationWithoutRequests_CompletesWithoutWaitingForAnotherGrainsRetargets()
    {
        // 阻塞调用方的重定向，使已被接受的 Grain 的迁移无法完成。
        var retargetArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetarget = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.RetargetMigratedRequests))
            {
                retargetArrived.TrySetResult();
                await releaseRetarget.Task;
            }

            await context.Invoke();
        })));
        var caller = cluster.Silos[0];
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var pool = source.ServiceProvider.GetRequiredService<SerializerSessionPool>();
        var grainFactory = caller.ServiceProvider.GetRequiredService<IGrainFactory>();
        var acceptedKey = Guid.NewGuid();
        var otherKey = Guid.NewGuid();
        RetirementHandoffState.Create(acceptedKey);
        RetirementHandoffState.Create(otherKey);
        var acceptedGrain = grainFactory.GetGrain<IRetirementHandoffGrain>(acceptedKey, typeof(RetirementHandoffGrain).FullName).GetGrainId();
        var otherGrain = grainFactory.GetGrain<IRetirementHandoffGrain>(otherKey, typeof(RetirementHandoffGrain).FullName).GetGrainId();

        // 两次迁移都在泵运行前进入迁移管理器的调度器队列，因此在一个批次中传输。仅其中一次携带请求。
        ActivationMigration? acceptedMigration = null;
        ActivationMigration? otherMigration = null;
        await migrationManager.RunOrQueueTask(() =>
        {
            acceptedMigration = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, acceptedGrain), new MigrationContext(pool), [CreateRequest(acceptedGrain, 1)], Cancellation);
            otherMigration = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, otherGrain), new MigrationContext(pool), requests: null, Cancellation);
            Assert.NotNull(acceptedMigration.Package.Requests);
            Assert.Null(otherMigration.Package.Requests);
            return Task.CompletedTask;
        });

        try
        {
            // 在已被接受的 Grain 的调用方尚未确认重定向时，另一个 Grain 的迁移已完成，因此其源可以完成停用。
            await retargetArrived.Task.WaitAsync(Timeout, Cancellation);
            await otherMigration!.Completion.WaitAsync(Timeout, Cancellation);
            Assert.False(acceptedMigration!.Completion.IsCompleted);
        }
        finally
        {
            releaseRetarget.TrySetResult();
        }

        await acceptedMigration.Completion.WaitAsync(Timeout, Cancellation);

        Message CreateRequest(GrainId grainId, long id) => new()
        {
            Direction = Message.Directions.Request,
            Id = new CorrelationId(id),
            SendingGrain = GrainId.Create("test", "caller"),
            SendingSilo = caller.SiloAddress,
            TargetGrain = grainId,
            BodyObject = new List<string> { "value" },
        };
    }

    [Fact]
    public async Task ActivationInterruptedByRetirement_DoesNotMigrate()
    {
        var migrationsReceived = 0;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) => silo.AddIncomingGrainCallFilter(async context =>
        {
            if (context.Request.GetMethodName() == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains))
            {
                Interlocked.Increment(ref migrationsReceived);
            }

            await context.Invoke();
        })));
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        var grain = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);

        // 激活在 OnActivateAsync 中被阻塞，后面排队等待着一个请求。
        state.HoldActivationOn = retiring.SiloAddress;
        RequestContext.Set(IPlacementDirector.PlacementHintKey, retiring.SiloAddress);
        Task call;
        try
        {
            call = grain.Record("queued").AsTask();
        }
        finally
        {
            RequestContext.Remove(IPlacementDirector.PlacementHintKey);
        }

        await state.ActivationHeld.Task.WaitAsync(Timeout, Cancellation);
        var activation = Assert.IsType<ActivationData>(FindActivation(retiring, grain.GetGrainId()));
        await WaitUntilAsync(() => activation.WaitingCount == 1);

        // 退役中断激活。其状态可能仅完成部分初始化，因此不进行迁移，排队请求仍按原有机制转发或拒绝。
        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        await WaitUntilAsync(() => activation.State is ActivationState.Deactivating or ActivationState.Invalid);
        state.ReleaseActivation.TrySetResult();
        await stopping.WaitAsync(Timeout, Cancellation);
        await call.WaitAsync(Timeout, Cancellation).ContinueWith(static _ => { }, TaskScheduler.Default);
        Assert.Equal(0, Volatile.Read(ref migrationsReceived));
    }

    [Fact]
    public async Task RetiringActivationWithAnOpenExclusiveSection_FaultsWaitingSectionsAndMigratesHeldRequests()
    {
        await using var cluster = await DeployAsync();
        var retiring = cluster.Silos[1];
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = ExclusiveSectionRetirementState.Create(key);
        var grain = grainFactory.GetGrain<IExclusiveSectionRetirementGrain>(key);
        await PlaceOnAsync(retiring, grain.GetLocation);

        // 一个没有调用链的已打开区段阻挡普通请求，另一个区段在第一个之后等待。
        await grain.OpenSection().WaitAsync(Timeout, Cancellation);
        await grain.RequestSection().WaitAsync(Timeout, Cancellation);
        var held = grain.GetLocation().AsTask();
        await WaitUntilAsync(() => GetWaitingCount(retiring, grain.GetGrainId()) == 1);

        // 停用不等待区段中的代码，使等待的区段以异常结束，而被阻塞的请求随
        // 激活迁移，新激活没有已打开的区段。
        var stopping = cluster.StopSiloAsync(retiring, Cancellation);
        Assert.IsType<InvalidOperationException>(await state.SectionWaitFault.Task.WaitAsync(Timeout, Cancellation));
        var destination = await held.WaitAsync(Timeout, Cancellation);
        Assert.NotEqual(retiring.SiloAddress, destination);
        await stopping.WaitAsync(Timeout, Cancellation);
        Assert.Equal(destination, await grain.GetLocation().AsTask().WaitAsync(Timeout, Cancellation));
    }

    private static Message CreateRequest(InProcessTestCluster cluster, GrainId grainId, long id, object body) => new()
    {
        Direction = Message.Directions.Request,
        Id = new CorrelationId(id),
        SendingGrain = GrainId.Create("test", "caller"),
        SendingSilo = cluster.Silos[0].SiloAddress,
        TargetGrain = grainId,
        BodyObject = body,
    };

    private static GrainMigrationPackage UnpackMigration(IIncomingGrainCallContext context)
        => Assert.Single((List<GrainMigrationPackage>)context.Request.GetArgument(0)!);

    private static Orleans.Serialization.Buffers.PooledBuffer CreatePooledBuffer(byte[] contents)
    {
        var buffer = new Orleans.Serialization.Buffers.PooledBuffer();
        buffer.Write(contents);
        return buffer;
    }

    /// <summary>
    /// 创建一个如同由此 Silo 接收的请求，使其持有序列化消息体。
    /// </summary>
    private static Message CreateReceivedRequest(InProcessTestCluster cluster, InProcessSiloHandle silo, GrainId grainId, long id, int bodyLength)
    {
        using var bodyWriter = new Orleans.Serialization.Buffers.ArcBufferWriter();
        bodyWriter.Write(new byte[bodyLength]);
        var readRequest = silo.ServiceProvider.GetRequiredService<Orleans.Runtime.Messaging.MessageHandlerShared>().GetReceiveMessageHandler();
        readRequest.Initialize(default, bodyWriter.ConsumeSlice(bodyLength));
        var request = new Message
        {
            Direction = Message.Directions.Request,
            Id = new CorrelationId(id),
            SendingGrain = GrainId.Create("test", "caller"),
            SendingSilo = cluster.Silos[0].SiloAddress,
            TargetGrain = grainId,
        };
        request.SetMessageReadRequest(readRequest);
        return request;
    }

    private static GrainAddress CreateSourceAddress(InProcessSiloHandle silo, GrainId grainId)
        => new() { GrainId = grainId, SiloAddress = silo.SiloAddress, ActivationId = ActivationId.NewId() };

    private sealed class UnserializableValue;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedMigrationRpc_IsRejectedBeforeAdmission(bool nullContext)
    {
        await using var cluster = await DeployAsync();
        var source = cluster.Silos[0];
        var destination = cluster.Silos[1];
        var sourceShared = source.ServiceProvider.GetRequiredService<MessageHandlerShared>();
        var destinationShared = destination.ServiceProvider.GetRequiredService<MessageHandlerShared>();
        var sessions = source.ServiceProvider.GetRequiredService<SerializerSessionPool>();
        var packages = new List<GrainMigrationPackage>();
        for (var i = 0; i < 3; i++)
        {
            var grainId = GrainId.Create("test", $"malformed-migration-{i}");
            var requests = new List<Message>();
            for (var j = 0; j < (i == 1 ? 2 : 1); j++)
            {
                requests.Add(CreateRequest(cluster, grainId, i * 2 + j + 1, new byte[20_000]));
            }

            if (i == 1 && !nullContext)
            {
                requests.Insert(1, null!);
            }

            packages.Add(new()
            {
                GrainId = grainId,
                MigrationContext = i == 1 && nullContext ? null! : new MigrationContext(sessions),
                Requests = requests,
            });
        }

        var invocationType = typeof(ActivationMigrationManager).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IInvokable).IsAssignableFrom(type)
                && type.Name.StartsWith("Invokable_IActivationMigrationManagerSystemTarget_", StringComparison.Ordinal))
            .Single(type => ((IInvokable)Activator.CreateInstance(type)!).GetMethodName()
                == nameof(IActivationMigrationManagerSystemTarget.AcceptMigratingGrains));
        var invocation = (IInvokable)Activator.CreateInstance(invocationType)!;
        invocation.SetArgument(0, packages);
        invocation.SetArgument(1, CancellationToken.None);
        Message received;
        using (var frame = new ArcBufferWriter())
        {
            using var message = new Message { Direction = Message.Directions.Request, BodyObject = invocation };
            var serializer = sourceShared.GetMessageSerializer();
            try
            {
                MessageFrames.Write(serializer, frame, message);
            }
            finally
            {
                sourceShared.Return(serializer);
            }

            received = MessageFrames.Read(destinationShared, frame);
        }

        using (received)
        {
            try
            {
                var malformed = Assert.Throws<InvalidMessageFrameException>(() => _ = received.BodyObject);
                Assert.Contains(nullContext ? "context" : "null request", malformed.Message);
                Assert.Null(received._bodyObject);
            }
            finally
            {
                foreach (var package in packages)
                {
                    package.MigrationContext?.Dispose();
                }
            }
        }
    }

    [Fact]
    public async Task RejectedMigrationBatch_ReleasesOnlyUnadmittedRequests()
    {
        var control = new MigrationAdmissionControl();
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Services.AddSingleton<IConfigureGrainContextProvider>(control)));
        var destination = cluster.Silos[1];
        var factory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var manager = destination.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        foreach (var failure in new[] { "validation", "cancellation", "construction" })
        {
            using var cancellation = new CancellationTokenSource();
            var packages = new List<GrainMigrationPackage>();
            var requests = new List<Message>();
            var states = new List<RetirementHandoffState>();
            for (var i = 0; i < 3; i++)
            {
                var key = Guid.NewGuid();
                var state = RetirementHandoffState.Create(key);
                state.HoldActivationOn = destination.SiloAddress;
                states.Add(state);
                var grainId = factory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName).GetGrainId();
                if (failure == "construction" && i == 1)
                {
                    grainId = GrainId.Create("missing-migration-grain", key.ToString());
                }
                var request = CreateReceivedRequest(cluster, destination, grainId, i, 20_000);
                requests.Add(request);
                var context = new MigrationContext(destination.ServiceProvider.GetRequiredService<SerializerSessionPool>());
                context.TryAddValue("pooled", new byte[20_000]);
                packages.Add(new() { GrainId = grainId, MigrationContext = context, Requests = [request] });
            }

            control.OnCreate = context =>
            {
                if (failure == "cancellation" && context.GrainId == packages[0].GrainId)
                {
                    cancellation.Cancel();
                }
            };
            if (failure == "validation")
            {
                requests[1].TargetGrain = GrainId.Create("wrong", "target");
            }

            var retained = requests[0]._bodyObject;
            try
            {
                var error = await Record.ExceptionAsync(() => manager.AcceptMigratingGrains(packages, cancellation.Token).AsTask().WaitAsync(Timeout, Cancellation));
                switch (failure)
                {
                    case "validation":
                        Assert.IsType<ArgumentException>(error);
                        Assert.Null(requests[0]._bodyObject);
                        break;
                    case "cancellation":
                        Assert.IsAssignableFrom<OperationCanceledException>(error);
                        break;
                    default:
                        Assert.IsType<InvalidOperationException>(error);
                        Assert.Contains("missing-migration-grain", error.Message);
                        break;
                }

                if (failure != "validation")
                {
                    await states[0].ActivationHeld.Task.WaitAsync(Timeout, Cancellation);
                    Assert.Same(retained, requests[0]._bodyObject);
                    Assert.Equal(1, GetWaitingCount(destination, packages[0].GrainId));
                }

                Assert.Null(requests[1]._bodyObject);
                Assert.Null(requests[2]._bodyObject);
                Assert.All(packages, package => Assert.Empty(package.Requests!));
                Assert.All(packages, package => Assert.Empty(package.MigrationContext.Keys));
            }
            finally
            {
                control.OnCreate = null;
                foreach (var state in states)
                {
                    state.ReleaseActivation.TrySetResult();
                }
            }
        }
    }

    [Theory]
    [InlineData("type")]
    [InlineData("grain")]
    public async Task Activation_HandlesPreviousAddressMetadataDuringRehydration(string invalid)
    {
        var control = new MigrationAdmissionControl();
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Services.AddSingleton<IConfigureGrainContextProvider>(control)));
        var destination = cluster.Silos[1];
        var factory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        state.FailActivationOn = destination.SiloAddress;
        var grain = factory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        var grainId = grain.GetGrainId();
        IGrainContext? captured = null;
        control.OnCreate = context =>
        {
            if (context.GrainId == grainId)
            {
                captured = context;
            }
        };

        var messages = Enumerable.Range(0, 3).Select(id => CreateReceivedRequest(cluster, destination, grainId, id, 20_000)).ToArray();
        using var migrationContext = new MigrationContext(destination.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        var package = new GrainMigrationPackage { GrainId = grainId, MigrationContext = migrationContext, Requests = [.. messages] };
        if (invalid == "grain")
        {
            Assert.True(migrationContext.TryAddValue(ActivationData.GrainAddressMigrationContextKey,
                CreateSourceAddress(cluster.Silos[0], GrainId.Create("wrong", "grain"))));
        }
        else
        {
            Assert.True(migrationContext.TryAddValue(ActivationData.GrainAddressMigrationContextKey, "not a grain address"));
        }

        using var events = new DiagnosticEventCollector(GrainLifecycleEvents.ListenerName);
        var deactivating = events.WaitForEventAsync(nameof(GrainLifecycleEvents.Deactivating),
            e => e.Payload is GrainLifecycleEvents.Deactivating transition && ReferenceEquals(transition.GrainContext, captured), Timeout, Cancellation);
        try
        {
            var catalog = destination.ServiceProvider.GetRequiredService<Catalog>();
            var activation = Assert.IsType<ActivationData>(catalog.GetOrCreateActivation(grainId, requestContextData: null, package));
            Assert.Same(captured, activation);
            control.OnCreate = null;
            Assert.Empty(package.Requests!);

            // 状态重建负责处理元数据错误，其已有的错误处理仍允许 Activate 运行。
            var reason = Assert.IsType<GrainLifecycleEvents.Deactivating>((await deactivating).Payload).Reason;
            Assert.Equal("Destination activation failed.", Assert.IsType<InvalidOperationException>(reason.Exception).Message);
            await activation.Deactivated.WaitAsync(Timeout, Cancellation);
            Assert.Null(activation.PreviousRegistration);
            Assert.Null(FindActivation(destination, grainId));
            Assert.Empty(migrationContext.Keys);
            Assert.All(messages, request => Assert.Null(request._bodyObject));

            state.FailActivationOn = null;
            await PlaceOnAsync(destination, () => grain.GetLocation());
            Assert.NotSame(activation, FindActivation(destination, grainId));
        }
        finally
        {
            control.OnCreate = null;
            if (captured is { } activation)
            {
                activation.Deactivate(new(DeactivationReasonCode.ActivationFailed, new InvalidOperationException("Test cleanup"), "Test cleanup"), CancellationToken.None);
                await activation.Deactivated.WaitAsync(Timeout, Cancellation);
            }

            foreach (var request in messages)
            {
                request.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_CallsActivateAndTheActivationOwnsAdmittedRequests(bool rejectResponseFails)
    {
        var control = new MigrationAdmissionControl();
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
        {
            silo.Services.AddSingleton<IConfigureGrainContextProvider>(control);
            if (rejectResponseFails)
            {
                silo.Configure<SiloMessagingOptions>(options => options.MaxEnqueuedRequestsHardLimit = 1);
            }
        }));
        var silo = cluster.Silos[1];
        var key = Guid.NewGuid();
        var state = RetirementHandoffState.Create(key);
        state.FailActivationOn = silo.SiloAddress;
        var factory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var grain = factory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName);
        var grainId = grain.GetGrainId();
        ActivationData? activation = null;
        control.OnCreate = context =>
        {
            if (context.GrainId == grainId)
            {
                activation = Assert.IsType<ActivationData>(context);
            }
        };
        var messages = Enumerable.Range(0, 3).Select(id => CreateReceivedRequest(cluster, silo, grainId, id, 20_000)).ToArray();
        var bodies = messages.Select(message => message._bodyObject).ToArray();
        using var migrationContext = new MigrationContext(silo.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        var package = new GrainMigrationPackage { GrainId = grainId, MigrationContext = migrationContext, Requests = [.. messages] };
        using var events = new DiagnosticEventCollector(GrainLifecycleEvents.ListenerName);
        var ownedCount = -1;
        object?[]? ownedBodies = null;
        var deactivating = events.WaitForEventAsync(nameof(GrainLifecycleEvents.Deactivating),
            e =>
            {
                if (e.Payload is GrainLifecycleEvents.Deactivating transition && ReferenceEquals(transition.GrainContext, activation))
                {
                    ownedCount = activation!.WaitingCount;
                    ownedBodies = messages.Take(ownedCount).Select(message => message._bodyObject).ToArray();
                    return true;
                }

                return false;
            }, Timeout, Cancellation);
        try
        {
            try
            {
                if (rejectResponseFails)
                {
                    RequestContext.Set("uncopyable-rejection-context", new UnserializableValue());
                }

                var error = Record.Exception(() => silo.ServiceProvider.GetRequiredService<Catalog>()
                    .GetOrCreateActivation(grainId, requestContextData: null, package));
                if (rejectResponseFails)
                {
                    Assert.NotNull(error);
                }
                else
                {
                    Assert.Null(error);
                }
            }
            finally
            {
                RequestContext.Remove("uncopyable-rejection-context");
            }

            var reason = Assert.IsType<GrainLifecycleEvents.Deactivating>((await deactivating).Payload).Reason;
            Assert.Equal(DeactivationReasonCode.ActivationFailed, reason.ReasonCode);
            Assert.Equal("Destination activation failed.", Assert.IsType<InvalidOperationException>(reason.Exception).Message);
            Assert.Equal(rejectResponseFails ? 2 : 3, ownedCount);
            Assert.Equal(bodies.Take(ownedCount), ownedBodies);
            await activation!.Deactivated.WaitAsync(Timeout, Cancellation);
            Assert.Equal(ActivationState.Invalid, activation.State);
            Assert.Equal(0, activation.WaitingCount);
            Assert.Null(FindActivation(silo, grainId));
            Assert.Empty(package.Requests!);
            Assert.Empty(migrationContext.Keys);
            Assert.All(messages, message => Assert.Null(message._bodyObject));

            control.OnCreate = null;
            state.FailActivationOn = null;
            await PlaceOnAsync(silo, () => grain.GetLocation());
            Assert.NotSame(activation, FindActivation(silo, grainId));
        }
        finally
        {
            control.OnCreate = null;
            if (activation is not null)
            {
                activation.Deactivate(new(DeactivationReasonCode.ActivationFailed, new InvalidOperationException("Test cleanup"), "Test cleanup"), CancellationToken.None);
                await activation.Deactivated.WaitAsync(Timeout, Cancellation);
            }

            foreach (var message in messages) message.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SystemTarget_ReleasesMessagesWhichCannotBeInvoked(bool expired)
    {
        await using var cluster = await DeployAsync();
        var silo = cluster.Silos[1];
        var target = silo.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        using var request = CreateReceivedRequest(cluster, silo, target.GrainId, 1, 20_000);
        var page = Assert.IsType<Orleans.Runtime.Messaging.MessageReadRequest>(request._bodyObject).Body.First!;
        var version = page.Version;
        if (expired)
        {
            request.TimeToLive = TimeSpan.FromMilliseconds(-1);
            await target.RunOrQueueTask(() => target.HandleNewRequest(request)).WaitAsync(Timeout, Cancellation);
        }
        else
        {
            request.Direction = Message.Directions.Response;
            target.ReceiveMessage(request);
        }

        Assert.Null(request._bodyObject);
        Assert.NotEqual(version, page.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_AccountsForCurrentMessageHeaders(bool previouslyMigrated)
    {
        const int BodyLimit = 1024 * 1024;
        var frameLength = previouslyMigrated ? 950 : 1047;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.MaxMessageBodySize = BodyLimit)));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var manager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var services = source.ServiceProvider;
        var shared = services.GetRequiredService<Orleans.Runtime.Messaging.MessageHandlerShared>();
        var serializer = shared.GetMessageSerializer();
        var grainId = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>()
            .GetGrain<IRetirementHandoffGrain>(Guid.NewGuid(), typeof(RetirementHandoffGrain).FullName).GetGrainId();
        var requests = new List<Message>();
        using var cancellation = new CancellationTokenSource();
        try
        {
            using var buffer = new Orleans.Serialization.Buffers.ArcBufferWriter();
            var request = CreateRequest(cluster, grainId, 1, new byte[900]);
            Orleans.Runtime.Messaging.MessageFrames.Write(serializer, buffer, request);
            var payloadLength = frameLength - (buffer.Length - 900);
            buffer.Reset();
            for (var i = 0; i < 1000; i++)
            {
                request = CreateRequest(cluster, grainId, i, new byte[payloadLength]);
                Orleans.Runtime.Messaging.MessageFrames.Write(serializer, buffer, request);
                Assert.Equal(frameLength, buffer.Length);
                var received = Orleans.Runtime.Messaging.MessageFrames.Read(shared, buffer);
                if (previouslyMigrated)
                {
                    received.AddToCacheInvalidationHeader(CreateSourceAddress(cluster.Silos[0], grainId), CreateSourceAddress(source, grainId));
                }

                requests.Add(received);
                buffer.Reset();
            }

            // 较早的缓存头部可能使原本符合大小限制的消息超过传输大小上限。
            Assert.True(frameLength * requests.Count < BodyLimit - 1024);
            foreach (var item in requests)
            {
                Orleans.Runtime.Messaging.MessageFrames.Write(serializer, buffer, item);
            }
            Assert.Equal(previouslyMigrated, buffer.Length > BodyLimit);
            ActivationMigration? migration = null;
            await manager.RunOrQueueTask(() =>
            {
                try
                {
                    migration = manager.StartMigration(target.SiloAddress, CreateSourceAddress(source, grainId),
                        new MigrationContext(services.GetRequiredService<SerializerSessionPool>()), requests, cancellation.Token);
                    Assert.Null(migration.Package.Requests);
                }
                finally
                {
                    cancellation.Cancel();
                }

                return Task.CompletedTask;
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => migration!.Completion);
        }
        finally
        {
            shared.Return(serializer);
            foreach (var request in requests)
            {
                request.Dispose();
            }
        }
    }

    [Fact]
    public async Task Migration_RetainsRequestsWhichExceedTheMessageSizeLimitTogether()
    {
        const int MaxMessageBodySize = 1024 * 1024;
        await using var cluster = await DeployAsync(builder => builder.ConfigureSilo((_, silo) =>
            silo.Configure<SiloMessagingOptions>(options => options.MaxMessageBodySize = MaxMessageBodySize)));
        var source = cluster.Silos[1];
        var target = cluster.Silos[2];
        var migrationManager = source.ServiceProvider.GetRequiredService<ActivationMigrationManager>();
        var grainFactory = cluster.Silos[0].ServiceProvider.GetRequiredService<IGrainFactory>();
        var key = Guid.NewGuid();
        RetirementHandoffState.Create(key);
        var grainId = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName).GetGrainId();

        // 此 Silo 收到的各个请求单独可以放入一条消息，但二者合在一起则不能，因此迁移不携带任何一个请求。
        using var first = CreateReceivedRequest(cluster, source, grainId, 1, MaxMessageBodySize * 3 / 5);
        using var second = CreateReceivedRequest(cluster, source, grainId, 2, MaxMessageBodySize * 3 / 5);
        var context = new MigrationContext(source.ServiceProvider.GetRequiredService<SerializerSessionPool>());
        var migration = migrationManager.StartMigration(target.SiloAddress, CreateSourceAddress(source, grainId), context, [first, second], Cancellation);
        Assert.Null(migration.Package.Requests);

        // Grain 不携带这些请求进行迁移。
        await migration.Completion.WaitAsync(Timeout, Cancellation);
    }

    /// <summary>
    /// 将停用及主机关闭超时提高至测试超时之上，使等待这些超时的工作在测试中表现为始终未完成。
    /// </summary>
    private static void ExtendShutdownTimeouts(InProcessTestClusterBuilder builder)
    {
        var timeout = TimeSpan.FromMinutes(10);
        builder.ConfigureSilo((_, silo) => silo.Configure<GrainCollectionOptions>(options => options.DeactivationTimeout = timeout));
        builder.ConfigureSiloHost((_, host) => host.Services.PostConfigure<Microsoft.Extensions.Hosting.HostOptions>(options => options.ShutdownTimeout = timeout));
    }

    private static readonly TimeSpan DeactivationTimeout = TimeSpan.FromSeconds(3);

    private static async Task<InProcessTestCluster> DeployAsync(Action<InProcessTestClusterBuilder>? configure = null)
    {
        var builder = new InProcessTestClusterBuilder(3);
        configure?.Invoke(builder);
        var cluster = builder.Build();
        await cluster.DeployAsync(Cancellation);
        await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(Cancellation);
        await cluster.WaitForClusterManifestToStabilizeAsync().WaitAsync(Cancellation);
        return cluster;
    }

    private static async Task PlaceOnAsync(InProcessSiloHandle silo, Func<ValueTask<SiloAddress>> getLocation)
    {
        RequestContext.Set(IPlacementDirector.PlacementHintKey, silo.SiloAddress);
        try
        {
            Assert.Equal(silo.SiloAddress, await getLocation().AsTask().WaitAsync(Timeout, Cancellation));
        }
        finally
        {
            RequestContext.Remove(IPlacementDirector.PlacementHintKey);
        }
    }

    private static Guid SelectKey(IGrainFactory grainFactory, InProcessSiloHandle observer, InProcessSiloHandle retiring, bool directoryOwnedByRetiringSilo)
    {
        var directory = observer.ServiceProvider.GetRequiredService<Orleans.Runtime.GrainDirectory.LocalGrainDirectory>();
        while (true)
        {
            var key = Guid.NewGuid();
            var grainId = grainFactory.GetGrain<IRetirementHandoffGrain>(key, typeof(RetirementHandoffGrain).FullName).GetGrainId();
            if (retiring.SiloAddress.Equals(directory.CalculateGrainDirectoryPartition(grainId)) == directoryOwnedByRetiringSilo)
            {
                return key;
            }
        }
    }

    private static (GrainId Sender, CorrelationId Id) FindPendingCall(InProcessSiloHandle silo, GrainId target)
    {
        var message = FindPendingCallback(silo, target).Message;
        return (message.SendingGrain, message.Id);
    }

    private static CallbackData FindPendingCallback(InProcessSiloHandle silo, GrainId target, string? methodName = null)
    {
        var callbacks = (ConcurrentDictionary<(GrainId, CorrelationId), CallbackData>)typeof(InsideRuntimeClient)
            .GetField("callbacks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(silo.ServiceProvider.GetRequiredService<InsideRuntimeClient>())!;
        return Assert.Single(callbacks.Values, callback => callback.Message.TargetGrain == target
            && (methodName is null || callback.Message.BodyObject is Orleans.Serialization.Invocation.IInvokable request && request.GetMethodName() == methodName));
    }

    private static Message CreateOneWayRequest(GrainId grainId, long id) => new()
    {
        Direction = Message.Directions.OneWay,
        Id = new CorrelationId(id),
        SendingGrain = GrainId.Create("test", "caller"),
        TargetGrain = grainId,
        TimeToLive = TimeSpan.FromMinutes(1),
    };

    private static IGrainContext? FindActivation(InProcessSiloHandle silo, GrainId grainId)
        => silo.ServiceProvider.GetRequiredService<ActivationDirectory>().FindTarget(grainId);

    private static int GetWaitingCount(InProcessSiloHandle silo, GrainId grainId)
        => FindActivation(silo, grainId) is ActivationData activation ? activation.WaitingCount : 0;

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeLimit = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(timeLimit ?? Timeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}

[GenerateSerializer, Immutable, SerializationCallbacks(typeof(MigrationSerializationCallbacks))]
public sealed class MigrationSerializationGate
{
    [NonSerialized]
    public Action? BeforeSerialization;
}

public sealed class MigrationSerializationCallbacks
{
    public void OnSerializing(MigrationSerializationGate value) => value.BeforeSerialization?.Invoke();
}

public interface IRetirementHandoffGrain : IGrainWithGuidKey
{
    ValueTask<SiloAddress> GetLocation();

    ValueTask Block();

    ValueTask<SiloAddress> HoldThenGetLocation();

    ValueTask HoldUntilCanceled(CancellationToken cancellationToken);

    [OneWay]
    Task RecordOneWay();

    [AlwaysInterleave]
    ValueTask MigrateOnIdleTo(SiloAddress target);

    ValueTask Record(string call);

    ValueTask<SiloAddress> GetLocationWithPayload(byte[] payload);

    ValueTask<byte[]> EchoPooledPayload(Orleans.Serialization.Buffers.PooledBuffer payload);
}

public interface IGenericRetirementHandoffGrain<T> : IGrainWithGuidKey
{
    ValueTask<SiloAddress> GetLocation();

    ValueTask Block();

    ValueTask<SiloAddress> HoldThenGetLocation();
}

[PreferLocalPlacement]
public sealed class GenericRetirementHandoffGrain<T> : Grain, IGenericRetirementHandoffGrain<T>
{
    private RetirementHandoffState State => RetirementHandoffState.Get(this.GetPrimaryKey());

    public ValueTask<SiloAddress> GetLocation() => new(GrainContext.Address.SiloAddress!);

    public async ValueTask Block()
    {
        State.BlockEntered.TrySetResult();
        await State.ReleaseBlock.Task;
    }

    public async ValueTask<SiloAddress> HoldThenGetLocation()
    {
        var location = GrainContext.Address.SiloAddress!;
        State.HoldEntered.TrySetResult(location);
        await State.ReleaseHold.Task;
        return location;
    }
}

public interface IStatelessRetirementHandoffGrain : IGrainWithGuidKey
{
    ValueTask Ping();
}

[StatelessWorker]
public sealed class StatelessRetirementHandoffGrain : Grain, IStatelessRetirementHandoffGrain
{
    public ValueTask Ping() => default;
}

/// <summary>
/// 提供集群清单，其中选定 Silo 的清单已移除 <see cref="IActivationMigrationManagerSystemTarget"/> 的版本信息，
/// 以模拟 Silo 看到运行未对该接口进行版本标记的较早版本的 Silo。
/// </summary>
internal sealed class EarlierVersionManifestProvider(
    IClusterManifestProvider inner,
    GrainInterfaceType migrationInterface,
    ConcurrentDictionary<SiloAddress, bool> earlierVersionSilos) : IClusterManifestProvider
{
    public ClusterManifest Current => Transform(inner.Current);

    public IAsyncEnumerable<ClusterManifest> Updates => inner.Updates;

    public GrainManifest LocalGrainManifest => inner.LocalGrainManifest;

    private ClusterManifest Transform(ClusterManifest manifest)
    {
        if (earlierVersionSilos.IsEmpty)
        {
            return manifest;
        }

        var silos = manifest.Silos;
        foreach (var silo in earlierVersionSilos.Keys)
        {
            if (silos.TryGetValue(silo, out var siloManifest) && siloManifest.Interfaces.TryGetValue(migrationInterface, out var properties))
            {
                var earlierProperties = new GrainInterfaceProperties(properties.Properties.Remove(WellKnownGrainInterfaceProperties.Version));
                silos = silos.SetItem(silo, new GrainManifest(siloManifest.Grains, siloManifest.Interfaces.SetItem(migrationInterface, earlierProperties)));
            }
        }

        return new ClusterManifest(manifest.Version, silos);
    }
}

public interface IDisposalHeldGrain : IGrainWithGuidKey
{
    ValueTask<SiloAddress> GetLocation();

    ValueTask DeactivateNow();
}

[PreferLocalPlacement]
public sealed class DisposalHeldGrain : Grain, IDisposalHeldGrain, IAsyncDisposable
{
    public ValueTask<SiloAddress> GetLocation() => new(GrainContext.Address.SiloAddress!);

    public ValueTask DeactivateNow()
    {
        DeactivateOnIdle();
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        var state = RetirementHandoffState.Get(this.GetPrimaryKey());
        state.DisposeEntered.TrySetResult();
        await state.ReleaseDispose.Task;
    }
}

/// <summary>
/// 观察一个 Silo 开始和结束停止某个生命周期阶段的时机。
/// </summary>
internal sealed class SiloStageObserver : IObserver<Orleans.Runtime.Diagnostics.SiloLifecycleEvents.LifecycleEvent>, IDisposable
{
    private readonly SiloAddress _silo;
    private readonly int _stage;
    private readonly Task _released;
    private readonly IDisposable _subscription;
    private readonly TaskCompletionSource _stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SiloStageObserver(SiloAddress silo, int stage, Task released)
    {
        _silo = silo;
        _stage = stage;
        _released = released;
        _subscription = Orleans.Runtime.Diagnostics.SiloLifecycleEvents.AllEvents.Subscribe(this);
    }

    /// <summary>
    /// 当该阶段的第一个观察者开始停止时完成。
    /// </summary>
    public Task Stopping => _stopping.Task;

    /// <summary>
    /// 阶段停止时完成，并指示释放任务届时是否已完成。
    /// </summary>
    public Task<bool> StoppedAfterRelease => _stopped.Task;

    public void OnNext(Orleans.Runtime.Diagnostics.SiloLifecycleEvents.LifecycleEvent value)
    {
        if (value.Stage != _stage || !_silo.Equals(value.SiloAddress))
        {
            return;
        }

        switch (value)
        {
            case Orleans.Runtime.Diagnostics.SiloLifecycleEvents.ObserverStopping:
                _stopping.TrySetResult();
                break;
            case Orleans.Runtime.Diagnostics.SiloLifecycleEvents.StageStopped:
                _stopped.TrySetResult(_released.IsCompleted);
                break;
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Dispose() => _subscription.Dispose();
}

public interface IImmovableRetirementHandoffGrain : IRetirementHandoffGrain
{
}

public interface IRetirementHandoffCallerGrain : IGrainWithGuidKey
{
    ValueTask<SiloAddress> GetLocation();

    ValueTask<SiloAddress> CallHoldThenGetLocation(Guid targetKey);

    ValueTask<bool> CallHoldUntilCanceled(Guid targetKey);
}

[PreferLocalPlacement]
public sealed class RetirementHandoffCallerGrain : Grain, IRetirementHandoffCallerGrain
{
    public ValueTask<SiloAddress> GetLocation() => new(GrainContext.Address.SiloAddress!);

    public ValueTask<SiloAddress> CallHoldThenGetLocation(Guid targetKey)
        => GrainFactory.GetGrain<IRetirementHandoffGrain>(targetKey, typeof(RetirementHandoffGrain).FullName).HoldThenGetLocation();

    public async ValueTask<bool> CallHoldUntilCanceled(Guid targetKey)
    {
        var target = GrainFactory.GetGrain<IRetirementHandoffGrain>(targetKey, typeof(RetirementHandoffGrain).FullName);
        try
        {
            await target.HoldUntilCanceled(RetirementHandoffState.Get(targetKey).Cancellation.Token);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}

[PreferLocalPlacement]
public class RetirementHandoffGrain : Grain, IRetirementHandoffGrain
{
    private RetirementHandoffState State => RetirementHandoffState.Get(this.GetPrimaryKey());

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (State.FailActivationOn is { } failingSilo && failingSilo.Equals(GrainContext.Address.SiloAddress))
        {
            throw new InvalidOperationException("Destination activation failed.");
        }

        if (State.HoldActivationOn is { } silo && silo.Equals(GrainContext.Address.SiloAddress))
        {
            State.ActivationHeld.TrySetResult();
            await State.ReleaseActivation.Task;
        }
    }

    public ValueTask<SiloAddress> GetLocation() => new(GrainContext.Address.SiloAddress!);

    public ValueTask<SiloAddress> GetLocationWithPayload(byte[] payload) => new(GrainContext.Address.SiloAddress!);

    public ValueTask<byte[]> EchoPooledPayload(Orleans.Serialization.Buffers.PooledBuffer payload)
    {
        var result = payload.ToArray();
        payload.Dispose();
        return new(result);
    }

    public ValueTask Record(string call)
    {
        State.Calls.Enqueue(call);
        return default;
    }

    public async ValueTask Block()
    {
        State.BlockEntered.TrySetResult();
        await State.ReleaseBlock.Task;
    }

    public async ValueTask<SiloAddress> HoldThenGetLocation()
    {
        var location = GrainContext.Address.SiloAddress!;
        Interlocked.Increment(ref State.HoldCount);
        State.HoldEntered.TrySetResult(location);
        await State.ReleaseHold.Task;
        return location;
    }

    public ValueTask MigrateOnIdleTo(SiloAddress target)
    {
        RequestContext.Set(IPlacementDirector.PlacementHintKey, target);
        MigrateOnIdle();
        return default;
    }

    public Task RecordOneWay()
    {
        State.OneWayReceived.TrySetResult(GrainContext.Address.SiloAddress!);
        return Task.CompletedTask;
    }

    public async ValueTask HoldUntilCanceled(CancellationToken cancellationToken)
    {
        State.HoldEntered.TrySetResult(GrainContext.Address.SiloAddress!);
        try
        {
            // 测试释放一个从未被取消的请求，以免测试失败时阻碍 Silo 关闭。
            await State.ReleaseHold.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            State.HoldCanceled.TrySetResult();
            throw;
        }
    }
}

[PreferLocalPlacement, Immovable]
public sealed class ImmovableRetirementHandoffGrain : RetirementHandoffGrain, IImmovableRetirementHandoffGrain
{
}

public sealed class MigrationAdmissionControl : IConfigureGrainContextProvider, IConfigureGrainContext
{
    public Action<IGrainContext>? OnCreate { get; set; }

    public bool TryGetConfigurator(GrainType grainType, GrainProperties properties, out IConfigureGrainContext configurator)
    {
        configurator = this;
        return true;
    }

    public void Configure(IGrainContext context) => OnCreate?.Invoke(context);
}

public sealed class RetirementHandoffState
{
    private static readonly ConcurrentDictionary<Guid, RetirementHandoffState> States = new();

    public TaskCompletionSource BlockEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseBlock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<SiloAddress> HoldEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseHold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource HoldCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationTokenSource Cancellation { get; } = new();
    public TaskCompletionSource<SiloAddress> OneWayReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ActivationHeld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseActivation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<string> Calls { get; } = new();
    public SiloAddress? HoldActivationOn { get; set; }
    public SiloAddress? FailActivationOn { get; set; }
    public int HoldCount;

    public static RetirementHandoffState Create(Guid key) => States[key] = new();

    public static RetirementHandoffState Get(Guid key) => States[key];
}

public interface IExclusiveSectionRetirementGrain : IGrainWithGuidKey
{
    ValueTask<SiloAddress> GetLocation();

    [AlwaysInterleave]
    Task OpenSection();

    [AlwaysInterleave]
    Task RequestSection();
}

public sealed class ExclusiveSectionRetirementGrain : Grain, IExclusiveSectionRetirementGrain
{
    private IDisposable? _section;

    private IGrainRequestAdmission Admission => GrainContext.GetComponent<IGrainRequestAdmission>()!;

    public ValueTask<SiloAddress> GetLocation() => new(GrainContext.Address.SiloAddress!);

    // 请求完成后区段仍保持打开，因为持有它的是非请求代码。
    public async Task OpenSection() => _section = await Admission.EnterExclusiveSectionAsync(CancellationToken.None);

    // 区段在已打开的区段之后等待；等待完成时记录其结果。
    public Task RequestSection()
    {
        var state = ExclusiveSectionRetirementState.Get(this.GetPrimaryKey());
        _ = ObserveAsync(Admission.EnterExclusiveSectionAsync(CancellationToken.None), state);
        return Task.CompletedTask;

        static async Task ObserveAsync(ValueTask<IDisposable> entering, ExclusiveSectionRetirementState state)
        {
            try
            {
                (await entering).Dispose();
                state.SectionWaitFault.TrySetResult(null);
            }
            catch (Exception exception)
            {
                state.SectionWaitFault.TrySetResult(exception);
            }
        }
    }
}

public sealed class ExclusiveSectionRetirementState
{
    private static readonly ConcurrentDictionary<Guid, ExclusiveSectionRetirementState> States = new();

    public TaskCompletionSource<Exception?> SectionWaitFault { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static ExclusiveSectionRetirementState Create(Guid key) => States[key] = new();

    public static ExclusiveSectionRetirementState Get(Guid key) => States[key];
}
