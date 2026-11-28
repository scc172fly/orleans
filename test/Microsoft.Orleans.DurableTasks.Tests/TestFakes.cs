#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 在整个 Microsoft.Orleans.DurableTasks.Tests 项目中复用的共享手写测试替身。
/// 沿用 test/Orleans.Journaling.Tests/JournalBatchTests.cs (TestGrainContext) 中使用的模式。
/// </summary>
internal sealed class TestGrainContext(GrainId grainId) : IGrainContext
{
    public GrainReference GrainReference => throw new NotImplementedException();
    public GrainId GrainId => grainId;
    public object? GrainInstance => throw new NotImplementedException();
    public ActivationId ActivationId => throw new NotImplementedException();
    public GrainAddress Address => throw new NotImplementedException();
    public IServiceProvider ActivationServices => throw new NotImplementedException();
    public IGrainLifecycle ObservableLifecycle { get; } = new TestGrainLifecycle();
    public IWorkItemScheduler Scheduler => throw new NotImplementedException();
    public Task Deactivated => throw new NotImplementedException();

    public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public bool Equals(IGrainContext? other) => throw new NotImplementedException();
    public TComponent? GetComponent<TComponent>() where TComponent : class => throw new NotImplementedException();
    public object? GetComponent(Type componentType) => throw new NotImplementedException();
    public TTarget? GetTarget<TTarget>() where TTarget : class => throw new NotImplementedException();
    public object? GetTarget() => throw new NotImplementedException();
    public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public void ReceiveMessage(object message) => throw new NotImplementedException();
    public void Rehydrate(IRehydrationContext context) => throw new NotImplementedException();
    public void SetComponent<TComponent>(TComponent? value) where TComponent : class => throw new NotImplementedException();
}

/// <summary>
/// 最小化的 <see cref="IGrainLifecycle"/> 测试替身，只记录订阅而不调用其回调。
/// 足以测试仅在初始化期间调用 <c>Subscribe</c> 的组件（例如生命周期参与者）。
/// </summary>
internal sealed class TestGrainLifecycle : IGrainLifecycle
{
    public List<(string ObserverName, int Stage)> Subscriptions { get; } = [];

    public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
    {
        Subscriptions.Add((observerName, stage));
        return new NoopDisposable();
    }

    public void AddMigrationParticipant(IGrainMigrationParticipant participant) => throw new NotImplementedException();
    public void RemoveMigrationParticipant(IGrainMigrationParticipant participant) => throw new NotImplementedException();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// 简单的 <see cref="IGrainContextAccessor"/> 测试替身，始终返回所提供的上下文。
/// </summary>
internal sealed class TestGrainContextAccessor(IGrainContext context) : IGrainContextAccessor
{
    public IGrainContext GrainContext { get; } = context;
}

/// <summary>
/// 在将请求排队的调度器上运行模拟 Grain 上下文接收的本地请求：
/// 如不可重入的激活实例一样按到达顺序逐个运行，或者如可重入的激活实例一样在请求到达后立即运行。
/// </summary>
internal sealed class TestLocalRequestQueue(bool reentrant)
{
    private readonly Queue<(ILocalRequest Request, TaskScheduler Scheduler)> _queue = new();
    private readonly object _lock = new();
    private bool _running;

    /// <summary>获取已运行的请求数量。</summary>
    public int Completed { get; private set; }

    /// <summary>使模拟对象 <paramref name="context"/> 通过新的队列运行其本地请求。</summary>
    public static TestLocalRequestQueue Attach(IGrainContext context, bool reentrant = false)
    {
        var queue = new TestLocalRequestQueue(reentrant);
        context
            .When(static grainContext => grainContext.ReceiveLocalRequest(Arg.Any<ILocalRequest>(), Arg.Any<LocalRequestOptions>()))
            .Do(call => queue.Enqueue(call.Arg<ILocalRequest>()));
        return queue;
    }

    private void Enqueue(ILocalRequest request)
    {
        if (reentrant)
        {
            Run(request, TaskScheduler.Current, runNext: false);
            return;
        }

        lock (_lock)
        {
            _queue.Enqueue((request, TaskScheduler.Current));
            if (_running)
            {
                return;
            }

            _running = true;
        }

        RunNext();
    }

    private void RunNext()
    {
        (ILocalRequest Request, TaskScheduler Scheduler) next;
        lock (_lock)
        {
            if (!_queue.TryDequeue(out next))
            {
                _running = false;
                return;
            }
        }

        Run(next.Request, next.Scheduler, runNext: true);
    }

    private void Run(ILocalRequest request, TaskScheduler scheduler, bool runNext)
    {
        _ = Task.Factory.StartNew(
            async () =>
            {
                Response response;
                try
                {
                    response = await request.Invoke();
                }
                catch (Exception exception)
                {
                    response = Response.FromException(exception);
                }

                request.Complete(response);
                request.Dispose();
                Completed++;
                if (runNext)
                {
                    RunNext();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            scheduler).Unwrap();
    }
}

/// <summary>在直接驱动运行时的测试中运行执行尝试所调用的运行时操作。</summary>
internal static class DurableTaskGrainRuntimeTestExtensions
{
    /// <summary>按照拥有 <paramref name="decisionId"/> 的执行实例中的执行尝试所采用的方式选择完成项。</summary>
    public static ValueTask<TaskId> SelectCompletionAsync(
        this global::Orleans.Durable.Tasks.Runtime.DurableTaskGrainRuntime runtime,
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken) =>
        runtime.SelectCompletionAsync(
            new global::Orleans.Durable.Tasks.Runtime.GrainDurableTaskContext(decisionId.Parent(), runtime, runtime.GrainContext, TaskScheduler.Current),
            decisionId,
            candidates,
            cancellationToken);

    /// <summary>
    /// 按照收件箱的投递方式，投递 <paramref name="sender"/> 为远程子任务 <paramref name="taskId"/> 发送的完成消息，
    /// 该子任务属于执行树的当前运行；完成消息以该子任务的传输标识符为地址，来自
    /// 被调用方在该运行时的调用方作用域下存储的任务。
    /// </summary>
    public static void AcceptResponseFromInbox(
        this global::Orleans.Durable.Tasks.Runtime.DurableTaskGrainRuntime runtime,
        TaskId taskId,
        DurableTaskResponse response,
        GrainId sender)
    {
        var wireId = runtime.GetWireId(taskId);
        runtime.AcceptResponseFromInbox(
            wireId,
            global::Orleans.Durable.Tasks.Runtime.CallerScope.GetTaskId(runtime.GrainId, wireId),
            response,
            sender);
    }
}
