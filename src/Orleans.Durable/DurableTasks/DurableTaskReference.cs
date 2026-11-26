using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 提及 grain的现有持久任务,
/// <see cref="DurableTaskGrainExtensions.GetDurableTask{TResult}(IAddressable, TaskId)"/> 返回。
/// </summary>
/// <typeparam name="TResult">任务结果类型。</typeparam>
/// <remarks>
/// <para>
/// 在持久执行中,参考是长期任务定义:等待、安排或转交
/// 执行结果记录为重放。
/// </para>
/// <para>
/// 等待推荐人等待任务结果,其投票成员,例如:
/// <see cref="GetResponseAsync(CancellationToken)"/>, <see cref="WaitAsync(CancellationToken)"/>,和
/// <see cref="GetStatusAsync"/>及其控制成员,
/// <see cref="CancelAsync(string, CancellationToken)"/> 和 <see cref="AbortAsync"/>,调用任务 grain。
/// 重新测试使任务不受影响而导致的工作失败,例如连接丢失、坠机 silo或停用
/// 激活, 直到 grain 回答或呼叫者的信条结束它们。 耐用执行不会记录这些电话 。
/// </para>
/// <para>
/// 参考文件可通过 <see cref="DurableTaskReferenceExtensions.GetAwaiter{TResult}"/>,一个扩展等待
/// 用于检查返回类型实例成员(例如 ASP.NET 核心最小API)的框架,处理
/// <see cref="IDurableTaskReference"/> 读 <see cref="Target"/> 和 <see cref="Id"/> ,但无其
/// 属性类型。
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

    /// <summary>获取引用任务的身份识别符号。</summary>
    public TaskId Id { get; }

    /// <summary>获取拥有该引用任务的 grain 。</summary>
    public GrainId Target { get; }

    /// <summary>获取引用的任务地址,该地址编码为存储或发送以待日后完成任务。</summary>
    public DurableTaskAddress Address => new(Target, Id);

    private ScheduledTask<TResult> Handle =>
        _handle ??= new AttachedScheduledTask<TResult>(Id, Target, _target.AsReference<IDurableTaskGrainExtension>());

    /// <inheritdoc cref="DurableTaskReferenceExtensions.GetAwaiter{TResult}"/>
    internal DurableTaskReferenceAwaiter<TResult> CreateAwaiter() =>
        DurableTaskContext.TryGetCurrent(out _)
            ? new(DurableTaskExtensions.GetAwaiter(this))
            : new(Handle.GetAwaiter());

    /// <summary>等待任务终端响应。</summary>
    /// <param name="cancellationToken">放弃等待的象征,它不会取消任务</param>
    /// <exception cref="DurableTaskNotFoundException"> grain 与此标识符没有任务。</exception>
    public Task<DurableTaskResponse> GetResponseAsync(CancellationToken cancellationToken = default) =>
        Handle.GetResponseAsync(cancellationToken);

    /// <summary>等待任务成功结果。</summary>
    /// <param name="cancellationToken">放弃等待的象征,它不会取消任务</param>
    /// <returns>返回结果或丢弃任务失败或取消的待处理文件。</returns>
    /// <remarks>
    /// 等待时间呼叫任务 grain。在持久执行中,等待参考文件本身或传递给
    /// <c>WaitAsync(TimeSpan)</c>,因此执行记录了等待。
    /// </remarks>
    public ConfiguredScheduledTaskAwaitable<TResult> WaitAsync(CancellationToken cancellationToken = default) =>
        Handle.WaitAsync(cancellationToken);

    /// <summary>将任务打滚并返回是否为终端。</summary>
    /// <param name="options">民意测验的选择。</param>
    /// <param name="cancellationToken">放弃民意调查的象征</param>
    /// <exception cref="DurableTaskNotFoundException"> grain 与此标识符没有任务。</exception>
    public Task<bool> IsCompletedAsync(PollingOptions options = default, CancellationToken cancellationToken = default) =>
        Handle.IsCompletedAsync(options, cancellationToken);

    /// <summary>等待任务的最终响应、与 <paramref name="options"/>的投票。</summary>
    /// <param name="options">每一次民意测验的选择。</param>
    /// <param name="cancellationToken">放弃等待的象征,它不会取消任务</param>
    /// <exception cref="DurableTaskNotFoundException"> grain 与此标识符没有任务。</exception>
    public Task<DurableTaskResponse> GetResponseAsync(PollingOptions options, CancellationToken cancellationToken = default) =>
        Handle.GetResponseAsync(options, cancellationToken);

    /// <summary>将任务打滚并返回当前状态。</summary>
    /// <param name="options">民意测验的选择。</param>
    /// <param name="cancellationToken">放弃民意调查的象征</param>
    /// <exception cref="DurableTaskNotFoundException"> grain 与此标识符没有任务。</exception>
    public Task<DurableTaskStatus> GetStatusAsync(PollingOptions options = default, CancellationToken cancellationToken = default) =>
        Handle.GetStatusAsync(options, cancellationToken);

    /// <summary>要求持久取消这一任务。</summary>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    public ValueTask CancelAsync(CancellationToken cancellationToken = default) => Handle.CancelAsync(cancellationToken);

    /// <summary>要求持久取消执行的任务 <paramref name="reason"/>.</summary>
    /// <param name="reason">
    /// 任务 <see cref="DurableTaskContext.TaskCancellationReason"/> 报告或 <see langword="null"/>报告的原因
    /// 第一次请求决定了原因。
    /// </param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken = default) =>
        Handle.CancelAsync(reason, cancellationToken);

    /// <summary>
    /// 自动中止任务, 任务终止, 任务结束, 执行树因失败而 停止
    /// <see cref="DurableTaskAbortedException"/> 没有运行其他任务代码 。
    /// </summary>
    /// <param name="reason">中止的原因,记录为例外信息。</param>
    /// <param name="cancellationToken">放弃等待中止执行的标记。</param>
    public ValueTask AbortAsync(string reason, CancellationToken cancellationToken = default) =>
        Handle.AbortAsync(reason, cancellationToken);

    /// <inheritdoc/>
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context) =>
        throw new InvalidOperationException(
            "A durable task reference is scheduled by the durable task runtime of a grain, which subscribes to the referenced task.");
}

/// <summary>
/// 等待a <see cref="DurableTaskReference{TResult}"/>:作为长期执行的被认可儿童,或
/// 等待任务结果
/// </summary>
/// <typeparam name="TResult">任务结果类型。</typeparam>
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

    /// <summary>返回任务的成功结果,或丢弃其失败或取消。</summary>
    public TResult GetResult() => _subscribes ? _subscription.GetResult() : _wait.GetResult();
}

/// <summary>等待提及现有的持久任务。</summary>
public static class DurableTaskReferenceExtensions
{
    /// <summary>
    /// 找一个等待者,在持久处决中, 将执行任务的孩子 与任务的结果挂钩,
    /// 否则,将等待任务结果。
    /// </summary>
    /// <typeparam name="TResult">任务结果类型。</typeparam>
    /// <param name="reference">任务引用。</param>
    public static DurableTaskReferenceAwaiter<TResult> GetAwaiter<TResult>(this DurableTaskReference<TResult> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.CreateAwaiter();
    }
}

/// <summary>
/// 提及 grain的现有持久任务,例如不读取的 <see cref="DurableTaskReference{TResult}"/>
/// 或 <see cref="DurableTaskAddress"/>。
/// </summary>
/// <remarks>
/// 其其他缔约方所要完成的任务作为特点提及 <see cref="ScheduledTask"/> ,
/// <see cref="ScheduledTask.GetFeature{TFeature}"/>,其中
/// <see cref="DurableTaskAddressExtensions.TryGetAddress(ScheduledTask, out DurableTaskAddress)"/> 内容如下。
/// </remarks>
public interface IDurableTaskReference
{
    /// <summary>获取引用任务的身份识别符号。</summary>
    TaskId Id { get; }

    /// <summary>获取拥有该引用任务的 grain 。</summary>
    GrainId Target { get; }
}

/// <summary>提及一项现有任务,而持久任务将这一任务作为订阅儿童的时间安排。</summary>
internal interface IDurableTaskSubscription : IDurableTaskReference
{
}
