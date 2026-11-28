using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks;

/// <summary>
/// Grain 上现有持久化任务的引用，由
/// <see cref="DurableTaskGrainExtensions.GetDurableTask{TResult}(IAddressable, TaskId)"/> 返回。
/// </summary>
/// <typeparam name="TResult">任务的结果类型。</typeparam>
/// <remarks>
/// <para>
/// 在持久化执行中，该引用是一个持久化任务定义：等待、调度该引用，或将它传给
/// 组合器，都会将执行的一个子任务订阅到目标任务的结果，并由执行记录该操作以供重放。
/// </para>
/// <para>
/// 在持久化执行之外，等待该引用就是等待任务的结果；其轮询成员（例如
/// <see cref="GetResponseAsync(CancellationToken)"/>、<see cref="WaitAsync(CancellationToken)"/> 和
/// <see cref="GetStatusAsync"/>）以及控制成员
/// <see cref="CancelAsync(string, CancellationToken)"/> 和 <see cref="AbortAsync"/> 都会调用任务所属的 Grain。等待和
/// 轮询会重试不影响任务本身的失败，例如连接丢失、Silo 崩溃或正在停用的
/// 激活实例，直到 Grain 响应或调用方的令牌终止它们。持久化执行实例不会记录这些调用。
/// </para>
/// <para>
/// 可通过扩展方法 <see cref="DurableTaskReferenceExtensions.GetAwaiter{TResult}"/> 等待此引用，
/// 因此检查返回类型实例成员的框架（例如 ASP.NET Core Minimal API）会将该
/// 引用视为值。<see cref="IDurableTaskReference"/> 可以读取其 <see cref="Target"/> 和 <see cref="Id"/>，
/// 无需获知其结果类型。
/// </para>
/// </remarks>
public sealed class DurableTaskReference<TResult> : DurableTask<TResult>, IDurableTaskSubscription
{
    private readonly IAddressable _target;
    private ScheduledTask<TResult>? _handle;

    internal DurableTaskReference(IAddressable target, TaskId taskId)
    {
        _target = target;
        Target = target.GetGrainId();
        Id = taskId;
    }

    /// <summary>获取所引用任务的标识符。</summary>
    public TaskId Id { get; }

    /// <summary>获取拥有所引用任务的 Grain。</summary>
    public GrainId Target { get; }

    /// <summary>获取所引用任务的地址，代码可以存储或发送此地址以便之后访问任务。</summary>
    public DurableTaskAddress Address => new(Target, Id);

    private ScheduledTask<TResult> Handle =>
        _handle ??= new AttachedScheduledTask<TResult>(Id, Target, _target.AsReference<IDurableTaskGrainExtension>());

    /// <inheritdoc cref="DurableTaskReferenceExtensions.GetAwaiter{TResult}"/>
    internal DurableTaskReferenceAwaiter<TResult> CreateAwaiter() =>
        DurableTaskContext.TryGetCurrent(out _)
            ? new(DurableTaskExtensions.GetAwaiter(this))
            : new(Handle.GetAwaiter());

    /// <summary>等待任务的终态响应。</summary>
    /// <param name="cancellationToken">用于放弃等待的令牌，不会取消任务本身。</param>
    /// <exception cref="DurableTaskNotFoundException">Grain 未保留具有此标识符的任务。</exception>
    public Task<DurableTaskResponse> GetResponseAsync(CancellationToken cancellationToken = default) =>
        Handle.GetResponseAsync(cancellationToken);

    /// <summary>等待任务的成功结果。</summary>
    /// <param name="cancellationToken">用于放弃等待的令牌，不会取消任务本身。</param>
    /// <returns>一个可等待对象，返回结果或抛出任务的失败或取消异常。</returns>
    /// <remarks>
    /// 等待操作调用任务所属的 Grain。在持久化执行中，应等待该引用本身，或将其传给
    /// <c>WaitAsync(TimeSpan)</c>，以便执行记录此等待操作。
    /// </remarks>
    public ConfiguredScheduledTaskAwaitable<TResult> WaitAsync(CancellationToken cancellationToken = default) =>
        Handle.WaitAsync(cancellationToken);

    /// <summary>轮询任务，并返回它是否已进入终态。</summary>
    /// <param name="options">轮询选项。</param>
    /// <param name="cancellationToken">用于放弃轮询的令牌。</param>
    /// <exception cref="DurableTaskNotFoundException">Grain 未保留具有此标识符的任务。</exception>
    public Task<bool> IsCompletedAsync(PollingOptions options = default, CancellationToken cancellationToken = default) =>
        Handle.IsCompletedAsync(options, cancellationToken);

    /// <summary>通过 <paramref name="options"/> 轮询并等待任务的终态响应。</summary>
    /// <param name="options">每次轮询的选项。</param>
    /// <param name="cancellationToken">用于放弃等待的令牌，不会取消任务本身。</param>
    /// <exception cref="DurableTaskNotFoundException">Grain 未保留具有此标识符的任务。</exception>
    public Task<DurableTaskResponse> GetResponseAsync(PollingOptions options, CancellationToken cancellationToken = default) =>
        Handle.GetResponseAsync(options, cancellationToken);

    /// <summary>轮询任务并返回其当前状态。</summary>
    /// <param name="options">轮询选项。</param>
    /// <param name="cancellationToken">用于放弃轮询的令牌。</param>
    /// <exception cref="DurableTaskNotFoundException">Grain 未保留具有此标识符的任务。</exception>
    public Task<DurableTaskStatus> GetStatusAsync(PollingOptions options = default, CancellationToken cancellationToken = default) =>
        Handle.GetStatusAsync(options, cancellationToken);

    /// <summary>请求对任务执行持久化取消。</summary>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    public ValueTask CancelAsync(CancellationToken cancellationToken = default) => Handle.CancelAsync(cancellationToken);

    /// <summary>请求对任务执行持久化取消，并携带 <paramref name="reason"/>。</summary>
    /// <param name="reason">
    /// 任务的 <see cref="DurableTaskContext.TaskCancellationReason"/> 所报告的原因；若无原因，则为 <see langword="null"/>。
    /// 第一次请求决定该原因。
    /// </param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken = default) =>
        Handle.CancelAsync(reason, cancellationToken);

    /// <summary>
    /// 对任务执行管理性中止，使其及其执行树以失败状态进入终态，并以
    /// <see cref="DurableTaskAbortedException"/> 报告失败，而不再执行任何任务代码。
    /// </summary>
    /// <param name="reason">作为异常消息记录的中止原因。</param>
    /// <param name="cancellationToken">用于放弃等待中止操作提交的令牌。</param>
    public ValueTask AbortAsync(string reason, CancellationToken cancellationToken = default) =>
        Handle.AbortAsync(reason, cancellationToken);

    /// <inheritdoc/>
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) =>
        throw new InvalidOperationException(
            "A durable task reference is scheduled by the durable task runtime of a grain, which subscribes to the referenced task.");
}

/// <summary>
/// 等待 <see cref="DurableTaskReference{TResult}"/>：在持久化执行实例中作为订阅子任务等待，否则通过
/// 等待任务的结果来实现。
/// </summary>
/// <typeparam name="TResult">任务的结果类型。</typeparam>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Awaiters are compiler protocol values whose identity and equality are not part of their contract.")]
public readonly struct DurableTaskReferenceAwaiter<TResult> : ICriticalNotifyCompletion
{
    private readonly DurableTaskAwaiter<TResult> _subscription;
    private readonly ScheduledTaskAwaiter<TResult> _wait;
    private readonly bool _subscribes;

    internal DurableTaskReferenceAwaiter(DurableTaskAwaiter<TResult> subscription)
    {
        _subscription = subscription;
        _wait = default;
        _subscribes = true;
    }

    internal DurableTaskReferenceAwaiter(ScheduledTaskAwaiter<TResult> wait)
    {
        _subscription = default;
        _wait = wait;
        _subscribes = false;
    }

    /// <inheritdoc cref="DurableTaskAwaiter{TResult}.IsCompleted"/>
    public bool IsCompleted => _subscribes ? _subscription.IsCompleted : _wait.IsCompleted;

    /// <inheritdoc/>
    public void OnCompleted(Action continuation)
    {
        if (_subscribes)
        {
            _subscription.OnCompleted(continuation);
        }
        else
        {
            _wait.OnCompleted(continuation);
        }
    }

    /// <inheritdoc/>
    public void UnsafeOnCompleted(Action continuation)
    {
        if (_subscribes)
        {
            _subscription.UnsafeOnCompleted(continuation);
        }
        else
        {
            _wait.UnsafeOnCompleted(continuation);
        }
    }

    /// <summary>返回任务的成功结果，或抛出任务的失败或取消异常。</summary>
    public TResult GetResult() => _subscribes ? _subscription.GetResult() : _wait.GetResult();
}

/// <summary>等待现有持久化任务的引用。</summary>
public static class DurableTaskReferenceExtensions
{
    /// <summary>
    /// 获取一个等待器；在持久化执行实例中，它会使执行实例的一个子任务订阅任务结果，并
    /// 在其他情况下等待任务的结果。
    /// </summary>
    /// <typeparam name="TResult">任务的结果类型。</typeparam>
    /// <param name="reference">任务的引用。</param>
    public static DurableTaskReferenceAwaiter<TResult> GetAwaiter<TResult>(this DurableTaskReference<TResult> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.CreateAwaiter();
    }
}

/// <summary>
/// Grain 上现有持久化任务的引用，例如在不知道其结果类型时读取的 <see cref="DurableTaskReference{TResult}"/>，
/// 或 <see cref="DurableTaskAddress"/>。
/// </summary>
/// <remarks>
/// 若 <see cref="ScheduledTask"/> 的任务可由其他参与方访问，则该句柄通过
/// <see cref="ScheduledTask.GetFeature{TFeature}"/> 将任务引用作为一项功能提供，
/// <see cref="DurableTaskAddressExtensions.TryGetAddress(ScheduledTask, out DurableTaskAddress)"/> 可读取该引用。
/// </remarks>
public interface IDurableTaskReference
{
    /// <summary>获取所引用任务的标识符。</summary>
    TaskId Id { get; }

    /// <summary>获取拥有所引用任务的 Grain。</summary>
    GrainId Target { get; }
}

/// <summary>对现有任务的引用，持久化任务运行时将其调度为订阅子任务。</summary>
internal interface IDurableTaskSubscription : IDurableTaskReference
{
}
