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
/// 在启动 grain 内执行一项耐久任务尝试 。
/// </summary>
/// <remarks>
/// <para>
/// 当激活停止时, 或任务执行中止或删除时, 运行时间取消尝试
/// 穿过树,穿过 <see cref="EndAttempt"/>。从那时起,尝试的持久操作就投下
/// <see cref="DurableTaskAttemptCanceledException"/>,以及其代码对 grain日记状态所作的修改,
/// 清理等, 请在 grain下一份日记中承诺。 运行时将放弃取消的尝试回复;
/// 激活停止后, 下一次激活会在新的背景下重现任务 。
/// </para>
/// <para>
/// 运行中的代码无法重放摊位的尝试:运行时间取消
/// <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 当执行时记录了
/// 代码不支持, 当操作时 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>
/// 与执行记录的差异,例如根据记录的身份识别特征进行的另一项行动或记录的结果
/// 不再解码。任务将一直等待到代码重放为止。
/// </para>
/// <para>
/// 尝试在 <em>段</em>中运行: 其耐久等待之间的代码。 每个区段是本地对
/// grain 激活,该启动继 grain重新恢复规则,所以对于一个没有被重新恢复的人 grain 一个段不包括
/// grain的其他请求, 作为一种 grain 方法。 第一部分开始身体。 等待的持久等待, 尚未结束 ,
/// 例如等待儿童的结果、信号或几个完成的第一项,使尝试成为空洞:
/// 请求。一旦等待完成,新的请求将恢复尝试,等待代码将
/// 其他等待,例如日记写作或致其他 grains的电话等,则留在该部分内。
/// </para>
/// <para>
/// 一次因启动停止或试图拖延而放弃的尝试,从未恢复:下一次
/// 激活从记录中重放执行的功能。 如果有的话,它打开部分会观察取消和结束 。
/// </para>
/// <para>
/// 该机构在 <em>取消点</em>观察其任务的取消,这是其一个持久业务的开始,
/// 执行记录在它第一次观察时,重放时在同一点上;见
/// <see cref="DeliverTaskCancellation"/>.
/// </para>
/// <para>
/// 尝试状态仅限于启动的调度器
/// 之前的调度器在读取或更改该状态前先读取或修改此状态,因为一个机构可以在其他地方继续,例如
/// <c>ConfigureAwait(false)</c>.
/// </para>
/// </remarks>
internal sealed class GrainDurableTaskContext : DurableTaskContext
{
    private static readonly LocalRequestOptions SegmentOptions = new();

    private readonly IDurableTaskGrainRuntime _runtime;
    private readonly IGrainContext _grainContext;
    private readonly TaskScheduler _scheduler;
    private long _utcNowTicks;

    // 为何运行时间结束了尝试,在以后的工作项目中取消这一尝试,或无效。
    private DurableTaskAttemptCancellationReason? _endReason;

    // 运行的段段的请求,或尝试停放时无效或尚未启动的部分请求;部分请求
    // 排队; 试图等待的耐久等待停放,以及等待的任务和信号记录;
    // 以及停在车边的等待是否已经释放 其简历是否还没有排队
    private TaskCompletionSource? _segment;
    private int _queuedSegments;
    private int _parkedWaits;
    private HashSet<TaskId>? _awaited;
    private bool _resumePending;

    // 一旦尝试代码结束,或者在被废弃的尝试中,一旦它没有运行部分时,它就会完成。
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 尝试开始的每个信号发送次数,按标记段分列。
    private Dictionary<string, int>? _signalSends;

    // 先前的尝试记录的行动和这次尝试尚未达到,一旦尝试出来即予以释放
    // 每一个都达到了;当尝试重播时,它所达到的, 当重播结束时释放出来。
    // 重放结束比未到达空格设定时间早于当尝试开始一项操作时的重播放结束, 而不是提前
    // 记录了尝试。
    private HashSet<TaskId>? _unreached;
    private HashSet<TaskId>? _reached;
    private bool _replaying;

    // 尝试决定的版本,以更改名称。
    private Dictionary<string, int>? _versions;

    // 试图达到的取消点;执行过程首先遵守任务时的点
    // 取消, 记录持有的取消或这种尝试选择的取消; 是否请求取消任务并等待
    // 其点,它的理由。
    private int _cancellationPoints;
    private int? _cancellationPoint;
    private bool _cancellationPending;
    private string? _pendingCancellationReason;

    // 为什么运行中的代码无法重弹处决, 当运行时间停止了这一尝试: 代码的版本
    // 与处决记录不符
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
    /// <remarks>被援引的任务的化身 其行刑树包含这一处决, 读一次。</remarks>
    public override Guid Incarnation => _incarnation ??= _runtime.GetIncarnation(Id);

    /// <summary>
    /// 获取为什么尝试结束, 由运行时间或抽象发现的差异, 或者
    /// <see langword="null"/> 运行时。
    /// </summary>
    internal DurableTaskAttemptCancellationReason? AttemptEndReason => AttemptCancellationReason ?? _endReason;

    /// <summary>获取尝试是否结束。</summary>
    internal bool IsAttemptEnded => AttemptEndReason is not null;

    /// <summary>
    /// 获取是否尝试在没有记录的情况下结束, 所以它永远不会恢复: 激活停止, 或者运行代码
    /// 无法重播处决。
    /// </summary>
    internal bool IsDiscarded => AttemptEndReason
        is DurableTaskAttemptCancellationReason.ActivationStopping
        or DurableTaskAttemptCancellationReason.Diverged
        or DurableTaskAttemptCancellationReason.UnsupportedVersion;

    /// <summary>获取尝试的某个部分是否运行或被排队,所以尝试在没有消息的情况下会取得进展。</summary>
    /// <remarks>
    /// 等待时间已解除,
    /// 工作项。尝试计算从释放时可以运行,所以激活时会进行提醒
    /// 坠机后仍恢复原样
    /// </remarks>
    internal bool IsRunnable => _segment is not null || _queuedSegments > 0 || _resumePending;

    /// <summary>注意到完成的运行时间 <paramref name="awaited"/>,任务或信号记录,可以释放停靠的等待。</summary>
    internal void OnAwaitedCompleted(TaskId awaited)
    {
        AssertOnScheduler();
        if (_awaited?.Contains(awaited) is true)
        {
            _resumePending = true;
        }
    }

    /// <summary>
    /// 在其取消点,即解除停泊等候的等待时间时,将取消这一尝试的任务。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 尝试取消的点数是其机构开始持久行动的起点,以便:
    /// 减去 <see cref="ObserveOperation"/> 和每个耐久等待,例如任务或信号,不论是否
    /// 公园,所以一个重放 发现等待事件已经记录下来 计算了相同的点数。
    /// 到达与其重放的运行相同的点, 当它达到同样的操作时。
    /// </para>
    /// <para>
    /// 执行机关在某一点上观察任务取消时,即执行机关在以下记录下:
    /// <c>{execution}/$cancellation-observed</c> 。
    /// 重放。
    /// 当它到达记录点时, 在操作开始的时候, 观察它, 而它的持久标记 保持未取消
    /// 在此之前,重播一次死刑,因为其企图在记录之前就已终止,
    /// 显示重放结尾点的该台,因为它到达了先前记录的最后一次操作或
    /// 开始一个他们没有开始,记录那个点。 当尝试重播时, 一个请求也来了。
    /// 早期尝试在观察后开始的操作没有, 因此重播选择了点 。
    /// 第一次运行没有记录,因此它注意到在开始点0时先于身体的要求。
    /// </para>
    /// <para>
    /// 交付取消 <see cref="DurableTaskContext.CancellationToken"/>,其登记同步运行,因此
    /// 登记时,发送货物失败或稍后发生该点的操作。
    /// </para>
    /// </remarks>
    /// <exception cref="AggregateException">标记上的一个注册。</exception>
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

    /// <summary>注意到该机构达到了下一个取消点,可以交付待决任务取消。</summary>
    /// <exception cref="AggregateException">标记上的一个注册。</exception>
    private void ReachCancellationPoint()
    {
        _cancellationPoints++;
        DeliverPendingCancellation();
    }

    /// <summary>
    /// 一旦尝试在机构观察时,即交付待决任务取消,并记录
    /// 执行时没有记录。
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
    /// 其身体在达到一个较早取消点之前就已停止,但这一尝试却因时间差而停滞不前
    /// 试图观察任务取消后, 所以运行代码不会重现执行过程 。
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
    /// 任务一旦尝试代码完成, 完成一个任务。 或者,如果尝试被废弃, 一旦没有部分内容, 任务就会完成
    /// 运行,所以尝试在激活时不再使用代码。
    /// </summary>
    internal Task Settled => _settled.Task;

    /// <summary>将运行尝试体的段排队,该部分 <paramref name="start"/> 释放。</summary>
    internal void Start(TaskCompletionSource start) => QueueSegment(start);

    /// <summary>请求在激活时允许释放 <paramref name="gate"/> 的段请求。</summary>
    private void QueueSegment(TaskCompletionSource gate)
    {
        AssertOnScheduler();
        _queuedSegments++;
        _resumePending = false;
        _grainContext.ReceiveLocalRequest(new DurableTaskSegment(this, gate), SegmentOptions);
    }

    /// <summary>
    /// 运行一个允许激活的段,即释放 <paramref name="gate"/>,其等待代码继续
    /// 请求内,并完成一次尝试公园或结束。
    /// </summary>
    /// <remarks>
    /// 被抛弃的尝试没有恢复。
    /// 重新输入的 grain 可以, 继续运行在运行的 。
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

    /// <summary>结束运行的段,所以激活会接受其他请求。</summary>
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
    /// 等待 <paramref name="wait"/>,等待信息传递事件的长期等待,等待时间停放尝试
    /// 等待时间待定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 等待完成后, 将请求恢复等待代码
    /// 除非同时抛弃企图,
    /// </para>
    /// <para>
    /// 图书馆的组合器可以同时等待一次尝试的若干事件。 当继续等待返回时, 库的组合器可以等待一次尝试的多次事件。
    /// 另一只停在车停停停,它的片段结束, 所以另一个等待的简历可以运行, 代码在
    /// 在一个逻辑流中,重放模式要求的一条逻辑流中,最多一个等待
    /// 一次停靠。
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
    /// 运行时间提供以前在试图执行处决时,这棵树上记录到的尝试行动。
    /// 开始:儿童,包括订阅其他任务和记录,分别的任务、信号发送标记,
    /// 和有效记录,但不信号值,发送者记录、援引后代或注销
    /// 尝试在行动开始或发布时到达, 寻找它的手柄,
    /// 标记下标志下的信号,选择其决定之下的完成或在其名称下产生效果。
    /// 操作中没有记录过任何早期尝试, 结束重播; 再次到达操作时不会停止 。
    /// </remarks>
    public override bool IsReplaying => _replaying;

    /// <summary>
    /// 之前的尝试是否记录了这次尝试尚未达到的行动,所以早前的运行已经进行了
    /// 已经超越了尝试的当前点。
    /// </summary>
    /// <remarks>
    /// 处决机构在逻辑上以一个流程运行其持久运作,同时进行儿童处决方面的工作,
    /// 由早期运行创造的顺序 来决定重播
    /// </remarks>
    private bool HasUnreachedOperations => _unreached is not null;

    /// <summary>
    /// 获取执行此执行的 grain ,它符合独立任务根标识符。
    /// 执行开始时没有姓名, 所以不同 grains 上同一标识符的处决产生不同的。
    /// </summary>
    protected internal override string HostId => _runtime.GrainId.ToString();

    /// <summary>注意到这一努力已实现, <paramref name="operationId"/>,它可以结束它的重播。</summary>
    /// <param name="operationId">操作的识别符号。</param>
    /// <param name="recordsOperation">
    /// 操作是否按照时间表将自己记录在一个新的识别符号下,而不是仅仅查找
    /// 现有一个,作为处理查询或完成选择,东道方根据耐久命令进行选择。
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
    /// 获取此执行的逻辑时间: 当任务第一次运行时, 将进度提高到每个完成时间记录
    /// 其最终反应 处决观察。
    /// </summary>
    /// <remarks>
    /// 儿童成绩保持完成时间,所以重放时,以同样的顺序观察同样的孩子,
    /// 因此,在完成之前的工程时开始延误。
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
    /// 立即结束 <paramref name="reason"/> 的这一尝试:从现在起,其持久作业即告结束
    /// <see cref="DurableTaskAttemptCanceledException"/>和放弃的尝试没有恢复。
    /// <see cref="ApplyCancellation"/> 然后取消尝试的标记 。
    /// </summary>
    /// <returns><see langword="true"/> 当此呼叫结束尝试时。</returns>
    /// <remarks>
    /// 运行时间结束在激活调度器上的尝试, 启动的代码运行时, 运行时的尝试会同步运行
    /// 代码可以观察尝试的状态。取消尝试的标记会运行它们的注册,并且可以继续
    /// 同步使用 运行时间将它应用到 其中转操作之外
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

    /// <summary>取消 <see cref="EndAttempt"/> 结束的尝试的标记。</summary>
    /// <exception cref="AggregateException">标记上的一个注册。</exception>
    internal void ApplyCancellation()
    {
        AssertOnScheduler();
        Debug.Assert(_endReason is not null, "Only an ended attempt is canceled.");
        _resumePending |= _parkedWaits > 0 && !IsDiscarded;
        CancelAttempt(_endReason.GetValueOrDefault());
    }

    /// <summary>尝试的段结束,一旦代码结束就解决尝试。</summary>
    internal void OnAttemptCompleted()
    {
        EndSegment();
        _settled.TrySetResult();
    }

    /// <summary>
    /// 拖慢 <paramref name="reason"/>的尝试:运行时间取消并保持任务未完成,所以代码
    /// 稍后再放行刑
    /// </summary>
    /// <returns>停止作业的例外。</returns>
    private DurableTaskAttemptCanceledException Stall(DurableTaskAttemptCancellationReason reason, Exception cause)
    {
        _stallException ??= cause;
        _runtime.StallAttempt(this, reason, cause);
        return CreateAttemptEndedException();
    }

    /// <summary>拖延了这次尝试 因为一次行动偏离了处决记录</summary>
    private DurableTaskAttemptCanceledException Diverge(DurableTaskDivergenceException divergence) =>
        Stall(DurableTaskAttemptCancellationReason.Diverged, divergence);

    /// <summary>抛掷 <see cref="DurableTaskAttemptCanceledException"/> 当运行时间取消此尝试时 。</summary>
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
    /// 以其结果或一旦运行时间即取消尝试,完成这一尝试的操作
    /// 取消尝试。 与执行记录不符的行动会拖慢尝试 。
    /// </summary>
    /// <remarks>
    /// 运行时间取消了一次尝试, 在它发出尝试的标志之前, 所以一个操作完成之间,
    /// 例如等待一个已经堕胎的儿童,最后是试图取消而不是结果。
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
    /// 此主机解开耐用 grain 调用: 目标从该呼叫的序列请求中恢复根任务。 The the name
    /// 抽象的脱钩任意 <see cref="ISchedulableTask"/>,所以此主机拒绝其他人使用
    /// <see cref="NotSupportedException"/> 之前记录或发送任何信息。
    /// 开始时记录在 <c>$detach:{target}:{rootId}</c>下,因此,本执行开始的每一项分离任务
    /// 与部署增加或取消的其他行动无关。
    /// </remarks>
    protected internal override ValueTask<IScheduledTaskHandle> StartTaskAsync(
        TaskId taskId,
        DurableTask task,
        DurableTaskStartOptions options,
        CancellationToken cancellationToken)
    {
        // 盾牌随起始代码流动, 所以在切换调度器之前读取它 。
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
    /// 开始和记录在工作项目中进行, 工作项目要求激活启动时, 所以他们承诺在
    /// 与日记一样的日记写, 并以此来显示工作项目更改了哪个工作项。 返回到记录的回放
    /// 相同的手柄,不重新开始任务。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用器没有在激活的调度器上运行。</exception>
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

    /// <summary>返回记录执行中的分离启动的标识符和开始的请求。</summary>
    /// <exception cref="NotSupportedException">定义不是一个耐久 grain 呼叫。</exception>
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
    /// 丢弃, 除非 <paramref name="taskId"/> 可以是执行该执行时的子 <paramref name="task"/>:
    /// 执行不删除, 并且没有任何效果 早期记录记录 保持操作
    /// 运行,例如一个名为组合器的子输入。
    /// </summary>
    /// <remarks>运行时间检查直接儿童在开始时根据自己的识别特征找到的记录。</remarks>
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
    /// 人体的最终代码 运行在工作项目中 它被称为这个, 所以在这里启动响应 即以同样的方式执行
    /// 日记写时日记表示代码已更改, 任何操作要求写入的, 都更改。
    /// 另一台调度器上的完整内容 其反应通过尝试得到处理, 取消的尝试回应是
    /// 被丢弃的。一个机构在取消点之前结束,当时曾试图遵守任务
    /// 取消协议后,各异,因此尝试取消协议时没有拖延和分阶段。
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
    /// 效果运行,记录被进行到工作项目中, 激活时需要它, 所以记录
    /// 在同一日记中承诺与日记写时的状态相同, 效果改变。
    /// 儿童或儿童
    /// 效果和效果各异:尝试拖延 <see cref="DurableTaskAttemptCancellationReason.Diverged"/>,其
    /// 将两种名称分开, 而不是再次运行效果 。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用器没有在激活的调度器上运行。</exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 或因为早期运行记录了 另一种类型的行动
    /// 身份识别符。
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
    /// 尝试中第一个呼叫的名称, 试图决定版本并记住它, 所以循环和帮助者来检查
    /// 同一修改为同一种版本。没有记录的决定是 <c>0</c> ,而先前的尝试是'。
    /// 而不是 <see cref="IsReplaying"/> 报告重播:
    /// 先前插入的安装位置插入到正文中, 重放结束时不改变前一次运行的距离。 记录
    /// 是在工作项目中进行,它叫这个激活, 并且不会结束重播。
    /// </remarks>
    /// <exception cref="InvalidOperationException">调用器没有在激活的调度器上运行。</exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 运行时间取消了尝试, 例如因为执行版本不在支持范围之内,
    /// 其 <see cref="DurableTaskVersionException"/> 那时,内心是例外。
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
            // 运行时取消尝试并保持任务未完成, 所以支持版本重播的代码 。
            throw Stall(
                DurableTaskAttemptCancellationReason.UnsupportedVersion,
                new DurableTaskVersionException(Id, name, version, minSupported, maxSupported));
        }

        return version;
    }

    /// <summary>
    /// 创建标记的段,从操作身份中记录执行操作:
    /// <c>${kind}:{part}:{part}...</c>, <c>\</c>、 <c>:</c>和 <c>$</c> 在每个部分均以如此明显的方式逃脱
    /// 字符创建独立的区段, <c>$</c> 后缀可以数重复发生。
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
    /// 等待观察所看到的任务的信号 援引任务, 其行刑树包含处决, 并推进
    /// 输入信号的逻辑时间 。
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
    /// 向引用的任务发送信号 <paramref name="taskId"/> 日期 时 <paramref name="target"/>下,记录发送
    /// <c>$send:{target}:{taskId}:{name}</c> 在这个执行中,重播不会再次发送它。
    /// </summary>
    /// <remarks>
    /// 信号的识别身份名记录 所以添加或删除其他操作 并不能改变
    /// 执行时重复发送一个信号,例如
    /// <see cref="DurableSignalResult.TaskNotFound"/>,按发生时编号, <c>$send:{target}:{taskId}:{name}$2</c>
    /// 如此,每个目标都达到了自己的记录;目标报告值相等,
    /// <see cref="DurableSignalResult.Duplicate"/> 和不同的 <see cref="DurableSignalResult.Conflict"/>。
    /// 任务取消执行不会放弃发送; 尝试取消会放弃。
    /// </remarks>
    /// <param name="throwOnRejection">是否因为某项任务而拒绝 grain 而不是被记录和归还。</param>
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

    /// <summary>将操作的标记与试图取消连接,从而结束每次尝试。</summary>
    private CancellationTokenSource CreateOperationCancellationSource(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            AttemptCancellationToken);

    /// <summary>运行启动调度器运行的入口 <paramref name="callback"/>。</summary>
    /// <exception cref="InvalidOperationException">调用器没有在激活的调度器上运行。</exception>
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
    /// 除非呼叫器运行在启动状态 <paramref name="scheduler"/>上,否则会投出投手。
    /// </summary>
    /// <remarks>
    /// 引爆程序上继续有尸体在排程器上进行 <c>ConfigureAwait(false)</c>, 其中
    /// ORLEANS0226 报告。
    /// </remarks>
    private static void ThrowIfOffScheduler(TaskScheduler scheduler)
    {
        if (TaskScheduler.Current != scheduler)
        {
            throw new InvalidOperationException(
                "A durable task's operations run on the scheduler of the grain activation which runs it. Code which continues elsewhere, for example after ConfigureAwait(false), can't run them.");
        }
    }

    /// <summary>包住执行此操作的控点, 启动调度器运行该控点。</summary>
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
        /// 返回独立任务或已订阅任务的 <see cref="DurableTaskAddress"/> ,以
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
