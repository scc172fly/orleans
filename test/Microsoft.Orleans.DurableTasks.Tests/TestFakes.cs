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
/// 共用、手写测试假冒,在 Microsoft.Orleans.DurableTasks.Tests 项目中重新使用。
/// 镜像测试/Orleans.Journaling.Tests/JournalBatchTests.cs (TestGrainContext)中使用的模式。
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
/// 最小假冒 <see cref="IGrainLifecycle"/> 记录而未引用订阅者,
/// 足以测试部件(e.g使用寿命周期参与者) <c>Subscribe</c> 初始化期间。
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
/// 假的三维亚尔 <see cref="IGrainContextAccessor"/> ,总是返回所提供的上下文。
/// </summary>
internal sealed class TestGrainContextAccessor(IGrainContext context) : IGrainContextAccessor
{
    public IGrainContext GrainContext { get; } = context;
}

/// <summary>
/// 运行替换 grain 上上一个排队的调度器所接收到的本地请求:一次一个
/// 抵达令,如非返回启动一样,或一到达即返回。
/// </summary>
internal sealed class TestLocalRequestQueue(bool reentrant)
{
    private readonly Queue<(ILocalRequest Request, TaskScheduler Scheduler)> _queue = new();
    private readonly object _lock = new();
    private bool _running;

    /// <summary>获得运行的请求数。</summary>
    public int Completed { get; private set; }

    /// <summary>制造 <paramref name="context"/>,一个替代的,通过一个新的队列处理其本地请求。</summary>
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

/// <summary>运行运行时间操作, 运行时操作中尝试呼叫, 从直接驱动运行时间的测试中运行。</summary>
internal static class DurableTaskGrainRuntimeTestExtensions
{
    /// <summary>选择完成作为执行尝试的完成,该执行自有 <paramref name="decisionId"/> 确实如此。</summary>
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
    /// 完成时, <paramref name="sender"/> 发送给该天的远龄子 <paramref name="taskId"/>
    /// 儿童在工作上的任务
    /// 通知时间的呼叫范围下的叫卖店
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
