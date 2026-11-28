using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 在 Grain 激活实例内运行持久化任务的一次执行尝试。
/// </summary>
/// <remarks>
/// <para>
/// 当激活实例停止，或者任务被中止或随其执行树删除时，运行时通过
/// <see cref="EndAttempt"/> 取消该执行尝试。此后，执行尝试的持久化操作会抛出
/// <see cref="DurableTaskAttemptCanceledException"/>，其代码对 Grain 的日志化状态所做的变更，
/// 例如清理操作，会随 Grain 的下一次日志写入提交。运行时丢弃已取消执行尝试的响应；
/// 激活实例停止后，下一激活实例在新的上下文中重放任务。
/// </para>
/// <para>
/// 当前运行的代码无法重放某次执行尝试时，该尝试会停滞：运行时会取消尝试，
/// 原因为 <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/>（当执行实例记录了
/// 代码不支持的版本时），或者 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>（当操作
/// 与执行实例的记录发生分歧时，例如在已记录的标识符下执行另一操作，或已记录的结果
/// 无法再解码）。任务保持待处理状态，直到能够重放它的代码运行。
/// </para>
/// <para>
/// 执行尝试按 <em>代码段</em> 运行，即持久化等待之间的代码。每个代码段都是
/// Grain 激活实例上的本地请求，遵循 Grain 的可重入规则。因此，在不可重入的 Grain 上，代码段与
/// Grain 的其他请求互斥，如同 Grain 方法一样。第一个代码段启动主体。当持久化等待尚未完成时，
/// 例如等待子任务结果、信号或多个完成通知中的第一个，会挂起执行尝试：
/// 代码段的请求完成。等待完成后，新的请求恢复执行尝试，等待中的代码
/// 在该请求内继续执行。其他 await（例如日志写入或调用其他 Grain）仍在同一代码段内执行。
/// </para>
/// <para>
/// 因激活实例停止或执行尝试停滞而被丢弃的尝试永远不会恢复：下一
/// 激活实例将根据记录重放执行实例。其尚未结束的代码段（若有）会观察到取消并结束。
/// </para>
/// <para>
/// 主体在 <em>取消点</em>（其某个持久化操作的开始处）观察到任务取消；
/// 执行实例在首次观察到取消时记录该点，因此重放会在同一点观察到取消；参见
/// <see cref="DeliverTaskCancellation"/>.
/// </para>
/// <para>
/// 执行尝试的状态仅限于激活实例的调度器。从抽象层进入的入口点在读取或变更该状态之前，
/// 会切换到此调度器，因为主体可能在其他位置继续执行，例如在
/// <c>ConfigureAwait(false)</c> 之后。
/// </para>
/// </remarks>
internal sealed class GrainDurableTaskContext : DurableTaskContext
{
    private static readonly LocalRequestOptions SegmentOptions = new();

    private readonly IDurableTaskGrainRuntime _runtime;
    private readonly IGrainContext _grainContext;
    private readonly TaskScheduler _scheduler;
    private long _utcNowTicks;

    // 运行时结束执行尝试的原因（运行时在之后的工作项中取消它），或者 null。
    private DurableTaskAttemptCancellationReason? _endReason;

    // 正在运行的代码段的请求；尝试挂起或尚未开始时为 null；以及已排队的代码段请求、
    // 执行尝试中已挂起的持久化等待及其等待的任务和信号记录，
    // 以及是否已解除某个挂起等待但尚未将其恢复请求排队。
    private TaskCompletionSource? _segment;
    private int _queuedSegments;
    private int _parkedWaits;
    private HashSet<TaskId>? _awaited;
    private bool _resumePending;

    // 执行尝试的代码结束时完成；对于被丢弃的尝试，则在其不再有代码段运行时完成。
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 本次执行尝试已开始发送每种信号的次数，按标记段索引。
    private Dictionary<string, int>? _signalSends;

    // 先前的执行尝试已记录、而本次尚未到达的操作，在本次尝试到达全部操作后释放；
    // 重放期间还保留已到达的操作，并在重放结束时释放。
    // 当本次执行尝试启动了先前执行尝试都未记录的操作时，重放会在未到达集合清空之前
    // 结束。
    private HashSet<TaskId>? _unreached;
    private HashSet<TaskId>? _reached;
    private bool _replaying;

    // 本次执行尝试所决定的版本，按变更名称索引。
    private Dictionary<string, int>? _versions;

    // 本次执行尝试已到达的取消点；执行实例首次观察到其任务取消的点（由记录保存或由本次尝试选定）；
    // 以及是否已请求任务取消且仍等待到达取消点，
    // 并保存取消原因。
    private int _cancellationPoints;
    private int? _cancellationPoint;
    private bool _cancellationPending;
    private string? _pendingCancellationReason;

    // 当运行时使本次执行尝试停滞时，记录运行中的代码无法重放该执行实例的原因：代码不支持的版本，
    // 或与执行实例的记录发生分歧。
    private Exception? _stallException;

    private Guid? _incarnation;

    public GrainDurableTaskContext(
        TaskId taskId,
        IDurableTaskGrainRuntime runtime,
        IGrainContext grainContext,
        TaskScheduler scheduler,
        DateTimeOffset? utcNow = null)
        : base(taskId)
    {
        _runtime = runtime;
        _grainContext = grainContext;
        _scheduler = scheduler;
        _utcNowTicks = (utcNow ?? runtime.UtcNow).UtcTicks;
        _unreached = runtime.GetRecordedOperations(taskId);
        _replaying = _unreached is not null;
        _cancellationPoint = runtime.GetCancellationPoint(taskId);
    }

    /// <inheritdoc/>
    /// <remarks>包含此执行实例的被调用任务执行树的运行世代标识，只读取一次。</remarks>
    public override Guid Incarnation => _incarnation ??= _runtime.GetIncarnation(Id);

    /// <summary>
    /// 获取执行尝试结束的原因（由运行时结束，或由抽象层检测到分歧），或者
    /// 在其运行期间返回 <see langword="null"/>。
    /// </summary>
    internal DurableTaskAttemptCancellationReason? AttemptEndReason => AttemptCancellationReason ?? _endReason;

    /// <summary>获取执行尝试是否已结束。</summary>
    internal bool IsAttemptEnded => AttemptEndReason is not null;

    /// <summary>
    /// 获取执行尝试是否在没有记录结果的情况下结束，因而永远不会恢复：激活实例停止，或当前运行代码
    /// 无法重放该执行实例。
    /// </summary>
    internal bool IsDiscarded => AttemptEndReason
        is DurableTaskAttemptCancellationReason.ActivationStopping
        or DurableTaskAttemptCancellationReason.Diverged
        or DurableTaskAttemptCancellationReason.UnsupportedVersion;

    /// <summary>获取执行尝试是否有正在运行或已排队的代码段，从而无需消息即可继续推进。</summary>
    /// <remarks>
    /// 被运行时解除挂起的等待（通过完成其等待对象或取消其令牌）会在之后的
    /// 工作项中恢复。从解除挂起起，执行尝试即被视为可运行，因此这期间激活实例所查询的唤醒操作
    /// 仍可在崩溃后恢复它。
    /// </remarks>
    internal bool IsRunnable => _segment is not null || _queuedSegments > 0 || _resumePending;

    /// <summary>记录运行时已完成 <paramref name="awaited"/>（任务或信号记录），这可能解除一个挂起的等待。</summary>
    internal void OnAwaitedCompleted(TaskId awaited)
    {
        AssertOnScheduler();
        if (_awaited?.Contains(awaited) is true)
        {
            _resumePending = true;
        }
    }

    /// <summary>
    /// 在取消点将任务取消传达给本次执行尝试，从而解除其挂起的等待。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 执行尝试的取消点是主体各持久化操作按顺序开始的位置，包括每个由
    /// <see cref="ObserveOperation"/> 记录的操作，以及每次持久化等待（例如等待任务或信号），无论其是否
    /// 挂起。因此，找到已记录的等待事件的重放也会计入相同的取消点。确定性重放
    /// 到达与被重放运行相同的操作，也就到达相同的取消点。
    /// </para>
    /// <para>
    /// 执行实例的任务体在某个点观察到任务取消；在随后的一次日志写入中，执行实例将首次观察到的点记录在
    /// <c>{execution}/$cancellation-observed</c> 下。正在运行的
    /// 尝试在当前位置观察取消；对于已挂起的尝试，该位置是挂起等待的起点。重放
    /// 在到达记录的取消点（即相应操作的起点）时观察取消，其持久化令牌在此之前保持未取消。
    /// 若执行尝试在取消点记录提交前已结束，重放因而找不到记录的取消点，
    /// 则在重放结束的位置观察取消，即到达先前尝试记录的最后一个操作，或开始
    /// 先前尝试未记录的操作时，并记录该位置；尝试重放期间到达的取消请求亦如此。
    /// 先前尝试观察取消之后启动的操作均未提交，因此由重放确定取消点。
    /// 首次运行没有已记录的操作，因此在任务体开始前到达的取消请求会在起点（点 0）被观察到。
    /// </para>
    /// <para>
    /// 投递操作取消 <see cref="DurableTaskContext.CancellationToken"/>，其注册的回调同步运行，因此
    /// 抛出异常的注册回调会使该投递失败，或使后续取消点的操作失败。
    /// </para>
    /// </remarks>
    /// <exception cref="AggregateException">令牌上的注册回调抛出了异常。</exception>
    internal void DeliverTaskCancellation(string? reason)
    {
        AssertOnScheduler();
        if (IsTaskCancellationRequested || _cancellationPending)
        {
            return;
        }

        Debug.Assert(
            _cancellationPoint is not { } point || point >= _cancellationPoints,
            "An execution which recorded its cancellation point receives its cancellation when its attempt starts, before it reaches that point.");
        _cancellationPending = true;
        _pendingCancellationReason = reason;
        DeliverPendingCancellation();
    }

    /// <summary>记录主体到达下一个取消点，待处理的任务取消可在该点投递。</summary>
    /// <exception cref="AggregateException">令牌上的注册回调抛出了异常。</exception>
    private void ReachCancellationPoint()
    {
        _cancellationPoints++;
        DeliverPendingCancellation();
    }

    /// <summary>
    /// 当执行尝试处于主体观察任务取消的点时，投递待处理的任务取消；若执行实例尚无该点的记录，
    /// 则记录此点。
    /// </summary>
    private void DeliverPendingCancellation()
    {
        if (!_cancellationPending || (_cancellationPoint is { } point ? point != _cancellationPoints : _replaying))
        {
            return;
        }

        _cancellationPending = false;
        if (_cancellationPoint is null && !IsAttemptEnded)
        {
            _cancellationPoint = _cancellationPoints;
            _runtime.RecordCancellationPoint(Id, _cancellationPoints);
        }

        _resumePending |= _parkedWaits > 0;
        RequestTaskCancellation(_pendingCancellationReason);
    }

    /// <summary>
    /// 如果主体在到达先前执行尝试观察到任务取消的取消点之前就结束，则因重放分歧使尝试停滞，
    /// 因为当前运行代码未能重放该执行实例。
    /// </summary>
    internal void VerifyCancellationPointReached()
    {
        AssertOnScheduler();
        if (_cancellationPending && _cancellationPoint is { } point && !IsAttemptEnded)
        {
            _ = Diverge(new DurableTaskDivergenceException(
                $"Durable task '{Id}' ended at cancellation point {_cancellationPoints}, before point {point}, where an earlier run observed its task's cancellation. A replay reaches the same durable operations as the run which it replays."));
        }
    }

    /// <summary>
    /// 获取一个在执行尝试代码结束时完成的任务；对于被丢弃的尝试，则在其不再有代码段
    /// 运行时完成，此后执行尝试不再在激活实例上运行任何代码。
    /// </summary>
    internal Task Settled => _settled.Task;

    /// <summary>将运行执行尝试主体的代码段排队，由 <paramref name="start"/> 解除其等待。</summary>
    internal void Start(TaskCompletionSource start) => QueueSegment(start);

    /// <summary>将代码段请求排队，在激活实例准入该请求时解除 <paramref name="gate"/> 的等待。</summary>
    private void QueueSegment(TaskCompletionSource gate)
    {
        AssertOnScheduler();
        _queuedSegments++;
        _resumePending = false;
        _grainContext.ReceiveLocalRequest(new DurableTaskSegment(this, gate), SegmentOptions);
    }

    /// <summary>
    /// 运行激活实例已准入的代码段：解除 <paramref name="gate"/> 的等待，其等待中的代码在
    /// 请求内继续执行，并在执行尝试挂起或结束时完成。
    /// </summary>
    /// <remarks>
    /// 被丢弃的执行尝试不会恢复。若激活实例在该尝试的另一个代码段运行时准入新的代码段
    /// （可重入 Grain 可以如此），则在正在运行的代码段中继续执行。
    /// </remarks>
    internal ValueTask<Response> RunSegmentAsync(TaskCompletionSource gate)
    {
        AssertOnScheduler();
        _queuedSegments--;
        if (IsDiscarded)
        {
            return new(Response.Completed);
        }

        if (_segment is not null)
        {
            gate.SetResult();
            return new(Response.Completed);
        }

        var segment = _segment = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return AwaitSegmentAsync(segment.Task);

        static async ValueTask<Response> AwaitSegmentAsync(Task segment)
        {
            await segment;
            return Response.Completed;
        }
    }

    /// <summary>结束正在运行的代码段，使激活实例能够准入其他请求。</summary>
    private void EndSegment()
    {
        if (_segment is { } segment)
        {
            _segment = null;
            segment.SetResult();
        }

        if (IsDiscarded)
        {
            _settled.TrySetResult();
        }
    }

    /// <summary>
    /// 等待 <paramref name="wait"/>，即等待由消息投递的事件的持久化等待，并在该等待尚未完成时
    /// 挂起执行尝试。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 未完成的等待使执行尝试的代码段结束。等待完成后，新的代码段请求恢复等待中的代码，
    /// 使其在该请求中继续执行，除非此期间执行尝试已被丢弃。
    /// </para>
    /// <para>
    /// 库中的组合器可以同时等待一次执行尝试的多个事件。当一个已恢复的等待返回而
    /// 另一个等待仍然挂起时，该代码段结束，使另一个等待的恢复得以运行；两者之间继续执行的代码
    /// 在代码段之外运行。重放模型要求单一逻辑流程，而在同一逻辑流程中，同一时刻至多有一次等待
    /// 处于挂起状态。
    /// </para>
    /// </remarks>
    internal async ValueTask<T> ParkAsync<T>(Task<T> wait, IReadOnlyCollection<TaskId> awaited)
    {
        await ParkAsync((Task)wait, awaited);
        return await wait;
    }

    /// <inheritdoc cref="ParkAsync{T}(Task{T}, IReadOnlyCollection{TaskId})"/>
    internal async ValueTask ParkAsync(Task wait, IReadOnlyCollection<TaskId> awaited)
    {
        AssertOnScheduler();
        if (!wait.IsCompleted)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _parkedWaits++;
            (_awaited ??= []).UnionWith(awaited);
            _ = wait.ContinueWith(
                static (_, state) =>
                {
                    var (attempt, gate) = ((GrainDurableTaskContext, TaskCompletionSource))state!;
                    attempt.QueueSegment(gate);
                },
                (this, gate),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                _scheduler);
            EndSegment();
            await gate.Task;
            if (--_parkedWaits > 0)
            {
                EndSegment();
            }
            else
            {
                _awaited = null;
            }
        }

        await wait;
    }

    [Conditional("DEBUG")]
    private void AssertOnScheduler() =>
        Debug.Assert(TaskScheduler.Current == _scheduler, "The attempt's state is confined to the activation's scheduler.");

    /// <remarks>
    /// 执行尝试开始时，运行时提供先前尝试已记录在此执行实例的执行树中的操作：
    /// 其子任务，包括其他任务的订阅和分离任务的记录、信号发送标记，
    /// 以及效果记录，但不包含由发送方记录的信号值、被调用的后代任务，或先于其请求
    /// 到达的取消。执行尝试在启动或投递操作、查找其句柄、
    /// 在其标记下发送信号、在其决策下选择完成结果，或者按其名称运行效果时，即到达该操作。启动
    /// 先前执行尝试均未记录的操作会结束重放；再次到达某项操作则不会。
    /// </remarks>
    public override bool IsReplaying => _replaying;

    /// <summary>
    /// 获取先前执行尝试是否记录了本次尚未到达的操作，从而判断先前运行是否已经
    /// 越过本次执行尝试的当前位置。
    /// </summary>
    /// <remarks>
    /// 执行实例的任务体在一个逻辑流中运行持久化操作，并发工作则在子执行实例中运行，
    /// 因此确定性重放会按照先前运行创建这些操作的顺序到达已记录的操作。
    /// </remarks>
    private bool HasUnreachedOperations => _unreached is not null;

    /// <summary>
    /// 获取运行此执行实例的 Grain，它限定分离任务的根标识符，分离任务由
    /// 执行实例未指定名称而启动，因此不同 Grain 上具有相同标识符的执行实例会派生不同的根标识符。
    /// </summary>
    protected internal override string HostId => _runtime.GrainId.ToString();

    /// <summary>记录本次执行尝试到达 <paramref name="operationId"/>，这可能结束其重放。</summary>
    /// <param name="operationId">操作标识符。</param>
    /// <param name="recordsOperation">
    /// 操作是否像调度那样在新标识符下记录自身，而非仅查找已有
    /// 标识符（如句柄查找，或宿主依据持久化顺序派生的完成结果选择）。
    /// </param>
    private void ObserveOperation(TaskId operationId, bool recordsOperation)
    {
        AssertOnScheduler();
        ReachCancellationPoint();
        if (_unreached is not { } unreached)
        {
            return;
        }

        if (unreached.Remove(operationId))
        {
            if (unreached.Count > 0)
            {
                if (_replaying)
                {
                    (_reached ??= []).Add(operationId);
                }

                return;
            }

            _unreached = null;
        }
        else if (!_replaying || !recordsOperation || _reached?.Contains(operationId) is true)
        {
            return;
        }

        _reached = null;
        if (!_replaying)
        {
            return;
        }

        _replaying = false;
        _runtime.OnReplayEnded(Id);
        DeliverPendingCancellation();
    }

    /// <summary>
    /// 获取本执行实例的逻辑时间：从任务首次运行的时间开始，推进到每个
    /// 执行实例所观察终态响应的子任务的已记录完成时间。
    /// </summary>
    /// <remarks>
    /// 子任务结果保留各自的完成时间，因此按相同顺序观察相同子任务的重放会读取
    /// 相同的时间。因此，延迟从其前面的工作完成时开始。
    /// </remarks>
    public override DateTimeOffset UtcNow => new(_utcNowTicks, TimeSpan.Zero);

    private void ObserveCompletion(TaskId childId)
    {
        if (_runtime.GetCompletedAt(childId) is { } completedAt)
        {
            ObserveTime(completedAt);
        }
    }

    private void ObserveTime(DateTimeOffset time)
    {
        AssertOnScheduler();
        if (time.UtcTicks > _utcNowTicks)
        {
            _utcNowTicks = time.UtcTicks;
        }
    }

    /// <summary>
    /// 立即以 <paramref name="reason"/> 为由结束本次执行尝试：此后其持久化操作会抛出
    /// <see cref="DurableTaskAttemptCanceledException"/>，且被丢弃的执行尝试不会恢复。
    /// 随后由 <see cref="ApplyCancellation"/> 取消执行尝试的令牌。
    /// </summary>
    /// <returns><see langword="true"/> 表示本次调用结束了执行尝试。</returns>
    /// <remarks>
    /// 运行时在执行尝试代码运行的激活实例调度器上结束尝试，所以该代码的同步执行
    /// 只会观察到执行尝试的一种状态。取消执行尝试的令牌会运行注册回调，并且可能使
    /// 尝试的代码同步继续执行，因此运行时在暂存操作之外进行取消。
    /// </remarks>
    internal bool EndAttempt(DurableTaskAttemptCancellationReason reason)
    {
        AssertOnScheduler();
        if (AttemptEndReason is not null)
        {
            return false;
        }

        _endReason = reason;
        if (IsDiscarded && _segment is null)
        {
            _settled.TrySetResult();
        }

        return true;
    }

    /// <summary>取消由 <see cref="EndAttempt"/> 结束的执行尝试的令牌。</summary>
    /// <exception cref="AggregateException">令牌上的注册回调抛出了异常。</exception>
    internal void ApplyCancellation()
    {
        AssertOnScheduler();
        Debug.Assert(_endReason is not null, "Only an ended attempt is canceled.");
        _resumePending |= _parkedWaits > 0 && !IsDiscarded;
        CancelAttempt(_endReason.GetValueOrDefault());
    }

    /// <summary>结束执行尝试的代码段，并在其代码结束后使该执行尝试进入最终状态。</summary>
    internal void OnAttemptCompleted()
    {
        EndSegment();
        _settled.TrySetResult();
    }

    /// <summary>
    /// 以 <paramref name="reason"/> 为由使本次执行尝试停滞：运行时取消该尝试并使其任务保持待处理状态，以便能够
    /// 重放执行实例的代码稍后运行它。
    /// </summary>
    /// <returns>停滞操作抛出的异常。</returns>
    private DurableTaskAttemptCanceledException Stall(DurableTaskAttemptCancellationReason reason, Exception cause)
    {
        _stallException ??= cause;
        _runtime.StallAttempt(this, reason, cause);
        return CreateAttemptEndedException();
    }

    /// <summary>因操作与执行实例的记录发生分歧而使本次执行尝试停滞。</summary>
    private DurableTaskAttemptCanceledException Diverge(DurableTaskDivergenceException divergence) =>
        Stall(DurableTaskAttemptCancellationReason.Diverged, divergence);

    /// <summary>运行时已取消本次执行尝试时抛出 <see cref="DurableTaskAttemptCanceledException"/>。</summary>
    private void ThrowIfAttemptCanceled()
    {
        if (IsAttemptEnded)
        {
            throw CreateAttemptEndedException();
        }
    }

    private DurableTaskAttemptCanceledException CreateAttemptEndedException(Exception? innerException = null)
    {
        Debug.Assert(AttemptEndReason is not null, "Only an ended attempt throws its cancellation.");
        return new(AttemptEndReason.GetValueOrDefault(), AttemptCancellationToken, innerException ?? _stallException);
    }

    /// <summary>
    /// 以本次执行尝试中操作的结果完成该操作；如果运行时已取消执行尝试，
    /// 则以执行尝试的取消结果完成操作。与执行实例记录发生分歧的操作会使尝试停滞。
    /// </summary>
    /// <remarks>
    /// 运行时在发出执行尝试令牌的取消信号之前取消尝试，因此在此间完成的操作，
    /// 例如等待已经提交中止的子任务时，会以执行尝试取消结束，而不是返回子任务结果。
    /// </remarks>
    private async ValueTask<T> CompleteOperationAsync<T>(ValueTask<T> operation)
    {
        T result;
        try
        {
            result = await operation;
        }
        catch (Exception exception) when (exception is not DurableTaskAttemptCanceledException && IsAttemptEnded)
        {
            throw CreateAttemptEndedException(exception);
        }
        catch (DurableTaskDivergenceException divergence)
        {
            throw Diverge(divergence);
        }

        ThrowIfAttemptCanceled();
        return result;
    }

    /// <inheritdoc cref="CompleteOperationAsync{T}(ValueTask{T})"/>
    private async ValueTask CompleteOperationAsync(ValueTask operation)
    {
        try
        {
            await operation;
        }
        catch (Exception exception) when (exception is not DurableTaskAttemptCanceledException && IsAttemptEnded)
        {
            throw CreateAttemptEndedException(exception);
        }
        catch (DurableTaskDivergenceException divergence)
        {
            throw Diverge(divergence);
        }

        ThrowIfAttemptCanceled();
    }

    /// <remarks>
    /// 此宿主分离启动持久化 Grain 调用：目标从调用的序列化请求恢复根任务。
    /// 抽象层可以分离任意 <see cref="ISchedulableTask"/>，因此此宿主以
    /// <see cref="NotSupportedException"/> 拒绝其他类型的任务，且不会记录或发送任何内容。分离
    /// 启动操作在 <c>$detach:{target}:{rootId}</c> 下记录，因此本执行实例启动的每个分离任务
    /// 都有自己的记录，而不受部署增加或删除其他操作的影响。
    /// </remarks>
    protected internal override ValueTask<IScheduledTaskHandle> StartTaskAsync(
        TaskId taskId,
        DurableTask task,
        DurableTaskStartOptions options,
        CancellationToken cancellationToken)
    {
        // 屏蔽保护状态随启动代码流动，因此在切换调度器之前读取它。
        var shielded = SchedulesShieldedChildren;
        var waitForAcceptance = options.HasFlag(DurableTaskStartOptions.WaitForAcceptance);
        if (options.HasFlag(DurableTaskStartOptions.Detached))
        {
            return RunOnScheduler(
                _scheduler,
                () => StartDetachedCoreAsync(taskId, task, shielded, waitForAcceptance, cancellationToken));
        }

        return RunOnScheduler(
            _scheduler,
            () => ScheduleChildTaskCoreAsync(taskId, task, shielded, waitForAcceptance, cancellationToken));
    }

    private async ValueTask<IScheduledTaskHandle> ScheduleChildTaskCoreAsync(
        TaskId taskId,
        DurableTask task,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken)
    {
        ObserveOperation(taskId, recordsOperation: true);
        try
        {
            ValidateChild(taskId, task);
        }
        catch (DurableTaskDivergenceException divergence)
        {
            throw Diverge(divergence);
        }

        using var operationCts = CreateOperationCancellationSource(cancellationToken);
        var handle = await CompleteOperationAsync(_runtime.ScheduleChildAsync(taskId, task, shielded, waitForAcceptance, operationCts.Token));
        return CreateExecutionHandle(handle);
    }

    private async ValueTask<IScheduledTaskHandle> StartDetachedCoreAsync(
        TaskId rootId,
        DurableTask task,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken)
    {
        var (recordId, request) = GetDetachedRecord(rootId, task);
        ThrowIfAttemptCanceled();
        using var operationCts = CreateOperationCancellationSource(cancellationToken);
        var handle = await CompleteOperationAsync(_runtime.StartDetachedAsync(recordId, rootId, request, shielded, waitForAcceptance, operationCts.Token));
        return CreateExecutionHandle(handle);
    }

    /// <remarks>
    /// 启动操作及其记录在激活实例上调用此方法的工作项中暂存，因此它们与该工作项
    /// 修改的日志化状态在同一次日志写入中提交。到达记录的重放返回
    /// 同一句柄，而不会再次启动任务。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用方未在激活实例的调度器上运行。</exception>
    protected internal override IScheduledTaskHandle PostTask(TaskId taskId, DurableTask task, DurableTaskStartOptions options)
    {
        if (TaskScheduler.Current != _scheduler)
        {
            throw new InvalidOperationException(
                $"Post runs on the scheduler of the grain activation which runs durable task '{Id}', so its start commits with the state which the calling code changes. Code which continues elsewhere, for example after ConfigureAwait(false), can't post.");
        }

        ThrowIfAttemptCanceled();
        var shielded = SchedulesShieldedChildren;
        try
        {
            if (options.HasFlag(DurableTaskStartOptions.Detached))
            {
                var (recordId, request) = GetDetachedRecord(taskId, task);
                return CreateExecutionHandle(_runtime.PostDetached(recordId, taskId, request, shielded));
            }

            ObserveOperation(taskId, recordsOperation: true);
            ValidateChild(taskId, task);
            return CreateExecutionHandle(_runtime.PostChild(taskId, task, shielded));
        }
        catch (DurableTaskDivergenceException divergence)
        {
            throw Diverge(divergence);
        }
    }

    /// <summary>返回在本执行实例中记录分离启动操作的标识符，以及已启动的请求。</summary>
    /// <exception cref="NotSupportedException">该定义不是持久化 Grain 调用。</exception>
    private (TaskId RecordId, IDurableTaskRequest Request) GetDetachedRecord(TaskId rootId, DurableTask task)
    {
        if (task is not IDurableTaskRequest { Context: { } requestContext } request)
        {
            throw new NotSupportedException("Only durable grain calls can be detached.");
        }

        var recordId = Id.Child(CreateMarkerSegment("detach", [requestContext.TargetId.ToString(), rootId.ToString()]));
        ObserveOperation(recordId, recordsOperation: true);
        return (recordId, request);
    }

    /// <summary>
    /// 除非 <paramref name="taskId"/> 可以标识本执行实例中运行 <paramref name="task"/> 的子任务，否则抛出异常：
    /// 执行实例未被删除，并且先前运行记录的任何效果都未占用该子任务所运行的
    /// 上层操作，例如以该子任务为输入的命名组合器。
    /// </summary>
    /// <remarks>运行时会在直接子任务启动时检查其自身标识符下找到的记录。</remarks>
    private void ValidateChild(TaskId taskId, DurableTask task)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        ThrowIfNotChildTaskId(taskId);
        ThrowIfAttemptCanceled();
        var operationId = taskId;
        for (var parent = operationId.Parent(); parent != Id; parent = parent.Parent())
        {
            operationId = parent;
        }

        if (operationId != taskId)
        {
            _runtime.ThrowIfRecordedAsEffect(
                taskId,
                operationId,
                task switch
                {
                    IDurableTaskRequest { Context.TargetId: var target } => $"a call to grain '{target}'",
                    IDurableTaskSubscription subscription => $"a subscription to task '{subscription.Id}' on grain '{subscription.Target}'",
                    _ => "a local child task",
                });
        }
    }

    protected internal override ValueTask<DurableTaskResponse> ScheduleDelayAsync(
        TaskId taskId,
        DateTimeOffset dueTime,
        CancellationToken cancellationToken) =>
        RunOnScheduler(
            _scheduler,
            () => ScheduleDelayCoreAsync(taskId, dueTime, cancellationToken));

    private async ValueTask<DurableTaskResponse> ScheduleDelayCoreAsync(
        TaskId taskId,
        DateTimeOffset dueTime,
        CancellationToken cancellationToken)
    {
        if (taskId != Id)
        {
            ObserveOperation(taskId, recordsOperation: true);
            ThrowIfNotChildTaskId(taskId);
        }

        ThrowIfAttemptCanceled();

        using var operationCts = CreateOperationCancellationSource(cancellationToken);
        return await CompleteOperationAsync(_runtime.ScheduleDelayAsync(taskId, dueTime, operationCts.Token));
    }

    /// <remarks>
    /// 主体的最后一段代码在调用此方法的工作项中运行，因此在此处暂存响应，可使其与该代码修改的
    /// 日志化状态在同一次日志写入中提交，无论哪个操作请求这次写入。在另一
    /// 调度器上完成的主体的响应由执行尝试处理；已取消执行尝试的响应会被
    /// 丢弃。若主体在到达先前执行尝试观察到任务取消的取消点之前已结束，
    /// 则发生重放分歧，因此其执行尝试停滞，不暂存任何内容。
    /// </remarks>
    protected internal override void OnBodyCompleted(DurableTaskResponse response)
    {
        if (TaskScheduler.Current != _scheduler)
        {
            return;
        }

        VerifyCancellationPointReached();
        if (IsAttemptEnded)
        {
            return;
        }

        _runtime.StageBodyResponse(this, response);
    }

    /// <remarks>
    /// 效果在激活实例上调用此方法的工作项中运行，其记录也在该工作项中暂存，因此该记录
    /// 与效果所修改的日志化状态在同一次日志写入中提交。效果与子任务共用
    /// 标识符，因此在先前运行记录了子任务的位置运行效果，或在先前运行记录了
    /// 效果的位置运行子任务，都会发生分歧：执行尝试以 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 停滞，其
    /// 分歧信息指出两种操作类型，而不是再次运行效果。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用方未在激活实例的调度器上运行。</exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 执行尝试已取消，或者因先前运行在效果的标识符下记录了另一类型的操作而
    /// 停滞。
    /// </exception>
    protected internal override DurableTaskResponse RunEffect<TState>(
        TaskId effectId,
        TState state,
        Func<TState, DurableTaskResponse> effect)
    {
        if (TaskScheduler.Current != _scheduler)
        {
            throw new InvalidOperationException(
                $"DurableTask.RunInline runs on the scheduler of the grain activation which runs durable task '{Id}', so its record commits with the state which it changes. Code which continues elsewhere, for example after ConfigureAwait(false), can't record effects.");
        }

        ThrowIfNotChildTaskId(effectId);
        ThrowIfAttemptCanceled();
        ObserveOperation(effectId, recordsOperation: true);
        try
        {
            return _runtime.RunEffect(effectId, state, effect);
        }
        catch (DurableTaskDivergenceException divergence)
        {
            throw Diverge(divergence);
        }
    }

    /// <remarks>
    /// 执行尝试中针对某个名称的第一次调用决定其版本，并缓存该决策，因此检查同一变更的循环及辅助方法
    /// 读取相同的版本。如果尚无记录保存该决策，而先前执行尝试仍有未到达的操作，则版本为 <c>0</c>，
    /// 而不是依据 <see cref="IsReplaying"/> 是否报告重放：部署在主体较早位置
    /// 插入的新操作会结束重放，却不改变先前运行已经推进的范围。该记录
    /// 在激活实例上调用此方法的工作项中暂存，并且不会结束重放。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用方未在激活实例的调度器上运行。</exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 运行时取消了执行尝试，例如执行实例的版本超出支持范围；此时其
    /// <see cref="DurableTaskVersionException"/> 作为内部异常。
    /// </exception>
    protected internal override int GetVersion(string name, int minSupported, int maxSupported)
    {
        if (TaskScheduler.Current != _scheduler)
        {
            throw new InvalidOperationException(
                $"DurableTask.GetVersion runs on the scheduler of the grain activation which runs durable task '{Id}', so its record commits with the execution's other records. Code which continues elsewhere, for example after ConfigureAwait(false), can't read versions.");
        }

        ThrowIfAttemptCanceled();
        if (_versions is null || !_versions.TryGetValue(name, out var version))
        {
            version = -1;
        }

        if (version < 0)
        {
            try
            {
                version = _runtime.GetOrRecordVersion(Id, name, HasUnreachedOperations ? 0 : maxSupported);
            }
            catch (DurableTaskDivergenceException divergence)
            {
                throw Diverge(divergence);
            }

            (_versions ??= [])[name] = version;
        }

        if (version < minSupported || version > maxSupported)
        {
            // 运行时取消执行尝试并使其任务保持待处理状态，以便支持该版本的代码重放任务。
            throw Stall(
                DurableTaskAttemptCancellationReason.UnsupportedVersion,
                new DurableTaskVersionException(Id, name, version, minSupported, maxSupported));
        }

        return version;
    }

    /// <summary>
    /// 根据操作的标识创建记录本执行实例中某个操作的标记段：
    /// <c>${kind}:{part}:{part}...</c>，每个部分中的 <c>\</c>、<c>:</c> 和 <c>$</c> 均进行转义，因此不同的
    /// 标识会创建不同的段，且 <c>$</c> 后缀可以为重复出现的操作编号。
    /// </summary>
    private static string CreateMarkerSegment(string kind, ReadOnlySpan<string> parts)
    {
        var segment = new StringBuilder("$").Append(kind);
        foreach (var part in parts)
        {
            segment.Append(':');
            foreach (var character in part)
            {
                if (character is ':' or '\\' or '$')
                {
                    segment.Append('\\');
                }

                segment.Append(character);
            }
        }

        return segment.ToString();
    }

    /// <remarks>
    /// 等待操作观察被调用任务的信号（该任务的执行树包含本执行实例），并推进此
    /// 执行实例的逻辑时间到信号的记录时间。
    /// </remarks>
    protected internal override ValueTask<DurableTaskResponse> WaitForSignalAsync(string name, CancellationToken cancellationToken) =>
        RunOnScheduler(
            _scheduler,
            () => WaitForSignalCoreAsync(name, cancellationToken));

    private async ValueTask<DurableTaskResponse> WaitForSignalCoreAsync(string name, CancellationToken cancellationToken)
    {
        ReachCancellationPoint();
        ThrowIfAttemptCanceled();
        using var operationCts = CreateOperationCancellationSource(cancellationToken);
        var (value, recordedAt) = await CompleteOperationAsync(_runtime.WaitForSignalAsync(this, Id, name, operationCts.Token));
        ObserveTime(recordedAt);
        return value;
    }

    /// <summary>
    /// 向 <paramref name="taskId"/> 所标识、位于 <paramref name="target"/> 上的被调用任务发送信号，并将发送操作记录在
    /// 本执行实例的 <c>$send:{target}:{taskId}:{name}</c> 下，因此重放不会再次发送信号。
    /// </summary>
    /// <remarks>
    /// 信号的标识决定记录的名称，因此增加或删除其他操作不会改变发送操作
    /// 到达的记录。同一执行实例中对同一信号的重复发送（例如
    /// <see cref="DurableSignalResult.TaskNotFound"/> 后的重试）按出现次序编号，<c>$send:{target}:{taskId}:{name}$2</c>
    /// 等，以便每次发送都到达自己的记录；目标将相等的值报告为
    /// <see cref="DurableSignalResult.Duplicate"/>，将不同的值报告为 <see cref="DurableSignalResult.Conflict"/>。
    /// 本执行实例的任务取消不会放弃发送操作；执行尝试的取消则会。
    /// </remarks>
    /// <param name="throwOnRejection">此 Grain 上的任务拒绝信号时，是否抛出异常，而非记录并返回拒绝结果。</param>
    internal ValueTask<DurableSignalResult> SendSignalAsync(
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        bool throwOnRejection,
        CancellationToken cancellationToken) =>
        RunOnScheduler(
            _scheduler,
            () => SendSignalCoreAsync(target, taskId, name, value, throwOnRejection, cancellationToken));

    private async ValueTask<DurableSignalResult> SendSignalCoreAsync(
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        bool throwOnRejection,
        CancellationToken cancellationToken)
    {
        var signal = CreateMarkerSegment("send", [target.ToString(), taskId.ToString(), name]);
        _signalSends ??= [];
        _signalSends.TryGetValue(signal, out var occurrence);
        _signalSends[signal] = ++occurrence;
        var markerId = Id.Child(occurrence == 1 ? signal : $"{signal}${occurrence.ToString(CultureInfo.InvariantCulture)}");
        ObserveOperation(markerId, recordsOperation: true);
        ThrowIfAttemptCanceled();
        using var operationCts = CreateOperationCancellationSource(cancellationToken);
        return await CompleteOperationAsync(_runtime.SendSignalAsync(markerId, target, taskId, name, value, throwOnRejection, operationCts.Token));
    }

    protected internal override ValueTask<TaskId> SelectCompletionAsync(
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken) =>
        RunOnScheduler(
            _scheduler,
            () => SelectCompletionCoreAsync(decisionId, candidates, cancellationToken));

    private async ValueTask<TaskId> SelectCompletionCoreAsync(
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken)
    {
        ObserveOperation(decisionId, recordsOperation: false);
        ThrowIfNotChildTaskId(decisionId);
        foreach (var candidate in candidates)
        {
            ThrowIfNotChildTaskId(candidate);
        }

        ThrowIfAttemptCanceled();

        using var operationCts = CreateOperationCancellationSource(cancellationToken);
        return await CompleteOperationAsync(_runtime.SelectCompletionAsync(this, decisionId, candidates, operationCts.Token));
    }

    private void ThrowIfNotChildTaskId(TaskId taskId)
    {
        if (taskId == Id || !Id.IsAncestorOf(taskId))
        {
            throw new InvalidOperationException($"The provided task ID '{taskId}' is not a descendant of this task '{Id}'.");
        }
    }

    /// <summary>将操作的令牌与执行尝试的取消关联；后者结束该尝试的每项操作。</summary>
    private CancellationTokenSource CreateOperationCancellationSource(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            AttemptCancellationToken);

    /// <summary>运行入口点的 <paramref name="callback"/>，由激活实例的调度器执行。</summary>
    /// <exception cref="InvalidOperationException">调用方未在激活实例的调度器上运行。</exception>
    private static ValueTask RunOnScheduler(TaskScheduler scheduler, Func<ValueTask> callback)
    {
        ThrowIfOffScheduler(scheduler);
        return callback();
    }

    /// <inheritdoc cref="RunOnScheduler(TaskScheduler, Func{ValueTask})"/>
    private static ValueTask<TResult> RunOnScheduler<TResult>(TaskScheduler scheduler, Func<ValueTask<TResult>> callback)
    {
        ThrowIfOffScheduler(scheduler);
        return callback();
    }

    /// <summary>
    /// 除非调用方在 <paramref name="scheduler"/>（激活实例的调度器）上运行，否则抛出异常；该调度器拥有执行尝试的状态。
    /// </summary>
    /// <remarks>
    /// 主体在每次 await 后都在激活实例的调度器上继续执行，除非使用 <c>ConfigureAwait(false)</c>，
    /// ORLEANS0226 会报告此用法。
    /// </remarks>
    private static void ThrowIfOffScheduler(TaskScheduler scheduler)
    {
        if (TaskScheduler.Current != scheduler)
        {
            throw new InvalidOperationException(
                "A durable task's operations run on the scheduler of the grain activation which runs it. Code which continues elsewhere, for example after ConfigureAwait(false), can't run them.");
        }
    }

    /// <summary>包装本执行实例的操作句柄，由激活实例的调度器运行该操作。</summary>
    private ExecutionTaskHandle CreateExecutionHandle(IScheduledTaskHandle handle)
    {
        var targetTaskId = _runtime.GetTargetTaskId(handle.TaskId, out var addressedHost);
        return new(handle, this, targetTaskId, addressedHost);
    }

    private sealed class ExecutionTaskHandle(
        IScheduledTaskHandle inner,
        GrainDurableTaskContext owner,
        TaskId targetTaskId,
        GrainId addressedHost) : IScheduledTaskHandle
    {
        public TaskId TaskId => inner.TaskId;

        public TaskId TargetTaskId { get; } = targetTaskId;

        /// <summary>
        /// 返回分离任务或被订阅任务的 <see cref="DurableTaskAddress"/>，形式为
        /// <see cref="IDurableTaskReference"/>.
        /// </summary>
        public TFeature? GetFeature<TFeature>()
            where TFeature : class =>
            addressedHost.IsDefault ? null : new DurableTaskAddress(addressedHost, TargetTaskId) as TFeature;

        public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) =>
            RunOnScheduler(
                owner._scheduler,
                () => CancelCoreAsync(reason, cancellationToken));

        private async ValueTask CancelCoreAsync(string? reason, CancellationToken cancellationToken)
        {
            owner.ThrowIfAttemptCanceled();
            using var operationCts = owner.CreateOperationCancellationSource(cancellationToken);
            await owner.CompleteOperationAsync(inner.CancelAsync(reason, operationCts.Token));
        }

        public ValueTask AbortAsync(string reason, CancellationToken cancellationToken) =>
            RunOnScheduler(
                owner._scheduler,
                () => AbortCoreAsync(reason, cancellationToken));

        private async ValueTask AbortCoreAsync(string reason, CancellationToken cancellationToken)
        {
            owner.ThrowIfAttemptCanceled();
            using var operationCts = owner.CreateOperationCancellationSource(cancellationToken);
            await owner.CompleteOperationAsync(inner.AbortAsync(reason, operationCts.Token));
        }

        public ValueTask<DurableTaskResponse> PollAsync(
            PollingOptions options,
            CancellationToken cancellationToken) =>
            RunOnScheduler(
                owner._scheduler,
                () => PollCoreAsync(options, cancellationToken));

        private async ValueTask<DurableTaskResponse> PollCoreAsync(
            PollingOptions options,
            CancellationToken cancellationToken)
        {
            owner.ThrowIfAttemptCanceled();
            using var operationCts = owner.CreateOperationCancellationSource(cancellationToken);
            return Observe(await owner.CompleteOperationAsync(inner.PollAsync(options, operationCts.Token)));
        }

        public ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken) =>
            RunOnScheduler(
                owner._scheduler,
                () => WaitCoreAsync(cancellationToken));

        private async ValueTask<DurableTaskResponse> WaitCoreAsync(CancellationToken cancellationToken)
        {
            owner.ReachCancellationPoint();
            owner.ThrowIfAttemptCanceled();
            using var operationCts = owner.CreateOperationCancellationSource(cancellationToken);
            return Observe(await owner.CompleteOperationAsync(owner.ParkAsync(inner.WaitAsync(operationCts.Token).AsTask(), [inner.TaskId])));
        }

        private DurableTaskResponse Observe(DurableTaskResponse response)
        {
            if (response.IsCompleted)
            {
                owner.ObserveCompletion(inner.TaskId);
            }

            return response;
        }
    }
}
