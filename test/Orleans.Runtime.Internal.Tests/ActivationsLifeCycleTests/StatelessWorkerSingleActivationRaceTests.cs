#nullable enable
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.Runtime.Diagnostics;
using Orleans.Serialization.Invocation;
using Orleans.TestingHost;
using Orleans.TestingHost.Diagnostics;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.ActivationsLifeCycleTests;

/// <summary>
/// 工作环所处 <c>StatelessWorkerGrainContext</c> 赛场回归测试
/// 可能制造一个“幽灵”工人,
/// 目录,允许大于配置的 <see cref="StatelessWorkerAttribute.MaxLocalWorkers"/>
/// 激活以在单 silo 相同 grain id. 数据。
/// </summary>
public class StatelessWorkerSingleActivationRaceTests(StatelessWorkerSingleActivationRaceTests.Fixture fixture)
        : IClassFixture<StatelessWorkerSingleActivationRaceTests.Fixture>
{
    public class Fixture : BaseTestClusterFixture
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 1;
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        }

        private class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.Services.AddSingleton<StatelessWorkerSingleActivationTracker>();
                hostBuilder.Services.Configure<StatelessWorkerOptions>(options =>
                {
                    options.RemoveIdleWorkers = true;
                    options.IdleWorkersInspectionPeriod = TimeSpan.FromMilliseconds(5);
                    options.MinIdleCyclesBeforeRemoval = 1;
                });
            }
        }
    }

    private readonly Fixture _fixture = fixture;

    private InProcessSiloHandle PrimarySilo => (InProcessSiloHandle)_fixture.HostedCluster.Primary!;

    /// <summary>
    /// 确定性地核实,一旦无国籍工人中最后一名工人
    /// 销毁,上下文标记结束,从激活目录中删除
    /// 并发货迟到,以至于送新包装物,而不复生工人。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("StatelessWorker")]
    public async Task TerminatedContextForwardsLateDeliveryToFreshWrapper()
    {
        using var collector = new DiagnosticEventCollector(StatelessWorkerEvents.ListenerName);
        var directory = PrimarySilo.SiloHost.Services.GetRequiredService<ActivationDirectory>();

        var grain = _fixture.GrainFactory.GetGrain<IStatelessWorkerSingleActivationGrain>(7654321);
        var grainId = ((GrainReference)grain).GrainId;

        await grain.DoWork();

        var c1 = Assert.IsType<StatelessWorkerGrainContext>((await WaitForWorkerCreatedAsync(collector, grainId)).Context);
        c1.Deactivate(new DeactivationReason(DeactivationReasonCode.RuntimeRequested, "test"), CancellationToken.None);

        var terminated = await WaitForContextTerminatedAsync(collector, c1);
        Assert.Equal(0, terminated.WorkerCount);
        Assert.Null(directory.FindTarget(grainId));

        collector.Clear();

        var lateDelivery = new DoWorkRequest();
        c1.ReceiveMessage(CreateLateDeliveryMessage(grainId, lateDelivery));

        var forwarded = await WaitForMessageForwardedAsync(collector, c1);
        var workerCreated = await WaitForWorkerCreatedAsync(collector, forwarded.ReplacementContext);
        await AssertCompletedSuccessfullyAsync(lateDelivery);

        Assert.NotSame(c1, forwarded.ReplacementContext);
        Assert.Same(forwarded.ReplacementContext, workerCreated.Context);
        Assert.Empty(GetWorkerCreatedEvents(collector, c1));
    }

    /// <summary>
    /// 将迟交的结合到
    /// 带新包装的孤儿 grain 通过目录呼叫。 两种操作
    /// 必须使用相同的替换激活, 以便 <c>maxLocalWorkers: 1</c> 是
    /// 即使当终止的包装件收到另一则消息时, 也仍然保存 。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [Fact, TestCategory("BVT"), TestCategory("StatelessWorker")]
    public async Task LateDeliveryAndFreshCallUseSingleReplacementActivation()
    {
        using var collector = new DiagnosticEventCollector(StatelessWorkerEvents.ListenerName);
        var tracker = PrimarySilo.SiloHost.Services.GetRequiredService<StatelessWorkerSingleActivationTracker>();
        var directory = PrimarySilo.SiloHost.Services.GetRequiredService<ActivationDirectory>();

        var grain = _fixture.GrainFactory.GetGrain<IStatelessWorkerSingleActivationGrain>(424242);
        var grainId = ((GrainReference)grain).GrainId;

        tracker.Reset();
        await grain.DoWork();

        var c1 = Assert.IsType<StatelessWorkerGrainContext>((await WaitForWorkerCreatedAsync(collector, grainId)).Context);
        c1.Deactivate(new DeactivationReason(DeactivationReasonCode.RuntimeRequested, "test"), CancellationToken.None);
        _ = await WaitForContextTerminatedAsync(collector, c1);

        tracker.Reset();
        collector.Clear();

        var freshCallTask = grain.DoWork();
        var lateDelivery = new DoWorkRequest();
        c1.ReceiveMessage(CreateLateDeliveryMessage(grainId, lateDelivery));
        await freshCallTask;

        var forwarded = await WaitForMessageForwardedAsync(collector, c1);
        _ = await WaitForWorkerCreatedAsync(collector, forwarded.ReplacementContext);
        await AssertCompletedSuccessfullyAsync(lateDelivery);

        var workerCreatedEvents = GetWorkerCreatedEvents(collector);
        var replacementCreations = Assert.Single(workerCreatedEvents);

        Assert.NotSame(c1, forwarded.ReplacementContext);
        Assert.Same(forwarded.ReplacementContext, replacementCreations.Context);
        Assert.Empty(GetWorkerCreatedEvents(collector, c1));
        Assert.True(
            tracker.MaxObserved <= 1,
            $"Observed up to {tracker.MaxObserved} concurrent activations after a late delivery and a fresh call on a [StatelessWorker(maxLocalWorkers:1)] grain; expected at most 1.");
    }

    private Message CreateLateDeliveryMessage(GrainId grainId, DoWorkRequest request)
    {
        var services = PrimarySilo.SiloHost.Services;
        var messageFactory = services.GetRequiredService<MessageFactory>();
        var localSiloDetails = services.GetRequiredService<ILocalSiloDetails>();

        // 仅当地信息含有 ILocalRequest,接收发送结果。
        var message = messageFactory.CreateMessage(request, InvokeMethodOptions.OneWay);
        message.SetInfiniteTimeToLive();
        message.IsLocalOnly = true;
        message.SendingGrain = grainId;
        message.TargetGrain = grainId;
        message.SendingSilo = localSiloDetails.SiloAddress;
        message.TargetSilo = localSiloDetails.SiloAddress;
        return message;
    }

    private static async Task AssertCompletedSuccessfullyAsync(DoWorkRequest request)
    {
        var response = await request.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(response.Exception);
    }

    private static IReadOnlyList<StatelessWorkerEvents.WorkerCreated> GetWorkerCreatedEvents(DiagnosticEventCollector collector)
    {
        return [.. collector.Events
            .Select(static evt => evt.Payload)
            .OfType<StatelessWorkerEvents.WorkerCreated>()];
    }

    private static IReadOnlyList<StatelessWorkerEvents.WorkerCreated> GetWorkerCreatedEvents(DiagnosticEventCollector collector, IGrainContext context)
    {
        return [.. GetWorkerCreatedEvents(collector)
            .Where(evt => ReferenceEquals(evt.Context, context))];
    }

    private static async Task<StatelessWorkerEvents.ContextTerminated> WaitForContextTerminatedAsync(
        DiagnosticEventCollector collector,
        IGrainContext context)
    {
        var evt = await collector.WaitForEventAsync(
            nameof(StatelessWorkerEvents.ContextTerminated),
            diagnosticEvent => diagnosticEvent.Payload is StatelessWorkerEvents.ContextTerminated terminated
                && ReferenceEquals(terminated.Context, context),
            TimeSpan.FromSeconds(10));
        return Assert.IsType<StatelessWorkerEvents.ContextTerminated>(evt.Payload);
    }

    private static async Task<StatelessWorkerEvents.MessageForwarded> WaitForMessageForwardedAsync(
        DiagnosticEventCollector collector,
        IGrainContext context)
    {
        var evt = await collector.WaitForEventAsync(
            nameof(StatelessWorkerEvents.MessageForwarded),
            diagnosticEvent => diagnosticEvent.Payload is StatelessWorkerEvents.MessageForwarded forwarded
                && ReferenceEquals(forwarded.Context, context),
            TimeSpan.FromSeconds(10));
        return Assert.IsType<StatelessWorkerEvents.MessageForwarded>(evt.Payload);
    }

    private static async Task<StatelessWorkerEvents.WorkerCreated> WaitForWorkerCreatedAsync(
        DiagnosticEventCollector collector,
        GrainId grainId)
    {
        var evt = await collector.WaitForEventAsync(
            nameof(StatelessWorkerEvents.WorkerCreated),
            diagnosticEvent => diagnosticEvent.Payload is StatelessWorkerEvents.WorkerCreated workerCreated
                && workerCreated.GrainId.Equals(grainId),
            TimeSpan.FromSeconds(10));
        return Assert.IsType<StatelessWorkerEvents.WorkerCreated>(evt.Payload);
    }

    private static async Task<StatelessWorkerEvents.WorkerCreated> WaitForWorkerCreatedAsync(
        DiagnosticEventCollector collector,
        IGrainContext context)
    {
        var evt = await collector.WaitForEventAsync(
            nameof(StatelessWorkerEvents.WorkerCreated),
            diagnosticEvent => diagnosticEvent.Payload is StatelessWorkerEvents.WorkerCreated workerCreated
                && ReferenceEquals(workerCreated.Context, context),
            TimeSpan.FromSeconds(10));
        return Assert.IsType<StatelessWorkerEvents.WorkerCreated>(evt.Payload);
    }

    private sealed class DoWorkRequest : ILocalRequest
    {
        private static readonly MethodInfo Method = typeof(IStatelessWorkerSingleActivationGrain).GetMethod(nameof(IStatelessWorkerSingleActivationGrain.DoWork))!;
        private readonly TaskCompletionSource<Response> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IStatelessWorkerSingleActivationGrain? _target;

        public Task<Response> Completion => _completion.Task;

        public void Complete(Response response) => _completion.TrySetResult(response);

        public object? GetTarget() => _target;

        public void SetTarget(ITargetHolder holder)
        {
            _target = (IStatelessWorkerSingleActivationGrain)holder.GetTarget()!;
        }

        public async ValueTask<Response> Invoke()
        {
            try
            {
                await _target!.DoWork();
                return Response.FromResult<object?>(null);
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        public int GetArgumentCount() => 0;

        public object? GetArgument(int index) => throw new ArgumentOutOfRangeException(nameof(index));

        public void SetArgument(int index, object value) => throw new ArgumentOutOfRangeException(nameof(index));

        public string GetMethodName() => nameof(IStatelessWorkerSingleActivationGrain.DoWork);

        public string GetInterfaceName() => typeof(IStatelessWorkerSingleActivationGrain).FullName!;

        public string GetActivityName() => $"{GetInterfaceName()}/{GetMethodName()}";

        public MethodInfo GetMethod() => Method;

        public Type GetInterfaceType() => typeof(IStatelessWorkerSingleActivationGrain);

        public void Dispose()
        {
        }
    }
}
