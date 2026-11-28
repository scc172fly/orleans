using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace UnitTests.MessageScheduling;

/// <summary>
/// 验证由 <see cref="IGrainRequestAdmission.EnterExclusiveSectionAsync"/> 打开的独占区段：区段打开期间，
/// 激活仅启动始终可交错执行的请求以及持有者调用链上的请求，且区段
/// 按请求顺序逐个打开。
/// </summary>
[TestSuite("BVT"), TestProvider("None"), TestArea("Runtime"), TestCategory("BVT")]
public class ExclusiveSectionTests(ExclusiveSectionTests.Fixture fixture) : IClassFixture<ExclusiveSectionTests.Fixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Section_HoldsBackOrdinaryRequest_UntilDisposed()
    {
        var (grain, log) = fixture.CreateGrain();
        await Run(grain.OpenSection("a", allowCallChainReentrancy: false));

        var ping = grain.Ping("p");
        await fixture.AssertHeldAsync(grain, log, "p");

        await Run(grain.CloseSection("a"));
        await Run(ping);
        Assert.Equal(["a:open", "barrier:p", "a:closing", "p"], log.Snapshot());
    }

    [Fact]
    public async Task Section_StartsAlwaysInterleaveRequest()
    {
        var (grain, log) = fixture.CreateGrain();
        await Run(grain.OpenSection("a", allowCallChainReentrancy: false));

        await Run(grain.InterleavingPing("i"));

        await Run(grain.CloseSection("a"));
        Assert.Equal(["a:open", "i", "a:closing"], log.Snapshot());
    }

    [Fact]
    public async Task Sections_OpenOneAtATime_InRequestOrder()
    {
        var (grain, log) = fixture.CreateGrain();
        await Run(grain.OpenSection("a", allowCallChainReentrancy: false));
        await Run(grain.RequestSectionFromTimer("b"));
        await Run(grain.RequestSectionFromScheduler("c"));

        await Run(grain.CloseSection("a"));
        await Run(log.WaitAsync("b:open"));
        await Run(grain.CloseSection("b"));
        await Run(log.WaitAsync("c:open"));
        await Run(grain.CloseSection("c"));

        Assert.Equal(
            ["a:open", "b:requested", "c:requested", "a:closing", "b:open", "b:closing", "c:open", "c:closing"],
            log.Snapshot());
    }

    [Fact]
    public async Task CanceledWait_Throws_AndExitSkipsIt()
    {
        var (grain, log) = fixture.CreateGrain();
        await Run(grain.OpenSection("a", allowCallChainReentrancy: false));
        await Run(grain.RequestCanceledSection("x"));
        await Run(grain.RequestSectionFromScheduler("c"));
        var ping = grain.Ping("p");
        await fixture.AssertHeldAsync(grain, log, "p");

        await Run(grain.CloseSection("a"));
        await Run(log.WaitAsync("c:open"));
        await fixture.AssertHeldAsync(grain, log, "p", barrier: "barrier:p2");
        await Run(grain.CloseSection("c"));
        await Run(ping);

        Assert.Equal(
            ["a:open", "x:requested", "x:canceled", "c:requested", "barrier:p", "a:closing", "c:open", "barrier:p2", "c:closing", "p"],
            log.Snapshot());
        Assert.IsAssignableFrom<OperationCanceledException>(log.Faults["x"]);
    }

    [Fact]
    public async Task CanceledWait_LeavesSectionToClose()
    {
        var (grain, log) = fixture.CreateGrain();
        await Run(grain.OpenSection("a", allowCallChainReentrancy: false));
        await Run(grain.RequestCanceledSection("x"));
        var ping = grain.Ping("p");
        await fixture.AssertHeldAsync(grain, log, "p");

        await Run(grain.CloseSection("a"));
        await Run(ping);

        Assert.Equal(["a:open", "x:requested", "x:canceled", "barrier:p", "a:closing", "p"], log.Snapshot());
    }

    [Fact]
    public async Task Deactivation_FaultsWaitingSections()
    {
        var (grain, log) = fixture.CreateGrain();
        await Run(grain.OpenSection("a", allowCallChainReentrancy: false));
        await Run(grain.RequestSectionFromScheduler("b"));
        var activation = fixture.GetActivation(grain);

        await Run(grain.Deactivate());
        await Run(log.WaitAsync("b:faulted"));
        await Run(activation.Deactivated);

        Assert.Equal(["a:open", "b:requested", "b:faulted"], log.Snapshot());
        Assert.IsType<InvalidOperationException>(log.Faults["b"]);
    }

    [Fact]
    public async Task Section_StartsCallbackOnHolderCallChain()
    {
        var (grain, log) = fixture.CreateGrain();
        var caller = fixture.GrainFactory.GetGrain<IExclusiveSectionCallerGrain>(Guid.NewGuid());

        await Run(grain.OpenSectionAndCallBack(caller, "cb"));

        Assert.Equal(["cb:open", "cb", "cb:closing"], log.Snapshot());
    }

    [Fact]
    public async Task Section_HoldsBackRequestOnOtherCallChain()
    {
        var (grain, log) = fixture.CreateGrain();
        var caller = fixture.GrainFactory.GetGrain<IExclusiveSectionCallerGrain>(Guid.NewGuid());
        await Run(grain.OpenSection("a", allowCallChainReentrancy: true));

        // 回调携带由此激活上另一个请求打开的可重入区段的调用链。
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        log.CallbackRelease = releaseCallback;
        var call = grain.CallBackInReentrantSection(caller, "b");
        try
        {
            // 外层可交错执行的请求可能在回调存在之前入队。等待调用方收到该请求。
            await Run(log.WaitAsync("b:received"));
            await Run(grain.InterleavingPing("before-callback"));
            Assert.Equal(0, fixture.GetActivation(grain).WaitingCount);
            Assert.DoesNotContain("b", log.Snapshot());

            releaseCallback.TrySetResult();
            await fixture.AssertHeldAsync(grain, log, "b");
        }
        finally
        {
            releaseCallback.TrySetResult();
            await Run(grain.CloseSection("a"));
        }

        await Run(call);
        Assert.Equal(["a:open", "b:received", "before-callback", "barrier:b", "a:closing", "b"], log.Snapshot());
    }

    [Fact]
    public async Task OpenSection_KeepsActivationFromIdleCollection()
    {
        var (grain, _) = fixture.CreateGrain();
        await Run(grain.OpenSection("a", allowCallChainReentrancy: false));
        var activation = fixture.GetActivation(grain);

        await fixture.WaitForNoRequestsAsync(activation);
        Assert.False(activation.IsInactive);

        await Run(grain.CloseSection("a"));
        await fixture.WaitForNoRequestsAsync(activation);
        Assert.True(activation.IsInactive);
    }

    private static Task Run(Task task) => task.WaitAsync(Timeout, Cancellation);

    public sealed class Fixture : BaseInProcessTestClusterFixture
    {
        private ExclusiveSectionProbe Probe => HostedCluster.Silos[0].ServiceProvider.GetRequiredService<ExclusiveSectionProbe>();

        public (IExclusiveSectionGrain Grain, ExclusiveSectionLog Log) CreateGrain()
        {
            var key = Guid.NewGuid();
            return (GrainFactory.GetGrain<IExclusiveSectionGrain>(key), Probe.GetLog(key));
        }

        internal ActivationData GetActivation(IExclusiveSectionGrain grain)
        {
            Assert.True(HostedCluster.TryGetGrainContext(grain.GetGrainId(), out var context));
            return Assert.IsType<ActivationData>(context);
        }

        /// <summary>
        /// 断言记录 <paramref name="name"/> 的请求进入等待后，激活会阻止它执行。
        /// </summary>
        /// <remarks>
        /// 激活按到达顺序评估等待中的请求，因此当它启动屏障请求时（该请求始终可交错执行，
        /// 并在被阻塞的请求开始等待后发送），它已评估过被阻塞的请求。
        /// </remarks>
        public async Task AssertHeldAsync(IExclusiveSectionGrain grain, ExclusiveSectionLog log, string name, string? barrier = null)
        {
            var activation = GetActivation(grain);
            await WaitUntilAsync(() => activation.WaitingCount > 0, $"request '{name}' to wait on {grain.GetGrainId()}");

            await Run(grain.InterleavingPing(barrier ?? $"barrier:{name}"));
            Assert.DoesNotContain(name, log.Snapshot());
            Assert.Equal(1, activation.WaitingCount);
        }

        internal Task WaitForNoRequestsAsync(ActivationData activation) =>
            WaitUntilAsync(() => !activation.IsCurrentlyExecuting && activation.WaitingCount == 0, $"requests to complete on {activation.GrainId}");

        protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 1;
            builder.ConfigureSilo((_, silo) => silo.Services.AddSingleton<ExclusiveSectionProbe>());
        }

        private static async Task WaitUntilAsync(Func<bool> condition, string phase)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.Elapsed > Timeout)
                {
                    throw new TimeoutException($"Timed out after {Timeout} waiting for {phase}.");
                }

                await Task.Delay(10, Cancellation);
            }
        }
    }
}

public sealed class ExclusiveSectionProbe
{
    private readonly ConcurrentDictionary<Guid, ExclusiveSectionLog> _logs = new();

    public ExclusiveSectionLog GetLog(Guid grainKey) => _logs.GetOrAdd(grainKey, _ => new());
}

/// <summary>
/// 一个测试 Grain 按顺序记录的事件，以及区段等待结束时的异常。
/// </summary>
public sealed class ExclusiveSectionLog
{
    public TaskCompletionSource? CallbackRelease { get; set; }
    private readonly List<string> _events = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _recorded = new();

    public ConcurrentDictionary<string, Exception> Faults { get; } = new();

    public void Record(string name)
    {
        lock (_events)
        {
            _events.Add(name);
        }

        Recorded(name).TrySetResult();
    }

    public Task WaitAsync(string name) => Recorded(name).Task;

    public string[] Snapshot()
    {
        lock (_events)
        {
            return [.. _events];
        }
    }

    private TaskCompletionSource Recorded(string name) => _recorded.GetOrAdd(name, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

public interface IExclusiveSectionGrain : IGrainWithGuidKey
{
    Task OpenSection(string name, bool allowCallChainReentrancy);

    [AlwaysInterleave]
    Task CloseSection(string name);

    Task Ping(string name);

    [AlwaysInterleave]
    Task InterleavingPing(string name);

    [AlwaysInterleave]
    Task RequestSectionFromTimer(string name);

    [AlwaysInterleave]
    Task RequestSectionFromScheduler(string name);

    [AlwaysInterleave]
    Task RequestCanceledSection(string name);

    Task OpenSectionAndCallBack(IExclusiveSectionCallerGrain caller, string name);

    [AlwaysInterleave]
    Task CallBackInReentrantSection(IExclusiveSectionCallerGrain caller, string name);

    [AlwaysInterleave]
    Task Deactivate();
}

public interface IExclusiveSectionCallerGrain : IGrainWithGuidKey
{
    Task CallBack(IExclusiveSectionGrain target, string name);
}

public sealed class ExclusiveSectionCallerGrain(ExclusiveSectionProbe probe) : Grain, IExclusiveSectionCallerGrain
{
    public async Task CallBack(IExclusiveSectionGrain target, string name)
    {
        var log = probe.GetLog(target.GetPrimaryKey());
        if (log.CallbackRelease is { } release)
        {
            log.Record($"{name}:received");
            await release.Task;
        }

        await target.Ping(name);
    }
}

public sealed class ExclusiveSectionGrain(ExclusiveSectionProbe probe) : Grain, IExclusiveSectionGrain
{
    // 区段由在激活调度器之外运行的延续任务保存。
    private readonly ConcurrentDictionary<string, IDisposable> _sections = new();

    private ExclusiveSectionLog Log => probe.GetLog(this.GetPrimaryKey());

    private IGrainRequestAdmission Admission => GrainContext.GetComponent<IGrainRequestAdmission>()!;

    public async Task OpenSection(string name, bool allowCallChainReentrancy)
    {
        using var reentrancy = allowCallChainReentrancy ? RequestContext.AllowCallChainReentrancy() : default;
        _sections[name] = await Admission.EnterExclusiveSectionAsync(CancellationToken.None);
        Log.Record($"{name}:open");
    }

    public Task CloseSection(string name)
    {
        Log.Record($"{name}:closing");
        Assert.True(_sections.TryRemove(name, out var section));
        section.Dispose();
        return Task.CompletedTask;
    }

    public Task Ping(string name)
    {
        Log.Record(name);
        return Task.CompletedTask;
    }

    public Task InterleavingPing(string name) => Ping(name);

    public Task RequestSectionFromTimer(string name)
    {
        this.RegisterGrainTimer(
            () => RunSectionAsync(name),
            new GrainTimerCreationOptions { DueTime = TimeSpan.Zero, Period = Timeout.InfiniteTimeSpan, Interleave = true });
        return Log.WaitAsync($"{name}:requested");
    }

    public Task RequestSectionFromScheduler(string name)
    {
        _ = Task.Factory.StartNew(() => RunSectionAsync(name), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Current).Unwrap();
        return Log.WaitAsync($"{name}:requested");
    }

    public async Task RequestCanceledSection(string name)
    {
        using var cancellation = new CancellationTokenSource();
        var entering = Admission.EnterExclusiveSectionAsync(cancellation.Token);
        Log.Record($"{name}:requested");
        cancellation.Cancel();
        try
        {
            _sections[name] = await entering;
            Log.Record($"{name}:open");
        }
        catch (Exception exception)
        {
            Log.Faults[name] = exception;
            Log.Record($"{name}:canceled");
        }
    }

    public async Task OpenSectionAndCallBack(IExclusiveSectionCallerGrain caller, string name)
    {
        using var reentrancy = RequestContext.AllowCallChainReentrancy();
        using (await Admission.EnterExclusiveSectionAsync(CancellationToken.None))
        {
            Log.Record($"{name}:open");
            await caller.CallBack(this.AsReference<IExclusiveSectionGrain>(), name);
            Log.Record($"{name}:closing");
        }
    }

    public async Task CallBackInReentrantSection(IExclusiveSectionCallerGrain caller, string name)
    {
        using var reentrancy = RequestContext.AllowCallChainReentrancy();
        await caller.CallBack(this.AsReference<IExclusiveSectionGrain>(), name);
    }

    public Task Deactivate()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    // 从非 Grain 方法的代码中请求区段，并在打开区段或等待以异常结束时记录结果。
    private Task RunSectionAsync(string name)
    {
        var log = Log;
        var entering = Admission.EnterExclusiveSectionAsync(CancellationToken.None).AsTask();
        log.Record($"{name}:requested");

        // 延续任务在线程池上运行，因此即使激活调度器停止运行其工作，也仍会执行。
        return entering.ContinueWith(
            task =>
            {
                if (task.Exception is { } exception)
                {
                    log.Faults[name] = exception.InnerException!;
                    log.Record($"{name}:faulted");
                    return;
                }

                _sections[name] = task.Result;
                log.Record($"{name}:open");
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }
}
