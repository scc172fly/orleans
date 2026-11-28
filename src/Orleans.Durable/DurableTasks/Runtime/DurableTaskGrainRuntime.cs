using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Protocol;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 为一个 Grain 激活实例运行持久化任务。
/// </summary>
/// <remarks>
/// <para>
/// 任务状态、出站消息和逻辑时间共用 Grain 的日志。每项操作都先验证并准备
/// 变更，然后只暂存可以安全提交的变更。所有调用方共享暂存的变更，它们
/// 随下一次成功的日志写入提交；存储写入失败会隔离日志，并停用 Grain。
/// </para>
/// <para>
/// 运行时也是一个不写入条目的日志状态机。捕获操作标记依赖所
/// 捕获变更的效果，在写入完成时运行这些效果：启动已提交的请求、以已提交的
/// 响应完成句柄，以及传播已提交的取消。
/// </para>
/// <para>
/// 延迟将发送给自身 Grain 的恢复消息与延迟状态一同暂存，并将
/// <see cref="Orleans.Durable.Messaging.Envelope.DeliverAt"/> 设置为延迟的到期时间。Grain 自身的收件箱
/// 在与延迟相同的日志写入中接受该消息，因此只要延迟已持久化，其恢复消息也已持久化；收件箱
/// 在消息到期时处理它。适配器不自行调度持久化作业，而是向收件箱注册其唤醒延迟，由后者
/// 报告其可运行的执行实例，Messaging 的唤醒操作会为此重新激活 Grain。
/// </para>
/// <para>
/// 运行时是其完成通知消息的 <see cref="IOutboxMessageSource"/>：发件箱将每条完成通知的引用写入日志，
/// 运行时根据任务已存储的结果具体化消息，因此在完成通知待处理期间，结果只写入日志
/// 一次。任务的完成通知目的地是其订阅者：调用方（使用任务
/// 自身的标识符）及订阅现有任务的持久化执行实例的子任务（使用它们自己的
/// 标识符）。本 Grain 上的订阅者在存储结果的同一次写入中接收结果。转为死信的
/// 完成通知的死信记录保存结果的副本，而
/// 任务在保留期内保留自身的结果，以便轮询及重新调用仍可观察该结果。已存储的结果如果
/// 无法再解码，只会使读取该结果的操作失败：发件箱将其完成通知转为死信，而该通知的退役
/// 不解码结果，激活时也从不解码已存储的结果。
/// </para>
/// <para>
/// 作为请求调度的被调用任务拥有一棵执行树：该任务及其不属于其他被调用任务的后代节点，
/// 例如本地子任务、延迟、完成决策、分离任务记录，
/// 效果记录、对其他任务的订阅，以及远程子任务存根。任务标识符由调用方选择，因此
/// 被调用的后代任务保留自身的生命周期。
/// 当被调用的任务处于终态且没有待处理的完成通知时，运行时删除其执行树：在最后一条完成通知
/// 作为已投递退役的写入中删除（因为该目的地已持久化保存结果）；否则，在经过
/// <see cref="Orleans.Configuration.DurableTaskOptions.ResultRetentionPeriod"/> 指定的时长后删除，从任务完成或某条
/// 完成通知转为死信时起计算。没有已存储祖先的任务，例如在持久化
/// 执行实例之外调度的远程子任务，也按相同的保留期安排删除。删除执行树会取消其中的非终态任务：它暂存
/// 向每个待完成的远程子任务传播取消，并取消每次正在运行的执行尝试；随后丢弃这些尝试的结果。
/// 发往已删除任务或任务执行树先前一次运行的完成通知与恢复消息均被忽略：根任务 ID 的每次运行
/// 都具有自身的运行世代标识，指定其节点的消息的标识符包含该标识，具体参见
/// <see cref="CallerScope"/>。先于调用到达的取消或信号
/// 会被记录，之后调用会在自身执行树中找到该记录。Grain 允许调用准入，直到
/// <see cref="Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow"/> 规定的时长自发送方
/// 首次暂存调用时起经过为止；迟到的调用转为死信。若对应调用一直未到达，则相关记录在经过
/// <see cref="DurableTaskGrainRuntimeShared.EarlyRecordRetention"/> 指定的时长后删除，该时长涵盖发送方与
/// Grain 的时钟之间的偏差。
/// 对 Grain 未作为被调用任务保留的任务的订阅，会立即以未找到任务为结果完成。
/// </para>
/// <para>
/// 任务取消是协作式的：请求记录在任务状态中，取消正在运行的执行尝试的
/// <see cref="DurableTaskContext.CancellationToken"/>，并仅使叶任务（例如延迟
/// 以及尚未启动主体的任务）进入终态。已经启动的主体自行决定其任务的结果；恢复过程在应用取消
/// 请求的情况下重放主体。中止操作在一次写入中将任务执行树以失败状态提交，并取消执行树中正在运行的
/// 执行尝试，其结果将被丢弃。
/// </para>
/// <para>
/// 执行尝试取消用于停止尝试：停用、中止及删除执行树都会取消执行尝试。已取消的
/// 执行尝试的持久化操作抛出 <see cref="DurableTaskAttemptCanceledException"/>；它对日志化状态所做的变更，
/// 例如清理操作，随 Grain 的下一次日志写入提交，而其令牌在之后的工作项中取消。
/// 被中止或删除的执行尝试会排空：其挂起的等待因取消而恢复。因停用或
/// 停滞而被丢弃的执行尝试永远不会恢复；停用仅等待其正在运行的代码段，然后下一激活实例
/// 重放其任务。
/// </para>
/// <para>
/// 每次执行尝试按代码段运行，即激活实例上的本地请求；参见 <see cref="GrainDurableTaskContext"/>。
/// 与 Grain 状态一样，运行时状态仅限于激活实例的调度器，因此不使用锁。
/// </para>
/// </remarks>
internal sealed partial class DurableTaskGrainRuntime(
    IDurableTaskGrainStorage storage,
    [FromKeyedServices("$task-logical-times")] IDurableDictionary<TaskId, DateTimeOffset> logicalTimes,
    [FromKeyedServices("$task-invocations")] IDurableDictionary<MessageId, PendingInvocation> invocations,
    DurableTaskGrainRuntimeShared shared,
    IEnumerable<IDurableTaskMessageTransport> messageTransports) :
    IDurableTaskGrainRuntime,
    IDurableTaskGrainExtension,
    IDurableTaskManager,
    IStateMachine,
    IOutboxMessageSource
{
    /// <summary>运行时提交钩子的日志状态机名称。</summary>
    internal const string StateMachineName = "orleans.durable-tasks.runtime";

    // 与 Grain 状态一样，运行时的状态仅限于激活实例的调度器。
    private readonly Dictionary<TaskId, GrainDurableTaskContext> _executionContexts = [];
    private readonly Dictionary<TaskId, RunningRequest> _runningRequests = [];
    private readonly Dictionary<TaskId, TaskHandle> _taskHandles = [];
    private readonly HashSet<TaskId> _pendingStarts = [];
    private readonly Dictionary<TaskId, DurableTaskResponse> _pendingHandleResponses = [];
    private readonly Dictionary<long, CancellationPropagation> _pendingCancellationPropagations = [];
    private readonly Dictionary<long, Task> _backgroundTasks = [];
    private readonly DurableTaskGrainRuntimeShared _shared = shared;
    private readonly IDurableTaskGrainStorage _storage = storage;
    private readonly IDurableDictionary<TaskId, DateTimeOffset> _logicalTimes = logicalTimes;
    private readonly IDurableDictionary<MessageId, PendingInvocation> _invocations = invocations;
    private readonly IDurableTaskMessageTransport? _messageTransport = messageTransports.SingleOrDefault();
    private readonly CancellationTokenSource _deactivationCts = new();

    // 因正在运行的代码无法重放而使执行尝试停滞、仍处于待完成状态的执行实例。Grain
    // 在保留此类执行实例期间每隔 DurableTaskOptions.StalledExecutionRetryInterval 唤醒一次，以便之后的激活实例重放它。
    private readonly HashSet<TaskId> _stalledExecutions = [];
    private TaskId[] _committingStarts = [];
    private KeyValuePair<TaskId, DurableTaskResponse>[] _committingHandleResponses = [];
    private long[] _committingCancellationPropagations = [];

    // 已提交的效果，等待对执行尝试实施任务取消；该取消由先前写入或同一次写入提交，
    // 按提交顺序排列；若无效果等待，则为 null；参见 RunCommittedEffects。
    private List<CommittedEffects>? _orderedCommittedEffects;

    // 恢复过程中找到的执行实例，它们在激活完成后启动。
    private List<TaskId>? _recoveredStarts;
    private bool _recovered;
    private Task? _stopTask;
    private long _backgroundWriteId;
    private long _cancellationPropagationId;
    private IGrainContext? _grainContext;
    private bool _stopping;

    // 发件箱中的待处理完成通知，按消息 ID 索引。发件箱在运行时观察恢复之前解析已恢复的完成通知，
    // 因此首次使用时从任务状态构建索引，之后在完成通知发送及
    // 退役时维护该索引。
    private Dictionary<MessageId, PendingCompletion>? _pendingCompletions;

    // 保留结果或取消记录可能过期的最早时间。激活后该时间未知，
    // 因此第一次检查扫描所有任务；此后，启动保留期或记录取消的每项变更
    // 都会降低该时间，每次扫描则根据剩余任务重新计算该时间。
    private DateTimeOffset _nextExpiry = DateTimeOffset.MinValue;

    public GrainId GrainId => GrainContext.GrainId;

    /// <summary>获取运行时所绑定的激活实例。</summary>
    /// <remarks>
    /// 该绑定是显式的，因此在激活实例调度器之外运行的代码（此时环境 Grain 上下文是
    /// 托管客户端的上下文）仍能读取此激活实例的标识。
    /// </remarks>
    internal IGrainContext GrainContext => _grainContext ?? throw CreateUnboundException();

    public DateTimeOffset UtcNow => _shared.TimeProvider.GetUtcNow();

    private IDurableTaskMessageTransport MessageTransport => _messageTransport ?? throw new InvalidOperationException(
        "Durable messaging is not configured. Call AddDurableTasks on the silo builder.");

    /// <summary>将运行时绑定到 <paramref name="grainContext"/>，即它负责运行其中执行实例的激活实例。</summary>
    internal void InitializeForActivation(IGrainContext grainContext) => _grainContext = grainContext;

    /// <summary>
    /// 在激活实例生命周期中恢复已提交的任务状态，并将恢复的执行实例延迟至
    /// <see cref="StartRecoveredExecutions"/>.
    /// </summary>
    internal Task RecoverAsync(CancellationToken cancellationToken)
    {
        _recovered = true;
        return ResumePendingTasksCoreAsync(cancellationToken);
    }

    /// <summary>将 <see cref="RecoverAsync"/> 恢复的执行实例启动一次。</summary>
    /// <remarks>
    /// 在 Grain 的 <c>OnActivateAsync</c> 完成后运行，因此与 Grain 调用一样，恢复的 Grain 代码观察到已初始化的
    /// Grain。如果某个调用率先到达已恢复的任务，该调用会启动任务，本操作会跳过该任务。
    /// </remarks>
    internal void StartRecoveredExecutions()
    {
        if (_recoveredStarts is not { } starts)
        {
            return;
        }

        _recoveredStarts = null;

        foreach (var taskId in starts)
        {
            StartRequest(taskId);
        }
    }

    /// <summary>
    /// 返回最先完成的候选任务：完成序列号最低的已存储候选任务（不需要
    /// 记录），或者在 <paramref name="decisionId"/> 下记录的胜出任务。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存储在暂存每个结果时为该结果标记序列号，之后的结果会获得更大的
    /// 序列号。结果已暂存的候选任务在与所有序列号更大的结果相同的写入中，或者更早的写入中
    /// 提交，因此当某个候选任务的句柄完成时，序列号更小的所有候选任务均已存储。
    /// 因此，无论是根据当前已暂存的结果进行选择的实时执行尝试，
    /// 还是可能有更多候选任务已完成的任何重放，最低的已存储序列号都相同。
    /// </para>
    /// <para>
    /// 候选任务可能在没有序列号的情况下完成：例如已取消祖先的未存储子任务，或者在存储结果时
    /// 尚未标记序列号的任务。当此类完成结果最先被观察到，或者
    /// 已存在记录的决策时，选择记录在 <paramref name="decisionId"/> 下，重放从
    /// 该记录读取选择结果。
    /// </para>
    /// </remarks>
    public async ValueTask<TaskId> SelectCompletionAsync(
        GrainDurableTaskContext attempt,
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        cancellationToken.ThrowIfCancellationRequested();
        if (_storage.TryGetTask(decisionId, out var decision))
        {
            return await SelectRecordedCompletionAsync(attempt, decisionId, decision, candidates, cancellationToken);
        }

        for (var observedCompletion = false; ; observedCompletion = true)
        {
            var (first, unordered) = FindFirstStoredCompletion(candidates);
            if (!unordered && first >= 0)
            {
                return candidates[first];
            }

            // 在没有已存储且带序列号的结果的情况下完成的句柄没有持久化顺序。
            if (unordered || observedCompletion)
            {
                return await SelectRecordedCompletionAsync(attempt, decisionId, decision: null, candidates, cancellationToken);
            }

            DurableTaskEvents.EmitSelectionWaitStarted(GrainContext, decisionId);
            await WaitForAnyAsync(attempt, candidates, cancellationToken);
            ThrowIfStopping();
        }
    }

    /// <summary>
    /// 返回完成序列号最低的已存储候选任务的索引（若无则为 -1），并返回是否有候选任务的
    /// 已存储结果没有序列号。
    /// </summary>
    /// <remarks>仅读取已存储的完成元数据，不解码结果。</remarks>
    private (int Index, bool Unordered) FindFirstStoredCompletion(IReadOnlyList<TaskId> candidates)
    {
        var first = -1;
        var firstSequence = long.MaxValue;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (!_storage.TryGetTask(candidates[index], out var state) || !HasStoredResult(state))
            {
                continue;
            }

            var sequence = state.CompletionSequence;
            if (sequence <= 0)
            {
                return (-1, true);
            }

            if (sequence < firstSequence)
            {
                (first, firstSequence) = (index, sequence);
            }
        }

        return (first, false);
    }

    /// <summary>
    /// 返回在 <paramref name="decisionId"/> 下记录的胜出任务，或按候选任务顺序记录第一个
    /// 句柄已完成的候选任务。
    /// </summary>
    private async ValueTask<TaskId> SelectRecordedCompletionAsync(
        GrainDurableTaskContext attempt,
        TaskId decisionId,
        IDurableTaskState? decision,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken)
    {
        var fingerprint = GetCompletionDecisionFingerprint(candidates);
        if (decision is not null)
        {
            if (decision.HasRequest
                || !decision.RemoteTarget.IsDefault
                || decision.RemoteRequestFingerprint is not null
                || !decision.CallerId.IsDefault
                || decision.DueTime is not null
                || (decision.RequestFingerprint is { } existingFingerprint
                    && !string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal))
                || (decision.RequestFingerprint is null
                    && TryGetScheduledTaskHandle(decisionId, out _)))
            {
                throw CreateDivergenceException(
                    $"Durable completion decision '{decisionId}' is already associated with another operation.",
                    decision,
                    DescribeSelection(candidates.Count));
            }

            if (decision.RequestFingerprint is null)
            {
                _storage.SetRequestFingerprint(decisionId, decision, fingerprint);
            }

            if (ReadRecordedResult(decisionId, decision) is { IsCompleted: true } recorded)
            {
                var winner = recorded.GetResult<TaskId>();
                if (!candidates.Contains(winner))
                {
                    throw new DurableTaskDivergenceException(
                        $"Durable completion decision '{decisionId}' was recorded for candidate '{winner}', which is not in the supplied candidate set.");
                }

                return winner;
            }
        }
        else
        {
            decision = GetOrCreateTask(decisionId, DurableTaskRecordKind.Selection, request: null);
            _storage.SetRequestFingerprint(decisionId, decision, fingerprint);
        }

        while (true)
        {
            // 多个候选任务已经完成时，候选任务顺序中的第一个胜出。
            foreach (var candidate in candidates)
            {
                var response = await GetCompletionCandidateHandle(candidate).PollAsync(
                    new PollingOptions { PollTimeout = TimeSpan.Zero },
                    cancellationToken);
                if (!response.IsCompleted)
                {
                    continue;
                }

                decision = GetOrCreateTask(decisionId, DurableTaskRecordKind.Selection, request: null);
                StoreResult(decisionId, decision, DurableTaskResponse.FromResult(candidate));
                await WriteStateAsync(cancellationToken);
                return candidate;
            }

            await WaitForAnyAsync(attempt, candidates, cancellationToken);
        }
    }

    /// <summary>
    /// 等待任意候选任务的句柄完成，并在此期间挂起 <paramref name="attempt"/>，之后取消并排空
    /// 其他等待操作。
    /// </summary>
    private async Task WaitForAnyAsync(GrainDurableTaskContext attempt, IReadOnlyList<TaskId> candidates, CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waits = new Task<DurableTaskResponse>[candidates.Count];
        for (var index = 0; index < waits.Length; index++)
        {
            waits[index] = GetCompletionCandidateHandle(candidates[index]).WaitAsync(waitCancellation.Token).AsTask();
        }

        try
        {
            // 等待失败会传播，如同轮询失败一样。
            await await attempt.ParkAsync(Task.WhenAny(waits), candidates);
        }
        finally
        {
            waitCancellation.Cancel();
            await ((Task)Task.WhenAll(waits)).ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private IScheduledTaskHandle GetCompletionCandidateHandle(TaskId taskId)
    {
        if (TryGetScheduledTaskHandle(taskId, out var handle))
        {
            return handle;
        }

        // 选择操作接收已调度的输入。祖先的取消会拒绝启动这些输入，且不存储它们。
        return IsAncestorCancellationRequested(taskId, state: null, shielded: false)
            ? new CompletedTaskHandle(taskId, DurableTaskResponse.Canceled)
            : GetScheduledTaskHandle(taskId);
    }

    // 请求指纹是十六进制哈希，因此绝不会具有此前缀。
    private const string CompletionDecisionFingerprintPrefix = "$completion-decision:";

    /// <summary>
    /// 为在已记录另一操作的标识符下调度操作的情况创建错误，这就是与已记录的执行实例
    /// 发生分歧的重放所呈现的错误。执行尝试的上下文以该错误使执行尝试停滞。
    /// </summary>
    private static DurableTaskDivergenceException CreateDivergenceException(string message, IDurableTaskState recorded, string requested) =>
        new(DescribeDivergence(message, recorded, requested));

    /// <summary>
    /// 返回先前的执行尝试在 <paramref name="taskId"/> 下记录的结果，并在首次访问时解码。
    /// </summary>
    /// <exception cref="DurableTaskDivergenceException">已记录的结果无法再解码。</exception>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any decoding failure is a divergence from the record.")]
    private static DurableTaskResponse? ReadRecordedResult(TaskId taskId, IDurableTaskState state)
    {
        try
        {
            return state.Result;
        }
        catch (Exception exception)
        {
            throw new DurableTaskDivergenceException(
                $"The outcome which an earlier attempt recorded for durable task '{taskId}' no longer decodes: {exception.Message} A replay reads every recorded outcome; code which decodes it replays the execution.",
                exception);
        }
    }

    /// <summary>为在已记录另一操作的任务标识符下调度请求的情况创建错误。</summary>
    private static DurableTaskConflictException CreateScheduleConflictException(
        TaskId taskId,
        string message,
        IDurableTaskState recorded,
        string requested) =>
        new(taskId, DescribeDivergence(message, recorded, requested));

    private static string DescribeDivergence(string message, IDurableTaskState recorded, string requested) =>
        $"{message} It records {DescribeOperation(recorded)}, and this attempt requested {requested}. A replay schedules the same operation under each identifier: a call or request invokes the same method on the same target, whatever its arguments; new workflow steps use new identifiers.";

    private static string DescribeOperation(IDurableTaskState state) => state switch
    {
        { RemoteTarget.IsDefault: false } => DescribeCall(state.RemoteTarget, state.Method, state.RemoteRequestFingerprint),
        { DueTime: { } dueTime } => $"a delay due at {dueTime:O}",
        { Kind: DurableTaskRecordKind.Delay } => "a delay",
        { RequestFingerprint: { } fingerprint } when fingerprint.StartsWith(CompletionDecisionFingerprintPrefix, StringComparison.Ordinal) => "a selection of the first task to complete",
        _ when TryGetDetachedTask(state, out var target, out var rootId) => DescribeDetachedStart(target, rootId, state.Method),
        { RequestFingerprint: { } fingerprint } when fingerprint.StartsWith(SignalSendFingerprintPrefix, StringComparison.Ordinal) => "a signal's send",
        { RequestFingerprint: { } fingerprint } when fingerprint.StartsWith(SignalFingerprintPrefix, StringComparison.Ordinal) => "a signal's value",
        { RequestFingerprint: { } fingerprint } when TryParseIdentity(SubscriptionFingerprintPrefix, fingerprint, out var target, out var subscribed) => DescribeSubscription(target, subscribed),
        { RequestFingerprint: EffectFingerprint } => "a DurableTask.RunInline effect",
        { RequestFingerprint: VersionFingerprint } => "a version decision",
        { Kind: DurableTaskRecordKind.CancellationObserved } => "the point at which the execution observed its cancellation",
        { Method: { } method } => $"a request for {DescribeMethod(method)} ({AbbreviateFingerprint(state.RequestFingerprint)})",
        { RequestFingerprint: { } fingerprint } => $"a request ({AbbreviateFingerprint(fingerprint)})",
        { Kind: DurableTaskRecordKind.EarlyCancellation } => "a cancellation which arrived before its request",
        _ => "a local child task",
    };

    /// <summary>为诊断描述请求：其方法及参数的指纹。</summary>
    private string DescribeRequest(IDurableTaskRequest request) =>
        $"a request for {DescribeMethod(IDurableTaskRequest.GetMethod(request))} ({AbbreviateFingerprint(IDurableTaskRequest.GetFingerprint(request, _shared.Serializer))})";

    /// <summary>为诊断描述对另一 Grain 的调用：其目标、方法及参数的指纹。</summary>
    private string DescribeCall(GrainId target, IDurableTaskRequest request) =>
        DescribeCall(target, IDurableTaskRequest.GetMethod(request), IDurableTaskRequest.GetFingerprint(request, _shared.Serializer));

    private static string DescribeCall(GrainId target, string? method, string? fingerprint) =>
        $"a call to {DescribeMethod(method)} on grain '{target}' ({AbbreviateFingerprint(fingerprint)})";

    private static string DescribeMethod(string? method) => method is null ? "an unrecorded method" : $"method '{method}'";

    // WhenAny、Race、超时及信号截止时间以相同方式记录选择，因此说明不专门指其中任何一种。
    private static string DescribeSelection(int candidateCount) =>
        $"a selection of the first task to complete among {candidateCount} {(candidateCount == 1 ? "candidate" : "candidates")}";

    private static string AbbreviateFingerprint(string? fingerprint) =>
        fingerprint is null ? "no fingerprint" : $"fingerprint {fingerprint[..Math.Min(12, fingerprint.Length)]}";
    private static string GetCompletionDecisionFingerprint(IReadOnlyList<TaskId> candidates)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var candidate in candidates)
        {
            var value = Encoding.UTF8.GetBytes(candidate.ToString());
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
            hash.AppendData(length);
            hash.AppendData(value);
        }

        return $"{CompletionDecisionFingerprintPrefix}{Convert.ToHexString(hash.GetHashAndReset())}";
    }

    /// <summary>
    /// 创建新的执行上下文，并将其注册到本地执行上下文集合中。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <returns>新的执行上下文。</returns>
    /// <remarks>
    /// 在任务已被请求取消之后启动或重放的执行尝试会在此处收到取消请求，并
    /// 主体在取消点观察到取消：该点是执行实例在较早的执行尝试首次
    /// 观察到取消时记录的点；若无记录，则为首次运行的开始处或重放结束处。此后，其
    /// <see cref="DurableTaskContext.CancellationToken"/> 被取消，
    /// <see cref="DurableTaskContext.IsTaskCancellationRequested"/> 被设置，且
    /// <see cref="DurableTaskContext.TaskCancellationReason"/> 报告已记录的原因。
    /// </remarks>
    private GrainDurableTaskContext CreateExecutionContext(TaskId taskId)
    {
        var utcNow = GetOrCreateLogicalTime(taskId);

        if (!_executionContexts.TryGetValue(taskId, out var context))
        {
            _executionContexts[taskId] = context = new(taskId, this, GrainContext, TaskScheduler.Current, utcNow);
        }

        if (_storage.TryGetTask(taskId, out var taskState) && taskState.CancellationRequestedAt.HasValue)
        {
            context.DeliverTaskCancellation(taskState.CancellationReason);
        }

        return context;
    }

    private DateTimeOffset GetOrCreateLogicalTime(TaskId taskId)
    {
        if (_logicalTimes.TryGetValue(taskId, out var utcNow))
        {
            return utcNow;
        }

        utcNow = UtcNow;
        _logicalTimes[taskId] = utcNow;
        return utcNow;
    }

    /// <summary>
    /// 若存在与所提供任务对应的执行上下文，则获取并返回它。
    /// </summary>
    /// <param name="taskId">要获取其执行上下文的任务。</param>
    /// <param name="executionContext">执行上下文。</param>
    /// <returns><see langword="true"/> 表示找到执行上下文；否则为 <see langword="false"/>。</returns>
    private bool TryGetExecutionContext(TaskId taskId, [NotNullWhen(true)] out GrainDurableTaskContext? executionContext) => _executionContexts.TryGetValue(taskId, out executionContext);

    private TaskHandle GetOrCreateRunningTaskHandle(TaskId taskId, GrainId remoteTarget = default)
    {
        var handle = GetOrAddTaskHandle(taskId, remoteTarget);
        handle.IsRunning = true;
        return handle;
    }

    private TaskHandle GetOrAddTaskHandle(TaskId taskId, GrainId remoteTarget = default)
    {
        if (!_taskHandles.TryGetValue(taskId, out var handle))
        {
            _taskHandles[taskId] = handle = new TaskHandle(taskId, this, remoteTarget);
        }

        return handle;
    }

    private bool TryRegisterCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (destination.GrainId.IsDefault || destination.GrainId.IsClient() || state.CompletionDestinations.Contains(destination))
        {
            return false;
        }

        _storage.AddCompletionDestination(taskId, state, destination);
        return true;
    }

    private void ValidateCaller(TaskId taskId, IDurableTaskState state, GrainId callerId)
    {
        if (callerId.IsDefault)
        {
            return;
        }

        // 存储记录来自其他 Grain 的每次调用的调用方，因此检查不解码任何请求。
        var existingCaller = state.CallerId;
        if (!existingCaller.IsDefault && existingCaller != callerId)
        {
            throw new DurableTaskConflictException(
                taskId,
                $"Durable task '{taskId}' is already associated with caller '{existingCaller}', not '{callerId}'.");
        }
    }

    private void StageCaller(TaskId taskId, IDurableTaskState state, GrainId callerId)
    {
        if (!callerId.IsDefault && state.CallerId.IsDefault)
        {
            _storage.SetCallerId(taskId, state, callerId);
        }
    }

    /// <summary>向终态任务的其中一个订阅者发送结果。</summary>
    /// <remarks>
    /// 其他 Grain 上的订阅者通过发件箱接收完成通知，由运行时根据已存储的
    /// 结果提供通知，任务保留结果直到完成通知退役。本 Grain 上的订阅者在同一次写入中直接
    /// 接收结果，并立即退役。
    /// </remarks>
    private void SendCompletion(TaskId taskId, DurableTaskSubscriber destination)
    {
        if (destination.GrainId == GrainId)
        {
            var found = _storage.TryGetTask(taskId, out var state);
            Debug.Assert(found && state!.Result is { IsCompleted: true }, "A completion is sent for a stored result.");
            CompleteSubscription(destination.TaskId, taskId, state!.Result!);
            RetireDestination(taskId, state, destination, deadLettered: false);
            return;
        }

        var transport = MessageTransport;
        var messageId = transport.GetCompletionMessageId(GrainId, destination, taskId);
        PendingCompletions[messageId] = new(taskId, destination);
        transport.Send(messageId);
    }

    private Dictionary<MessageId, PendingCompletion> PendingCompletions
    {
        get
        {
            if (_pendingCompletions is { } pending)
            {
                return pending;
            }

            // 每个已完成任务的目的地都在发件箱中具有待处理的完成通知：完成通知与
            // 结果一同暂存，发件箱将通知退役时移除该目的地。本 Grain 上的目的地
            // 在存储结果的写入中退役。
            pending = [];
            var transport = MessageTransport;
            foreach (var (taskId, state) in _storage.Tasks)
            {
                if (state.CompletedAt.HasValue)
                {
                    foreach (var destination in state.CompletionDestinations)
                    {
                        Debug.Assert(destination.GrainId != GrainId, "This grain's subscribers retire with the result.");
                        pending[transport.GetCompletionMessageId(GrainId, destination, taskId)] = new(taskId, destination);
                    }
                }
            }

            return _pendingCompletions = pending;
        }
    }

    /// <remarks>
    /// 运行时提供其待处理调用表保存的调用，以及其任务的各
    /// 目的地的完成通知。它绝不撤回其中任何一项：待处理调用保留在表中，待处理完成通知的
    /// 目的地保留其任务和结果，直到 <see cref="IOutboxMessageSource.OnRetired"/> 将其移除为止；具有目的地的任务
    /// 永远不会被删除。解码已存储结果时发生的异常会传播，因此发件箱
    /// 将完成通知转为死信。
    /// </remarks>
    bool IOutboxMessageSource.TryGetMessage(MessageId messageId, out OutboxMessage message)
    {
        if (_invocations.TryGetValue(messageId, out var invocation))
        {
            message = MessageTransport.CreateInvocation(messageId, invocation.Target, invocation.Payload);
            return true;
        }

        if (!PendingCompletions.TryGetValue(messageId, out var completion))
        {
            message = default;
            return false;
        }

        var found = _storage.TryGetTask(completion.TaskId, out var state);
        Debug.Assert(found && state!.Result is { IsCompleted: true }, "A pending completion retains its task's result.");
        message = MessageTransport.CreateCompletionMessage(
            messageId,
            GrainId,
            completion.Destination,
            completion.TaskId,
            state!.Result!);
        return true;
    }

    /// <summary>
    /// 在启动已退役调用的记录上记录其结果，或者移除已退役完成通知的
    /// 目的地；一旦结果已投递到所有目的地，就删除任务及其执行树。
    /// </summary>
    /// <remarks>
    /// 发件箱在同一次日志写入中暂存消息移除操作，因此这些变更随退役一同提交。
    /// 退役只更新任务的元数据，因此即使已存储结果无法再解码也能成功；轮询此类
    /// 结果会向轮询方呈现解码错误。
    /// </remarks>
    void IOutboxMessageSource.OnRetired(MessageId messageId, OutboxMessageRetirement retirement)
    {
        if (_invocations.TryGetValue(messageId, out var invocation))
        {
            _invocations.Remove(messageId);
            RetireInvocation(invocation, retirement);
            return;
        }

        var removed = PendingCompletions.Remove(messageId, out var completion);
        Debug.Assert(removed, "The outbox retires only the invocations and completions which the runtime provides.");
        if (!_storage.TryGetTask(completion.TaskId, out var state))
        {
            Debug.Fail("A pending completion's task is retained until the completion retires.");
            return;
        }

        RetireDestination(
            completion.TaskId,
            state,
            completion.Destination,
            deadLettered: retirement is not OutboxMessageRetirement.Delivered);
    }

    /// <summary>
    /// 移除已保存终态任务结果或不再能够接收该结果的目的地，并在
    /// 不再有任何目的地时删除该任务及其执行树。
    /// </summary>
    /// <remarks>
    /// 已投递的目的地已持久化保存结果，因此任务无需为其保留结果。没有 Grain
    /// 调用方的任务（例如客户端调度的任务，或持久化执行实例启动的分离任务）在完成后的保留期内
    /// 保留结果，以便轮询可以观察到结果。转为死信的完成通知会重新开始结果的保留期，因此轮询方
    /// 或再次调用该任务的调用方仍可观察到结果；随后任务在该保留期结束时删除。
    /// </remarks>
    private void RetireDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination, bool deadLettered)
    {
        Debug.Assert(IsInvoked(state), "Only invoked tasks have completion destinations.");
        _storage.RemoveCompletionDestination(taskId, state, destination);
        if (deadLettered)
        {
            // 保留期从现在开始：死信可能在很长的重试期后才产生，结果必须在完整的保留期内
            // 对轮询方或重新调用可用。
            _storage.RetainResult(taskId, state);
        }

        if (state.CompletionDestinations.Count > 0)
        {
            return;
        }

        // 因完成通知转为死信而重新开始的保留期会延续到投递结束之后。
        if (state.RetainedSince.HasValue || state.CallerId.IsDefault)
        {
            TrackExpiry(GetRetentionExpiry(state));
        }
        else
        {
            DeleteTree(taskId, state);
        }
    }

    /// <summary>发件箱中待处理的完成通知。</summary>
    private readonly record struct PendingCompletion(TaskId TaskId, DurableTaskSubscriber Destination);

    private void StageCancellation(TaskId taskId, GrainId target, string? reason) =>
        MessageTransport.SendCancellation(GrainId, target, GetWireId(taskId), reason);

    private ValueTask WriteStateAsync(CancellationToken cancellationToken) => _storage.WriteAsync(cancellationToken);

    /// <summary>返回任务的状态；若任务不存在，则暂存其状态。</summary>
    private IDurableTaskState GetOrCreateTask(TaskId taskId, DurableTaskRecordKind kind, IDurableTaskRequest? request)
    {
        var reportCreation = DurableTaskEvents.IsTaskCreatedEnabled && !_storage.TryGetTask(taskId, out _);
        var state = _storage.GetOrCreateTask(taskId, kind, request);
        if (reportCreation)
        {
            DurableTaskEvents.EmitTaskCreated(GrainContext, taskId);
        }

        return state;
    }

    /// <summary>暂存任务的终态结果，并返回暂存的结果。</summary>
    /// <remarks>
    /// 存储在暂存任何内容之前序列化结果；若结果无法序列化（例如值或异常的
    /// 类型没有编解码器），则抛出异常。任务随后以 <see cref="SerializerException"/> 失败，
    /// 该异常指出类型，并像其他结果一样提交，因此任务会结束，而不会一直保存一个无法通过任何写入
    /// 提交的结果。
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A result which can't be stored is stored as that failure.")]
    private DurableTaskResponse SetResponse(TaskId taskId, IDurableTaskState state, DurableTaskResponse response)
    {
        try
        {
            _storage.SetResponse(taskId, state, response);
        }
        catch (Exception exception)
        {
            var (kind, value) = response.Exception is { } failure ? ("exception", (object)failure) : ("result", response.Result);
            _shared.Logger.LogWarning(exception, "{Id} the {Kind} of durable task '{TaskId}' doesn't serialize.", GrainId, kind, taskId);
            response = DurableTaskResponse.FromException(new SerializerException(
                $"The {kind} of durable task '{taskId}', of type '{value?.GetType()}', can't be stored because it doesn't serialize: {exception.GetType().Name}: {exception.Message}"));
            _storage.SetResponse(taskId, state, response);
        }

        DurableTaskEvents.EmitTaskCompleted(GrainContext, taskId, response);
        return response;
    }

    /// <summary>暂存执行实例的新终态记录，例如效果的结果或版本决策。</summary>
    private void AddRecord(TaskId taskId, DurableTaskRecordKind kind, string fingerprint, DurableTaskResponse response)
    {
        _storage.AddRecord(taskId, kind, fingerprint, response);
        DurableTaskEvents.EmitTaskCreated(GrainContext, taskId);
        DurableTaskEvents.EmitTaskCompleted(GrainContext, taskId, response);
    }

    /// <summary>暂存任务的删除操作。</summary>
    private void RemoveTask(TaskId taskId)
    {
        if (_storage.RemoveTask(taskId))
        {
            DurableTaskEvents.EmitTaskDeleted(GrainContext, taskId);
        }
    }


    /// <summary>
    /// 暂存对另一个 Grain 上 <paramref name="request"/> 的调用，将其作为远程子任务 <paramref name="taskId"/>，
    /// 不执行写入，并返回子任务已记录的终态响应，或者 <see cref="DurableTaskResponse.Pending"/>。
    /// </summary>
    private DurableTaskResponse StageRemote(TaskId taskId, IDurableTaskRequest request, bool shielded)
    {
        var transport = MessageTransport;
        var context = IDurableTaskRequest.GetContext(request);
        if (context.TargetId == GrainId)
        {
            throw new InvalidOperationException(
                $"Durable task '{taskId}' cannot invoke its owning grain '{GrainId}' through durable messaging.");
        }

        var method = IDurableTaskRequest.GetMethod(request);
        if (_storage.TryGetTask(taskId, out var state))
        {
            if (state.HasRequest
                || state.RequestFingerprint is not null
                || !state.CallerId.IsDefault
                || state.DueTime is not null
                || (!state.RemoteTarget.IsDefault && state.RemoteTarget != context.TargetId)
                || (state.Method is { } recordedMethod && !string.Equals(recordedMethod, method, StringComparison.Ordinal)))
            {
                throw CreateDivergenceException(
                    $"Durable task '{taskId}' is already associated with a different request.",
                    state,
                    DescribeCall(context.TargetId, request));
            }

            if (ReadRecordedResult(taskId, state) is { IsCompleted: true } completed)
            {
                return completed;
            }
        }

        state ??= GetOrCreateTask(taskId, DurableTaskRecordKind.RemoteCall, request: null);
        MarkShielded(taskId, state, shielded);
        if (state.RemoteTarget.IsDefault || state.Method is null)
        {
            _storage.SetRemoteRequest(taskId, state, context.TargetId, method, IDurableTaskRequest.GetFingerprint(request, _shared.Serializer));
        }

        context.CallerId = GrainId;
        context.SupportsDurableCompletion = true;

        // 已记录的接受状态表明调用已经退役，且目标已保存任务。
        if (state.Accepted is null)
        {
            var wireId = GetWireId(taskId);
            SendInvocation(transport.CreateInvocation(GrainId, context.TargetId, wireId, request), request, recordId: wireId);
        }

        return DurableTaskResponse.Pending;
    }

    // 执行树中的操作指纹以 '$' 开头，因此它们绝不会等于十六进制的请求指纹。
    private const string EffectFingerprint = "$effect";

    private static bool IsEffectRecord(IDurableTaskState state) => state.Kind == DurableTaskRecordKind.Effect;

    /// <summary>
    /// 返回 <paramref name="effectId"/> 记录的结果，或运行 <paramref name="effect"/> 并暂存其结果的
    /// 记录，而不执行写入。
    /// </summary>
    /// <remarks>
    /// 调用方在激活实例上运行，因此记录和效果所做的状态变更在同一
    /// 工作项中暂存，并在下一次日志写入中提交，无论哪个操作请求写入。该记录是执行树的终态
    /// 条目，并随执行树删除。结果的序列化方式与任务结果相同；如果
    /// 结果无法序列化，则将该失败作为结果记录和返回，以便每次运行均观察到相同的失败。
    /// 如果效果的标识符下存在另一类型的记录，或该标识符下有命名组合器的输入，则属于重放分歧。
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A result which can't be recorded is recorded as that failure.")]
    DurableTaskResponse IDurableTaskGrainRuntime.RunEffect<TState>(
        TaskId effectId,
        TState state,
        Func<TState, DurableTaskResponse> effect)
    {
        ThrowIfStopping();
        if (_storage.TryGetTask(effectId, out var record))
        {
            if (!IsEffectRecord(record) || ReadRecordedResult(effectId, record) is not { IsCompleted: true } outcome)
            {
                throw CreateDivergenceException(
                    $"Durable child task '{effectId}' is already associated with a different operation.",
                    record,
                    "a DurableTask.RunInline effect");
            }

            return outcome;
        }

        // 命名组合器仅在其标识符下记录其输入。
        if (_storage.GetChildren(effectId).FirstOrDefault() is (var inputId, { } input))
        {
            throw CreateDivergenceException(
                $"Durable child task '{effectId}' holds '{inputId}', which an earlier run recorded.",
                input,
                "a DurableTask.RunInline effect");
        }

        var response = effect(state);
        try
        {
            AddRecord(effectId, DurableTaskRecordKind.Effect, EffectFingerprint, response);
        }
        catch (Exception exception) when (response.Exception is null)
        {
            response = DurableTaskResponse.FromException(exception);
            AddRecord(effectId, DurableTaskRecordKind.Effect, EffectFingerprint, response);
        }

        return response;
    }

    // 版本记录与效果记录一样，是其执行实例的终态记录，其结果为版本。
    private const string VersionFingerprint = "$version";

    /// <summary>执行实例下版本决策记录的段前缀。</summary>
    internal const string VersionSegmentPrefix = "$version:";

    /// <remarks>
    /// 从当前执行实例沿其外层执行实例（例如步骤的父执行实例）向上查找，并在以下位置停止：
    /// 拥有执行树的被调用任务，因此被调用方或被调用后代任务的决策仍各自独立。该记录
    /// 是执行树的终态条目，在调用工作项中暂存，并随执行树删除。
    /// </remarks>
    int IDurableTaskGrainRuntime.GetOrRecordVersion(TaskId executionId, string name, int version)
    {
        ThrowIfStopping();
        var segment = VersionSegmentPrefix + name;
        for (var execution = executionId; !execution.IsDefault; execution = execution.Parent())
        {
            if (_storage.TryGetTask(execution.Child(segment), out var recorded))
            {
                var decision = ReadRecordedResult(execution.Child(segment), recorded);
                Debug.Assert(
                    IsVersionRecord(recorded) && decision is { IsCompleted: true },
                    "Only version decisions are recorded under version segments.");
                return decision!.GetResult<int>();
            }

            if (_storage.TryGetTask(execution, out var state) && IsInvoked(state))
            {
                break;
            }
        }

        var recordId = executionId.Child(segment);
        AddRecord(recordId, DurableTaskRecordKind.Version, VersionFingerprint, DurableTaskResponse.FromResult(version));
        DurableTaskEvents.EmitVersionRecorded(GrainContext, executionId, name, version);
        return version;
    }

    private static bool IsVersionRecord(IDurableTaskState state) => state.Kind == DurableTaskRecordKind.Version;

    /// <summary>保存执行实例观察到取消的取消点的记录的段。</summary>
    internal const string CancellationPointSegment = "$cancellation-observed";

    private const string CancellationPointFingerprint = "$cancellation-observed";

    /// <remarks>先前执行尝试的取消投递记录此取消点，后续执行尝试读取它。</remarks>
    public int? GetCancellationPoint(TaskId executionId)
    {
        if (!_storage.TryGetTask(executionId.Child(CancellationPointSegment), out var record))
        {
            return null;
        }

        Debug.Assert(
            IsCancellationPointRecord(record) && record.Result is { IsCompleted: true },
            "Only cancellation points are recorded under the cancellation point segment.");
        return record.Result!.GetResult<int>();
    }

    /// <remarks>
    /// 该记录是执行树的终态条目，在调用工作项中暂存，因此随
    /// Grain 的下一次日志写入提交，并随执行树删除。在该次写入提交之前启动的重放
    /// 以没有记录的执行实例的方式投递取消。
    /// </remarks>
    public void RecordCancellationPoint(TaskId executionId, int point)
    {
        Debug.Assert(!_stopping, "A stopping activation's attempts are canceled, so none observes its task's cancellation.");
        var recordId = executionId.Child(CancellationPointSegment);
        Debug.Assert(!_storage.TryGetTask(recordId, out _), "The first observation of an execution's cancellation records its point.");
        AddRecord(recordId, DurableTaskRecordKind.CancellationObserved, CancellationPointFingerprint, DurableTaskResponse.FromResult(point));
    }

    private static bool IsCancellationPointRecord(IDurableTaskState state) => state.Kind == DurableTaskRecordKind.CancellationObserved;

    void IDurableTaskGrainRuntime.ThrowIfRecordedAsEffect(TaskId operationId, TaskId effectId, string requested)
    {
        if (_storage.TryGetTask(effectId, out var recorded) && IsEffectRecord(recorded))
        {
            throw CreateDivergenceException(
                $"Durable task '{operationId}' runs beneath '{effectId}', which an earlier run recorded.",
                recorded,
                requested);
        }
    }

    public async ValueTask CancelRemoteAsync(TaskId taskId, GrainId target, string? reason, CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        if (!_storage.TryGetTask(taskId, out var state) || state.Result is { IsCompleted: true })
        {
            return;
        }

        if (state.RemoteTarget.IsDefault || state.RemoteTarget != target)
        {
            throw new InvalidOperationException(
                $"Durable task '{taskId}' is not associated with remote target '{target}'.");
        }

        // 取消及其消息一同提交；发件箱仅在提交之后投递消息，并在
        // 该次发送的准入截止时间内重试。已记录的请求（例如重放重复的请求）
        // 因此不暂存任何内容：发件箱将消息退役之后再次发送相同标识会成为一次新发送，
        // 已处理第一次发送的接收方会因截止时间不同而拒绝它。
        if (!state.CancellationRequestedAt.HasValue)
        {
            _storage.RequestCancellation(taskId, state, reason);
            StageCancellation(taskId, target, reason);
        }

        await WriteStateAsync(cancellationToken);
    }

    /// <summary>通过本地任务的句柄请求取消该任务。</summary>
    /// <remarks>
    /// 取消叶任务（例如延迟或尚未启动主体的任务）在记录请求的写入中
    /// 以已取消状态进入终态。已启动的主体通过其持久化令牌观察请求，并决定自己的结果。
    /// </remarks>
    internal async ValueTask CancelScheduledTaskAsync(TaskId taskId, string? reason, CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        if (!_storage.TryGetTask(taskId, out var state) || state.Result is { IsCompleted: true })
        {
            return;
        }

        if (!IsCancellationLeaf(taskId, state))
        {
            await SignalCancellationAsync(taskId, callerId: default, reason, cancellationToken);
            return;
        }

        var propagation = StageCancellationTree(taskId, callerId: default, reason);
        var propagated = StagePropagation(propagation.Contexts, propagation.Handles, reason);
        await CommitResponseAsync(taskId, DurableTaskResponse.Canceled, cancellationToken);
        await propagated.WaitAsync(cancellationToken);
    }

    /// <summary>返回任务的主体是否已开始运行；主体开始时会记录执行实例的逻辑时间。</summary>
    private bool HasStarted(TaskId taskId) => _logicalTimes.ContainsKey(taskId);

    /// <summary>
    /// 返回取消请求是否使任务以已取消状态进入终态，而不运行负责决定其
    /// 结果的主体。
    /// </summary>
    /// <remarks>
    /// 延迟和任务体尚未启动的任务是叶节点。远程子任务的被调用方决定该子任务的结果，而
    /// 已启动的主体在其持久化令牌被取消的情况下运行或重放。
    /// </remarks>
    private bool IsCancellationLeaf(TaskId taskId, IDurableTaskState state) =>
        state.RemoteTarget.IsDefault && (state.DueTime is not null || !HasStarted(taskId));

    public async ValueTask<DurableTaskResponse> ScheduleDelayAsync(
        TaskId taskId,
        DateTimeOffset dueTime,
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        var transport = MessageTransport;
        if (_storage.TryGetTask(taskId, out var state))
        {
            // 延迟仅根据延迟自身的记录，或主体即为延迟的被调用任务的记录进行重放：
            // 另一操作的结果或状态绝不会被当作该延迟的结果或状态。
            if (state.Kind != DurableTaskRecordKind.Delay
                && !(state.Kind == DurableTaskRecordKind.Invocation && TryGetExecutionContext(taskId, out _)))
            {
                throw CreateDivergenceException(
                    $"Durable delay '{taskId}' is already associated with another operation.",
                    state,
                    $"a delay due at {dueTime:O}");
            }

            if (ReadRecordedResult(taskId, state) is { IsCompleted: true } completed)
            {
                return completed;
            }

            if (state.DueTime is { } existingDueTime)
            {
                // 持久化延迟是一项承诺：重放保留已记录的到期时间，即使工作流现在
                // 请求另一到期时间（例如配置的时长发生了变化）。
                if (existingDueTime != dueTime)
                {
                    _shared.Logger.LogWarning(
                        "{Id} durable delay '{TaskId}' is due at {RecordedDueTime:O}; its replay requested {RequestedDueTime:O}. The recorded due time is kept.",
                        GrainId,
                        taskId,
                        existingDueTime,
                        dueTime);
                }

                // 重放可能观察到由写入未完成的先前执行尝试暂存的延迟。
                await WriteStateAsync(cancellationToken);
                return DurableTaskResponse.Pending;
            }
        }

        state ??= GetOrCreateTask(taskId, DurableTaskRecordKind.Delay, request: null);
        var generation = checked(state.ResumeGeneration + 1);
        _storage.SetDelay(taskId, state, dueTime, generation);

        // Grain 自身的收件箱在与延迟相同的日志写入中接受恢复消息，所以每个持久化延迟都具有
        // 持久化恢复消息。
        transport.SendResume(GrainId, taskId, GetIncarnation(taskId), generation, dueTime);
        await WriteStateAsync(cancellationToken);
        return DurableTaskResponse.Pending;
    }

    internal ValueTask<DurableTaskResponse> ScheduleDelayAsync(
        TaskId taskId,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        ScheduleDelayAsync(
            taskId,
            GetOrCreateLogicalTime(taskId) + duration,
            cancellationToken);

    /// <summary>
    /// 暂存计划的恢复消息已到期的延迟的结果，并在提交后完成其句柄。
    /// </summary>
    /// <param name="taskId">延迟。</param>
    /// <param name="incarnation">调度恢复消息时延迟所属执行树的运行世代标识。</param>
    /// <param name="generation">调度恢复消息时延迟的世代编号。</param>
    /// <param name="sender">发送恢复消息的 Grain，即本 Grain。</param>
    /// <remarks>
    /// 已被请求取消的延迟以已取消状态进入终态。具有相同运行世代标识和
    /// 世代编号的待处理延迟会完成。对于延迟执行树先前运行的恢复消息（后续运行中具有相同
    /// 标识符的延迟无法通过世代编号区分）、针对较早世代编号的恢复消息、针对终态延迟的恢复消息，或针对不存在
    /// 任务的恢复消息，均不会产生变更。
    /// </remarks>
    internal void ResumeDelayFromInbox(TaskId taskId, Guid incarnation, long generation, GrainId sender)
    {
        ThrowIfStopping();
        if (sender != GrainId)
        {
            throw new InvalidOperationException(
                $"Durable delay '{taskId}' does not accept resumes from grain '{sender}'.");
        }

        if (!_storage.TryGetTask(taskId, out var state)
            || state.Result is { IsCompleted: true }
            || GetIncarnation(taskId) != incarnation)
        {
            return;
        }

        if (state.CancellationRequestedAt.HasValue)
        {
            _pendingHandleResponses[taskId] = StageResponse(taskId, DurableTaskResponse.Canceled);
            return;
        }

        if (state.DueTime is null || state.ResumeGeneration != generation)
        {
            return;
        }

        _pendingHandleResponses[taskId] = StageResponse(taskId, DurableTaskResponse.Completed);
    }

    private DurableTaskResponse StageAcceptedResponse(TaskId taskId, IDurableTaskState state, DurableTaskResponse response)
    {
        if (!response.IsCompleted)
        {
            throw new InvalidOperationException("Only terminal durable task responses can be accepted.");
        }

        if (state.Result is { IsCompleted: true } terminal)
        {
            return terminal;
        }

        StoreResult(taskId, state, response);
        return response;
    }

    /// <summary>
    /// 暂存 <paramref name="sender"/> 的任务的终态响应，该任务由本 Grain 的一个节点等待：远程
    /// 子任务自身的结果，或订阅子任务订阅的现有任务 <paramref name="sourceTaskId"/> 的结果，
    /// 完成通知使用传输标识符 <paramref name="wireId"/> 标识该节点。本地句柄
    /// 在提交后完成。
    /// </summary>
    /// <remarks>
    /// 收件箱直接将完成通知接受到任务状态中，因此该已存储的响应是其唯一的日志化副本。
    /// 发送方保留其结果，直到收件箱将接受状态报告给发送方的发件箱。若完成通知针对
    /// 已不存在的节点，或其所属的执行树运行不同于传输标识符
    /// 所指定运行的节点，均会被接受但不改变状态：等待该通知的运行已随其执行树删除，
    /// 该删除操作取消了节点，且不再有任何操作等待其结果。已经结束的订阅（例如因为它被
    /// 取消）也接受完成通知，但不产生变更。
    /// </remarks>
    internal void AcceptResponseFromInbox(TaskId wireId, TaskId sourceTaskId, DurableTaskResponse response, GrainId sender)
    {
        ThrowIfStopping();
        if (!TryGetNode(wireId, out var taskId, out var state))
        {
            return;
        }

        ThrowIfNotStartedBy(taskId, wireId, sourceTaskId, state, sender);

        // 任务的结果表明其 Grain 已保存任务，因此记录接受状态，该状态原本由调用的
        // 退役记录。
        if (state.Accepted is null && AwaitsAcceptance(state))
        {
            _storage.SetAccepted(taskId, state, accepted: true);
            SignalAcceptance(taskId);
        }

        if (TryGetSubscription(state, out _, out _))
        {
            CompleteSubscription(taskId, sourceTaskId, response);
            return;
        }

        _pendingHandleResponses[taskId] = StageAcceptedResponse(taskId, state, response);
    }

    /// <summary>恢复已提交的任务状态，并启动恢复的执行实例。</summary>
    internal async Task ResumePendingTasksAsync(CancellationToken cancellationToken)
    {
        await RecoverAsync(cancellationToken);
        StartRecoveredExecutions();
    }

    /// <summary>返回任务是否存储了其终态结果，而不解码该结果。</summary>
    /// <remarks>
    /// 存储在每个已存储的结果上标记 <see cref="IDurableTaskState.CompletedAt"/>；这些结果始终是终态结果。
    /// 激活以此方式检查任务是否完成，因此无法再解码的已存储结果只会使读取该结果的操作失败，
    /// 例如具体化完成通知或轮询结果。
    /// </remarks>
    private static bool HasStoredResult(IDurableTaskState state) => state.CompletedAt.HasValue;

    private async Task ResumePendingTasksCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        List<TaskId>? starts = null;
        var tasks = _storage.Tasks.ToList();
        foreach (var (taskId, state) in tasks)
        {
            if (!HasStoredResult(state)
                && state.HasRequest
                && state.DueTime is null
                && (!state.CancellationRequestedAt.HasValue || !IsCancellationLeaf(taskId, state)))
            {
                GetOrAddTaskHandle(taskId);
            }
        }

        foreach (var (taskId, state) in tasks)
        {
            if (HasStoredResult(state))
            {
                continue;
            }

            // 已取消的叶任务进入终态。远程子任务等待被调用方的结果；已启动的主体在下面重放，
            // 或者随父任务重放，此时其持久化令牌已被取消。
            if (state.CancellationRequestedAt.HasValue && IsCancellationLeaf(taskId, state))
            {
                await SetResponseAsync(taskId, DurableTaskResponse.Canceled, cancellationToken);
                continue;
            }

            // 待处理延迟的恢复消息随延迟一同持久化，收件箱在其到期时处理消息。
            if (state.DueTime is not null)
            {
                continue;
            }

            if (!state.HasRequest
                || IsRequestRunning(taskId))
            {
                continue;
            }

            (starts ??= []).Add(taskId);
        }

        if (starts is not null)
        {
            _recoveredStarts = starts;
        }

        if (DeleteExpiredTasks())
        {
            await WriteStateAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 持久化地调度针对本实例的调用请求。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>一个指示请求状态的 <see cref="DurableTaskResponse"/>。类型为 <see cref="PendingDurableTaskResponse"/> 的响应表示调用方可再次调用本方法来轮询完成结果。</returns>
    /// <exception cref="InvalidOperationException"></exception>
    async ValueTask<DurableTaskResponse> IDurableTaskServer.ScheduleAsync(
        TaskId taskId,
        IDurableTaskRequest request,
        CancellationToken cancellationToken)
    {
        // 保留期在截止时间结束；即使 Grain 处于空闲状态，截止时间仍可能到来；本次写入将删除操作
        // 与请求一同提交。
        ThrowIfStopping();
        DeleteExpiredTasks();
        var scheduled = StageSchedule(taskId, request, fromInbox: false);

        // 在开始执行之前提交请求标识和调用方的订阅。
        await WriteStateAsync(cancellationToken);
        if (scheduled is CanceledRequest { Response: var completed })
        {
            CompleteTaskHandle(taskId, completed);
            if (DeleteExpiredTasks())
            {
                await WriteStateAsync(cancellationToken);
            }
        }

        if (scheduled is StartedRequest)
        {
            StartRequest(taskId);
        }

        return GetResponse(scheduled);
    }

    /// <summary>
    /// 暂存通过收件箱收到的请求，并在收件箱批次提交后启动该请求。
    /// </summary>
    internal DurableTaskResponse ScheduleFromInbox(TaskId taskId, IDurableTaskRequest request) =>
        StageScheduleWithCommitEffects(taskId, request, fromInbox: true);

    /// <summary>
    /// 暂存请求，在提交暂存变更的写入之后启动请求，或完成其句柄。
    /// </summary>
    private DurableTaskResponse StageScheduleWithCommitEffects(TaskId taskId, IDurableTaskRequest request, bool fromInbox)
    {
        var scheduled = StageSchedule(taskId, request, fromInbox);
        if (scheduled is CanceledRequest { Response: var completed })
        {
            _pendingHandleResponses[taskId] = completed;
        }

        if (scheduled is StartedRequest)
        {
            _pendingStarts.Add(taskId);
        }

        return GetResponse(scheduled);
    }

    private ScheduledRequest StageSchedule(TaskId taskId, IDurableTaskRequest request, bool fromInbox)
    {
        ThrowIfStopping();

        // 只有来自其他 Grain 的调用才会将任务存储在保留的调用方作用域根下。
        if (!fromInbox)
        {
            CallerScope.ThrowIfReserved(taskId);
        }

        if (request.Context is not { } requestContext)
        {
            throw new InvalidOperationException($"No context for durable task request {request}");
        }

        if (requestContext.TargetId != GrainId)
        {
            throw new InvalidOperationException(
                $"The durable task request targets grain '{requestContext.TargetId}', not receiver '{GrainId}'.");
        }

        if (!fromInbox)
        {
            requestContext.CallerId = default;
            requestContext.SupportsDurableCompletion = false;
        }
        else if (requestContext.CallerId == GrainId)
        {
            // 完成通知绝不会寻址到任务自身的 Grain：StageRemote 拒绝自调用。
            throw new InvalidOperationException(
                $"Durable task '{taskId}' cannot be invoked by its owning grain '{GrainId}' through durable messaging.");
        }

        var method = IDurableTaskRequest.GetMethod(request);
        if (!_storage.TryGetTask(taskId, out var state))
        {
            state = GetOrCreateTask(taskId, DurableTaskRecordKind.Invocation, request);
            StageCaller(taskId, state, requestContext.CallerId);
            _storage.SetRequestFingerprint(taskId, state, IDurableTaskRequest.GetFingerprint(request, _shared.Serializer), method);
            _storage.SetIncarnation(taskId, state, Guid.NewGuid());
            var subscribedNew = requestContext.SupportsDurableCompletion
                && TryRegisterCompletionDestination(taskId, state, GetInvoker(requestContext.CallerId, taskId));
            return new StartedRequest(subscribedNew ? DurableTaskResponse.Subscribed : DurableTaskResponse.Pending);
        }

        if (!state.RemoteTarget.IsDefault
            || state.RemoteRequestFingerprint is not null)
        {
            throw CreateScheduleConflictException(
                taskId,
                $"Durable task '{taskId}' is already associated with a remote child request.",
                state,
                DescribeRequest(request));
        }

        ValidateCaller(taskId, state, requestContext.CallerId);
        if (!state.HasRequest
            && state.RequestFingerprint is null
            && state.CancellationRequestedAt is null)
        {
            throw CreateScheduleConflictException(
                taskId,
                $"Durable task '{taskId}' is already associated with a non-request operation.",
                state,
                DescribeRequest(request));
        }

        // 当请求为同一调用方调用相同的方法时，该请求与已存储任务属于同一操作，调用方已由
        // ValidateCaller 检查：参数序列化形式不同的重试仍到达已存储的任务。执行实例自身操作的记录
        // （例如效果）具有指纹，但不调用任何方法。
        if (state.Method is { } recordedMethod
                ? !string.Equals(recordedMethod, method, StringComparison.Ordinal)
                : state.RequestFingerprint is not null)
        {
            throw CreateScheduleConflictException(
                taskId,
                $"Durable task '{taskId}' is already associated with a different request.",
                state,
                DescribeRequest(request));
        }

        StageCaller(taskId, state, requestContext.CallerId);
        if (state.Method is null)
        {
            _storage.SetRequestFingerprint(taskId, state, IDurableTaskRequest.GetFingerprint(request, _shared.Serializer), method);
        }

        // 在首次存储调用时生成该次运行的运行世代标识；先前取消的记录
        // 早于该调用。该记录成为调用的记录。
        if (state.Kind == DurableTaskRecordKind.EarlyCancellation)
        {
            _storage.SetInvocation(taskId, state);
        }

        if (state.Incarnation == Guid.Empty)
        {
            _storage.SetIncarnation(taskId, state, Guid.NewGuid());
        }

        // 取消先于启动的调用以已取消状态进入终态。重复调用所到达的已启动主体
        // 决定自身的结果。
        if (state.CancellationRequestedAt.HasValue
            && state.Result is not { IsCompleted: true }
            && IsCancellationLeaf(taskId, state))
        {
            // 先于调用的取消没有记录请求。
            if (!state.HasRequest)
            {
                _storage.SetRequest(taskId, state, request);
            }

            if (requestContext.SupportsDurableCompletion)
            {
                TryRegisterCompletionDestination(taskId, state, GetInvoker(requestContext.CallerId, taskId));
            }

            var canceled = StageResponse(taskId, DurableTaskResponse.FromException(new OperationCanceledException()));
            return new CanceledRequest(canceled);
        }

        if (state.Result is { IsCompleted: true } completed)
        {
            if (ShouldSendCompletion(taskId, state, requestContext))
            {
                SendCompletion(taskId, GetInvoker(requestContext.CallerId, taskId));
            }

            return new CompletedRequest(completed);
        }

        var subscribed = requestContext.SupportsDurableCompletion
            && TryRegisterCompletionDestination(taskId, state, GetInvoker(requestContext.CallerId, taskId));

        // 由写入未完成的执行尝试暂存的请求尚未启动。
        return new StartedRequest(subscribed ? DurableTaskResponse.Subscribed : DurableTaskResponse.Pending);
    }

    /// <summary>暂存操作已存储请求；提交该请求的写入完成后，请求将启动。</summary>
    private sealed class StartedRequest(DurableTaskResponse response)
    {
        public DurableTaskResponse Response { get; } = response;
    }

    /// <summary>请求到达了已经完成的任务。</summary>
    private sealed class CompletedRequest(DurableTaskResponse response)
    {
        public DurableTaskResponse Response { get; } = response;
    }

    /// <summary>
    /// 取消先于请求到达，因此暂存操作将任务存储为已取消状态，并在
    /// 提交任务的写入完成后完成请求的句柄。
    /// </summary>
    private sealed class CanceledRequest(DurableTaskResponse response)
    {
        public DurableTaskResponse Response { get; } = response;
    }

    /// <summary>暂存请求的结果。</summary>
    private readonly union ScheduledRequest(StartedRequest, CompletedRequest, CanceledRequest);

    private static DurableTaskResponse GetResponse(ScheduledRequest scheduled) => scheduled switch
    {
        StartedRequest started => started.Response,
        CompletedRequest completed => completed.Response,
        CanceledRequest canceled => canceled.Response,
    };

    /// <summary>
    /// 返回 <paramref name="taskId"/> 的完成通知发往其调用方 Grain 的目的地，该目的地标识
    /// 调用方自己的子任务。
    /// </summary>
    private static DurableTaskSubscriber GetInvoker(GrainId callerId, TaskId taskId) =>
        new(callerId, CallerScope.GetCallerTaskId(callerId, taskId));

    private bool ShouldSendCompletion(
        TaskId taskId,
        IDurableTaskState state,
        DurableTaskRequestContext requestContext)
    {
        if (!requestContext.SupportsDurableCompletion
            || requestContext.CallerId.IsDefault
            || requestContext.CallerId.IsClient())
        {
            return false;
        }

        if (state.CompletionDestinations.Contains(GetInvoker(requestContext.CallerId, taskId)))
        {
            // 发往该目的地的完成通知仍在发件箱中待处理。
            return false;
        }

        return TryRegisterCompletionDestination(taskId, state, GetInvoker(requestContext.CallerId, taskId));
    }

    private void StartRequest(TaskId taskId)
    {
        if (_stopping || IsRequestRunning(taskId))
        {
            return;
        }

        if (!_storage.TryGetTask(taskId, out var state)
            || state.Result is { IsCompleted: true })
        {
            return;
        }

        // 取消先于启动的请求没有能够观察取消的主体。
        if (state.CancellationRequestedAt.HasValue && IsCancellationLeaf(taskId, state))
        {
            TrackBackgroundTask(
                SetResponseAsync(taskId, DurableTaskResponse.Canceled, CancellationToken.None),
                "terminalizing a canceled deferred durable task");
            return;
        }

        if (!TryReadRequest(taskId, state, out var committedRequest))
        {
            return;
        }

        var executionContext = CreateExecutionContext(taskId);
        _ = GetOrCreateRunningTaskHandle(taskId);
        committedRequest.SetTarget(GrainContext);
        StartInvocation(taskId, static value => value.CreateTask(), committedRequest, executionContext);
    }

    /// <summary>
    /// 返回 <paramref name="taskId"/> 所存储的请求；如果该请求无法再
    /// 解码，则隔离任务。
    /// </summary>
    /// <remarks>
    /// 被隔离的任务保持待处理状态，其存储的请求保持不变，并且停滞：运行时在每个激活实例中
    /// 通过 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 报告一次该情况，并保留 Grain 的低频唤醒操作，因此
    /// 之后的激活实例（例如其代码能够解码请求的激活实例）可以启动任务。Grain 的其他任务不受影响，
    /// 轮询将该任务报告为待处理。中止该任务会使任务结束。
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any decoding failure quarantines only the task which stores the request.")]
    private bool TryReadRequest(TaskId taskId, IDurableTaskState state, [NotNullWhen(true)] out IDurableTaskRequest? request)
    {
        try
        {
            request = state.Request;
            return request is not null;
        }
        catch (Exception exception)
        {
            request = null;
            if (_stalledExecutions.Add(taskId))
            {
                ReportStall(
                    taskId,
                    DurableTaskAttemptCancellationReason.Diverged,
                    new DurableTaskDivergenceException(
                        $"The request which durable task '{taskId}' stores no longer decodes: {exception.GetType().Name}: {exception.Message} The task stays pending with its stored request, and code which decodes the request runs it.",
                        exception));
            }

            return false;
        }
    }

    /// <summary>
    /// Grain 持有可运行的执行实例时报告有工作，使收件箱的持久化唤醒重新激活该 Grain，并
    /// 在停用或崩溃之后，恢复过程恢复执行实例。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 当执行尝试的某个代码段正在运行或已排队时，该尝试可运行。已挂起的执行尝试等待由
    /// 消息投递的内容：子任务结果、信号、延迟恢复或多个完成通知中的第一个。该消息
    /// 重新激活 Grain。日志写入提交时也会使执行尝试可运行：它启动所接受的请求，
    /// 完成其存储的结果及信号的句柄，并传播其记录的取消。
    /// 收件箱在准备每次写入时查询此来源，此时运行时尚未捕获这些效果；收件箱确保
    /// 在写入能够持久化之前安排唤醒。已提交的取消在后台工作中传播，后台工作在结束前
    /// 一直计为待处理工作。
    /// </para>
    /// <para>
    /// 激活开始时，Messaging 会先检查其工作来源，然后此运行时才恢复执行实例，因此
    /// 在恢复运行之前，存储所保存的待处理执行实例均视为可运行。这样可以根据
    /// 已恢复的状态重新安排唤醒，因此已提交的工作永远不会搁置。
    /// </para>
    /// <para>
    /// Messaging 在激活实例停用期间也会查询这些信息，直至其消息泵停止。运行时在停用开始时丢弃每个
    /// 执行尝试，因此不再有可运行的执行尝试，此时运行的唤醒操作会直接结束，不安排
    /// 后续唤醒，使激活实例停止时已可运行、或其等待事件在停止期间提交的执行实例
    /// 无法继续执行。因此，正在停止的运行时也会将待处理执行实例报告为可运行。由此保持唤醒的
    /// 已挂起执行实例只会多花一次激活，其恢复过程会再次将其挂起。
    /// </para>
    /// <para>
    /// 延迟 <see cref="Orleans.Configuration.DurableTaskOptions.RunnableExecutionWakeupDelay"/> 限制
    /// 崩溃的 Grain 恢复可运行的执行尝试的最晚时间。唤醒仅在崩溃或停用后有意义：当
    /// Grain 处于活动状态时，唤醒只是重新调度自身，因此较长的延迟可以以较低成本完成此操作，而非不断轮询。
    /// </para>
    /// <para>
    /// 停滞的执行实例等待能够重放它的代码，这种代码只有后续激活才会运行，因此它保留低频
    /// 唤醒计划 <see cref="Orleans.Configuration.DurableTaskOptions.StalledExecutionRetryInterval"/>：当前激活结束后（例如
    /// 滚动部署停止其 Silo），该唤醒会重新激活 Grain，并由恢复过程重放执行实例。
    /// </para>
    /// </remarks>
    internal TimeSpan? GetWakeupDelay() =>
        HasRunnableAttempts()
        || _pendingStarts.Count > 0
        || _pendingHandleResponses.Count > 0
        || _pendingCancellationPropagations.Count > 0
        || _backgroundTasks.Count > 0
        || _recoveredStarts is not null
        || ((!_recovered || _stopping) && HasPendingExecutions())
            ? _shared.Options.RunnableExecutionWakeupDelay
            : HasStalledExecutions() ? _shared.Options.StalledExecutionRetryInterval : null;

    private bool HasRunnableAttempts()
    {
        foreach (var context in _executionContexts.Values)
        {
            if (context.IsRunnable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>返回存储是否保存恢复过程会运行的执行实例。</summary>
    private bool HasPendingExecutions()
    {
        foreach (var (_, state) in _storage.Tasks)
        {
            if (!HasStoredResult(state) && state.HasRequest && state.DueTime is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>返回停滞的执行实例是否仍待完成，并移除已结束（例如因中止而结束）的执行实例的记录。</summary>
    private bool HasStalledExecutions()
    {
        _stalledExecutions.RemoveWhere(taskId => !_storage.TryGetTask(taskId, out var state) || HasStoredResult(state));
        return _stalledExecutions.Count > 0;
    }

    /// <summary>
    /// 取消当前运行代码无法重放的执行实例的执行尝试，使其任务保持待处理，并报告
    /// 停滞情况。
    /// </summary>
    /// <remarks>
    /// 以停用、中止或删除取消执行尝试的方式取消该尝试，因此其之后的持久化操作会抛出异常，
    /// 其结果会被丢弃。任务不保存结果，而 Grain 的低频唤醒操作使之后的
    /// 激活实例能够重放任务。
    /// </remarks>
    void IDurableTaskGrainRuntime.StallAttempt(GrainDurableTaskContext context, DurableTaskAttemptCancellationReason reason, Exception exception)
    {
        Debug.Assert(
            reason is DurableTaskAttemptCancellationReason.UnsupportedVersion or DurableTaskAttemptCancellationReason.Diverged,
            "An attempt stalls when the running code can't replay its execution.");
        if (!CancelAttempt(context, reason))
        {
            return;
        }

        _stalledExecutions.Add(context.Id);
        ReportStall(context.Id, reason, exception);
    }

    private void ReportStall(TaskId taskId, DurableTaskAttemptCancellationReason reason, Exception exception)
    {
        _shared.Logger.LogWarning(exception, "{Id} durable task '{TaskId}' stalled: {Reason}.", GrainId, taskId, reason);
        DurableTaskEvents.EmitExecutionStalled(GrainContext, taskId, reason, exception);
    }


    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        throw new InvalidOperationException($"Journal state '{StateMachineName}' does not write journal entries.");

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
        _pendingStarts.Clear();
        _pendingHandleResponses.Clear();
        foreach (var propagation in _pendingCancellationPropagations.Values)
        {
            // 传播操作所依据的取消尚未提交。
            propagation.Completion?.TrySetCanceled();
        }

        _pendingCancellationPropagations.Clear();
        _pendingCompletions = null;
        _nextExpiry = DateTimeOffset.MinValue;
        _committingStarts = [];
        _committingHandleResponses = [];
        _committingCancellationPropagations = [];
    }

    ValueTask IStateMachine.PrepareWriteAsync(CancellationToken cancellationToken) => default;

    void IStateMachine.WritePendingEntries(JournalStreamWriter writer) => CaptureCommitEffects();

    void IStateMachine.WriteSnapshot(JournalStreamWriter writer) => CaptureCommitEffects();

    void IStateMachine.OnWriteCompleted() => RunCommittedEffects();

    private void CaptureCommitEffects()
    {
        // 效果保持待处理，直到捕获它们的写入完成，所以不写入任何字节的捕获操作
        // 将这些效果留给下一次写入。
        _committingStarts = (_pendingStarts.Count == 0) ? [] : [.. _pendingStarts];
        _committingHandleResponses = (_pendingHandleResponses.Count == 0) ? [] : [.. _pendingHandleResponses];
        _committingCancellationPropagations = (_pendingCancellationPropagations.Count == 0) ? [] : [.. _pendingCancellationPropagations.Keys];
    }

    private void RunCommittedEffects()
    {
        var starts = _committingStarts;
        var handleResponses = _committingHandleResponses;
        var propagations = _committingCancellationPropagations;
        _committingStarts = [];
        _committingHandleResponses = [];
        _committingCancellationPropagations = [];

        foreach (var taskId in starts)
        {
            if (_pendingStarts.Remove(taskId))
            {
                StartRequest(taskId);
            }
        }

        foreach (var (taskId, response) in handleResponses)
        {
            // 对同一任务的响应的后续暂存（由之后的写入提交）保持待处理状态。
            if (_pendingHandleResponses.TryGetValue(taskId, out var pending) && ReferenceEquals(pending, response))
            {
                _pendingHandleResponses.Remove(taskId);
            }
        }

        List<CancellationPropagation>? committedPropagations = null;
        foreach (var id in propagations)
        {
            if (_pendingCancellationPropagations.Remove(id, out var propagation))
            {
                (committedPropagations ??= []).Add(propagation);
            }
        }

        if (committedPropagations is null && _orderedCommittedEffects is null)
        {
            foreach (var entry in handleResponses)
            {
                CompleteTaskHandle(entry.Key, entry.Value);
            }
        }
        else
        {
            // 执行尝试先于本次或之后写入所提交的任何结果观察到其任务取消，例如
            // 因取消而结束的子任务。执行尝试的任务取消会调用其令牌的注册回调，因此它
            // 在本次写入得到确认后运行，而由本次写入完成的句柄会等待该操作。
            if (_orderedCommittedEffects is null)
            {
                _orderedCommittedEffects = [];
                TrackBackgroundTask(RunOrderedCommittedEffectsAsync(), "propagating committed durable task cancellation");
            }

            _orderedCommittedEffects.Add(new(committedPropagations ?? [], handleResponses));
        }

        if (!_stopping)
        {
            // 在最早过期时间到达之前，检查任务是否过期的开销很低。
            if (!(_pendingHandleResponses.Count == 0) || DeleteExpiredTasks())
            {
                QueueBackgroundWrite();
            }
        }
    }

    /// <summary>一次写入所提交的取消传播和句柄完成操作。</summary>
    private readonly record struct CommittedEffects(
        List<CancellationPropagation> Propagations,
        KeyValuePair<TaskId, DurableTaskResponse>[] HandleResponses);

    /// <summary>
    /// 按提交顺序运行等待任务取消的已提交效果：每次写入对
    /// 执行尝试的取消、其句柄完成，然后是它所波及的叶任务的取消（例如延迟）。
    /// </summary>
    /// <remarks>此操作运行期间，各次写入所提交的效果会加入该操作，因此它们的句柄在较早的取消之后完成。</remarks>
    private async Task RunOrderedCommittedEffectsAsync()
    {
        await Task.Yield();
        while (_orderedCommittedEffects is { Count: > 0 } batches)
        {
            _orderedCommittedEffects = [];
            foreach (var (committedPropagations, handleResponses) in batches)
            {
                var failures = new List<Exception>?[committedPropagations.Count];
                for (var index = 0; index < committedPropagations.Count; index++)
                {
                    failures[index] = RequestTaskCancellation(committedPropagations[index]);
                }

                foreach (var entry in handleResponses)
                {
                    CompleteTaskHandle(entry.Key, entry.Value);
                }

                for (var index = 0; index < committedPropagations.Count; index++)
                {
                    TrackBackgroundTask(
                        PropagateToLeavesAsync(committedPropagations[index], failures[index]),
                        "propagating committed durable task cancellation to leaves");
                }
            }
        }

        _orderedCommittedEffects = null;
    }

    /// <summary>请求已提交传播中各执行尝试的任务取消，并返回其注册回调的失败。</summary>
    private static List<Exception>? RequestTaskCancellation(CancellationPropagation propagation)
    {
        List<Exception>? failures = null;
        foreach (var context in propagation.Contexts)
        {
            try
            {
                context.DeliverTaskCancellation(propagation.CancellationReason);
            }
            catch (AggregateException exception)
            {
                // 一个注册回调的失败不会影响其他执行尝试的取消。
                (failures ??= []).Add(exception);
            }
        }

        return failures;
    }

    /// <summary>
    /// 取消已提交的取消传播中的叶节点，例如延迟及任务体尚未启动的子任务，然后
    /// 在包含所有失败信息的情况下完成传播操作。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every failure is reported through the propagation's completion.")]
    private static async Task PropagateToLeavesAsync(CancellationPropagation propagation, List<Exception>? failures)
    {
        try
        {
            await Task.WhenAll(propagation.Handles.Select(
                handle => handle.CancelAsync(propagation.CancellationReason, CancellationToken.None).AsTask()));
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (failures is not null)
        {
            var exception = new AggregateException(failures);
            if (propagation.Completion is not { } completion)
            {
                throw exception;
            }

            completion.TrySetException(exception);
            return;
        }

        propagation.Completion?.TrySetResult();
    }

    /// <summary>启动其祖先的持久化取消能够传播到的子任务。</summary>
    public ValueTask<IScheduledTaskHandle> ScheduleChildAsync(
        TaskId taskId,
        DurableTask durableTask,
        CancellationToken cancellationToken) =>
        ScheduleChildAsync(taskId, durableTask, shielded: false, waitForAcceptance: false, cancellationToken);

    /// <summary>启动或重新附接到子任务，并等待提交该任务的写入。</summary>
    /// <param name="taskId">子任务的标识符。</param>
    /// <param name="durableTask">子任务的定义。</param>
    /// <param name="shielded">
    /// 父执行实例是否在启动子任务时启用取消屏蔽。即使祖先已请求取消，这样的子任务仍会创建并运行，
    /// 并记录其祖先的取消不会传播到该子任务。
    /// </param>
    /// <param name="waitForAcceptance">
    /// 是否在子任务宿主接受并持久化存储子任务后才完成启动：对其他 Grain 的调用
    /// 等待目标接受该调用，被拒绝的调用使启动操作失败。
    /// </param>
    /// <param name="cancellationToken">用于放弃启动操作的令牌。</param>
    public async ValueTask<IScheduledTaskHandle> ScheduleChildAsync(
        TaskId taskId,
        DurableTask durableTask,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        if (durableTask is IDurableTaskSubscription subscription)
        {
            var (subscriptionHandle, requiresWrite) = StageSubscription(taskId, subscription, shielded);
            if (requiresWrite)
            {
                await WriteStateAsync(cancellationToken);
            }

            return subscriptionHandle;
        }

        ThrowIfCustomScheduled(durableTask);
        var staged = StageChild(taskId, durableTask, shielded);
        if (staged.RequiresWrite)
        {
            await WriteStateAsync(cancellationToken);
        }

        if (waitForAcceptance)
        {
            await WaitForAcceptanceAsync(taskId, cancellationToken);
        }

        return staged.Handle;
    }

    /// <summary>暂存在激活实例上启动子任务的操作，不执行写入，并返回其句柄。</summary>
    /// <remarks>
    /// 启动操作随下一次日志写入提交，并与调用工作项所做的状态变更一起提交。
    /// </remarks>
    /// <exception cref="NotSupportedException">由自定义宿主调度该定义。</exception>
    public IScheduledTaskHandle PostChild(TaskId taskId, DurableTask durableTask, bool shielded)
    {
        ThrowIfStopping();
        if (durableTask is IDurableTaskSubscription subscription)
        {
            return StageSubscription(taskId, subscription, shielded).Handle;
        }

        ThrowIfCustomScheduled(durableTask);
        return StageChild(taskId, durableTask, shielded).Handle;
    }

    /// <summary>
    /// 若子任务定义由此运行时以外的宿主调度，则抛出异常：一个执行实例的子任务包括其
    /// Grain 的本地子任务、对其他 Grain 的调用及订阅，这些操作均由此运行时记录和重放。
    /// </summary>
    private static void ThrowIfCustomScheduled(DurableTask durableTask)
    {
        if (durableTask is ISchedulableTask and not IDurableTaskRequest)
        {
            throw new NotSupportedException(
                $"A durable execution runs its grain's local children, durable grain calls, and subscriptions. '{durableTask.GetType()}' is scheduled by another host.");
        }
    }

    /// <summary>暂存启动本地子任务或调用其他 Grain 的操作，不执行写入。</summary>
    /// <returns>子任务句柄，以及启动时是否暂存了需要通过写入提交的变更。</returns>
    private StagedChild StageChild(TaskId taskId, DurableTask durableTask, bool shielded)
    {
        if (_shared.Logger.IsEnabled(LogLevel.Trace))
        {
            _shared.Logger.LogTrace("{Id} evaluating task {TaskId}", GrainId, taskId);
        }

        var stateExisted = _storage.TryGetTask(taskId, out var state);
        if (stateExisted)
        {
            // 对已记录的结果解码一次，使重放接收的句柄保存该结果。
            ReadRecordedResult(taskId, state!);
        }

        IScheduledTaskHandle? handle = null;
        IScheduledTaskHandle? existingScheduledHandle = null;
        if (stateExisted && TryGetScheduledTaskHandle(taskId, out var existingHandle))
        {
            existingScheduledHandle = existingHandle;
            if (existingHandle is not TaskHandle localHandle || localHandle.IsRunning)
            {
                handle = existingHandle;
            }
        }

        var remoteRequest = durableTask as IDurableTaskRequest;
        if (remoteRequest is not null)
        {
            var target = remoteRequest.Context?.TargetId
                ?? throw new InvalidOperationException("The durable task request has no target.");
            var method = IDurableTaskRequest.GetMethod(remoteRequest);
            if (state is not null)
            {
                if (state.HasRequest
                    || state.RequestFingerprint is not null
                    || !state.CallerId.IsDefault
                    || state.DueTime is not null
                    || (existingScheduledHandle is TaskHandle existingTaskHandle
                        && existingTaskHandle.RemoteTarget.IsDefault)
                    || (!state.RemoteTarget.IsDefault && state.RemoteTarget != target)
                    || (state.Method is { } recordedMethod
                        && !string.Equals(recordedMethod, method, StringComparison.Ordinal))
                    || (handle is not null
                        && (state.RemoteTarget.IsDefault || state.Method is null)))
                {
                    throw CreateDivergenceException(
                        $"Durable child task '{taskId}' is already associated with a different request.",
                        state,
                        DescribeCall(target, remoteRequest));
                }

                if (state.RemoteTarget.IsDefault || state.Method is null)
                {
                    _storage.SetRemoteRequest(taskId, state, target, method, IDurableTaskRequest.GetFingerprint(remoteRequest, _shared.Serializer));
                }
            }
        }
        else if (state is not null && state.Kind != GetLocalKind(durableTask))
        {
            throw CreateDivergenceException(
                $"Durable child task '{taskId}' is already associated with a different request.",
                state,
                GetLocalKind(durableTask) == DurableTaskRecordKind.Delay ? "a delay" : "a local child task");
        }

        // 取消请求会使叶节点进入终态，例如延迟或任务体尚未启动的子任务。远程
        // 子任务的被调用方决定其结果，而已经启动的任务体会在下方使用已取消的持久化令牌重放。
        if (state is { CancellationRequestedAt: not null, Result: not { IsCompleted: true } }
            && IsCancellationLeaf(taskId, state))
        {
            var canceled = StageResponse(taskId, DurableTaskResponse.Canceled);
            _pendingHandleResponses[taskId] = canceled;
            return new(new CompletedTaskHandle(taskId, canceled), RequiresWrite: true);
        }

        if (handle is CompletedTaskHandle)
        {
            return new(handle, RequiresWrite: false);
        }

        if (handle is TaskHandle cachedHandle
            && cachedHandle.ResponseTask.IsCompletedSuccessfully
            && cachedHandle.ResponseTask.Result.IsCompleted)
        {
            return new(handle, RequiresWrite: false);
        }

        // 不会创建已取消祖先的子任务，除非父任务以屏蔽保护方式启动它。如果取消请求发出时子任务已经存在，
        // 则该子任务自身记录取消请求。
        if (state is not { CancellationRequestedAt: not null } && IsStartRefusedByAncestorCancellation(taskId, state, shielded))
        {
            return new(new CompletedTaskHandle(taskId, DurableTaskResponse.Canceled), RequiresWrite: false);
        }

        if (handle is not null)
        {
            return new(handle, RequiresWrite: false);
        }

        if (remoteRequest is not null)
        {
            return StageRemoteChild(taskId, remoteRequest, shielded);
        }

        // 否则，该任务必须是本地方法调用。其状态会被暂存并随下一次写入提交，
        // 例如主体的第一个持久化操作或其结果所触发的写入，因此同步步骤只需一次写入。步骤的
        // 标识与输入取决于其父任务的输入及先前结果，所以未找到记录的重放
        // 再次运行该步骤。延迟是一个本地子任务，其任务体以该子任务自身的标识符记录延迟。
        state ??= GetOrCreateTask(taskId, GetLocalKind(durableTask), request: null);
        MarkShielded(taskId, state, shielded);

        var executionContext = CreateExecutionContext(taskId);
        handle = GetOrCreateRunningTaskHandle(taskId);
        StartInvocation(taskId, static task => task, durableTask, executionContext);
        return new(handle, RequiresWrite: false);
    }

    /// <summary>已暂存启动操作的句柄，以及该启动操作是否暂存了需要写入提交的变更。</summary>
    private readonly record struct StagedChild(IScheduledTaskHandle Handle, bool RequiresWrite);

    // DurableTask.Delay 和 DurableTask.DelayUntil 创建的定义，其主体记录延迟时使用
    // 对应子任务自身的标识符。
    private static readonly Type DelayType = DurableTask.Delay(TimeSpan.Zero).GetType();
    private static readonly Type DelayUntilType = DurableTask.DelayUntil(DateTimeOffset.MinValue).GetType();

    /// <summary>返回运行 <paramref name="durableTask"/> 的本地子任务的记录类型。</summary>
    private static DurableTaskRecordKind GetLocalKind(DurableTask durableTask) =>
        durableTask.GetType() is var type && (type == DelayType || type == DelayUntilType)
            ? DurableTaskRecordKind.Delay
            : DurableTaskRecordKind.LocalStep;

    private StagedChild StageRemoteChild(TaskId taskId, IDurableTaskRequest request, bool shielded)
    {
        var response = StageRemote(taskId, request, shielded);
        if (response.IsCompleted)
        {
            return new(new CompletedTaskHandle(taskId, response), RequiresWrite: false);
        }

        return new(GetOrCreateRunningTaskHandle(taskId, IDurableTaskRequest.GetContext(request).TargetId), RequiresWrite: true);
    }

    /// <summary>返回任务的某个祖先的持久化取消是否传播到任务。</summary>
    /// <param name="taskId">任务。</param>
    /// <param name="state">任务的状态（若存在）。</param>
    /// <param name="shielded">任务的父任务是否以屏蔽保护方式启动它。</param>
    /// <remarks>
    /// 取消通过任务的祖先传播到任务，最远到达最近的受屏蔽保护的祖先：该祖先自身的
    /// 祖先的取消不会传播到它，因此也不会传播到其后代任务。
    /// </remarks>
    private bool IsAncestorCancellationRequested(TaskId taskId, IDurableTaskState? state, bool shielded)
    {
        if (shielded || state is { Shielded: true })
        {
            return false;
        }

        for (var ancestorId = taskId.Parent(); !ancestorId.IsDefault; ancestorId = ancestorId.Parent())
        {
            if (_storage.TryGetTask(ancestorId, out var ancestorState))
            {
                if (ancestorState.CancellationRequestedAt.HasValue)
                {
                    return true;
                }

                if (ancestorState.Shielded)
                {
                    return false;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 返回任务的某个祖先的持久化取消是否会拒绝任务启动，且此检查发生在取消所波及的每个正在运行的
    /// 祖先观察到取消之后。
    /// </summary>
    /// <param name="taskId">要启动的任务。</param>
    /// <param name="state">任务的状态（若存在）。</param>
    /// <param name="shielded">任务的父任务是否以屏蔽保护方式启动它。</param>
    /// <remarks>
    /// 取消请求到达时会被暂存，提交该请求的写入完成后才传播到正在运行的执行实例，
    /// 在此期间启动工作的主体会发现该工作被拒绝，从而先观察到自己的取消：
    /// 被拒绝的启动操作抛出异常，此时 <see cref="DurableTaskContext.IsTaskCancellationRequested"/> 报告
    /// 取消，而主体执行其取消处理，例如受屏蔽保护的补偿操作。在写入之后执行的传播操作
    /// 会发现已经请求了取消。
    /// </remarks>
    private bool IsStartRefusedByAncestorCancellation(TaskId taskId, IDurableTaskState? state, bool shielded)
    {
        if (!IsAncestorCancellationRequested(taskId, state, shielded))
        {
            return false;
        }

        for (var ancestorId = taskId.Parent(); !ancestorId.IsDefault; ancestorId = ancestorId.Parent())
        {
            if (!_storage.TryGetTask(ancestorId, out var ancestorState))
            {
                continue;
            }

            if (ancestorState.CancellationRequestedAt.HasValue && TryGetExecutionContext(ancestorId, out var context))
            {
                try
                {
                    context.DeliverTaskCancellation(ancestorState.CancellationReason);
                }
                catch (AggregateException exception)
                {
                    _shared.Logger.LogWarning(exception, "{Id} error canceling durable task '{TaskId}'.", GrainId, ancestorId);
                }
            }

            if (ancestorState.Shielded)
            {
                break;
            }
        }

        return true;
    }

    /// <summary>记录父执行实例以取消屏蔽方式启动的子任务不受祖先取消传播的影响。</summary>
    private void MarkShielded(TaskId taskId, IDurableTaskState state, bool shielded)
    {
        if (shielded && !state.Shielded)
        {
            _storage.Shield(taskId, state);
        }
    }

    private void StartInvocation<TState>(
        TaskId taskId,
        Func<TState, DurableTask> createTask,
        TState state,
        GrainDurableTaskContext context)
    {
        Debug.Assert(!_runningRequests.ContainsKey(taskId), "Callers start a task's attempt only while none runs.");

        // 主体在执行尝试的第一个代码段中启动，该代码段是激活实例以 Grain 调用的方式准入的请求。
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = Invoke(createTask, state, context, start.Task);
        _runningRequests.Add(taskId, new RunningRequest(invocation, context));
        DurableTaskEvents.EmitExecutionStarted(GrainContext, taskId);
        context.Start(start);
    }

    private async Task Invoke<TState>(
        Func<TState, DurableTask> createTask,
        TState state,
        GrainDurableTaskContext context,
        Task start)
    {
        await start;
        DurableTaskResponse? response;
        try
        {
            response = await DurableTaskRuntimeHelper.RunAsync(createTask(state), context);
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException && !IsUnregistered(context))
            {
                _shared.Logger.LogWarning(exception, "{Id} error invoking durable task '{DurableTask}'.", GrainId, createTask);
            }

            response = DurableTaskResponse.FromException(exception);
        }

        context.VerifyCancellationPointReached();

        // 在本次执行尝试仍运行时到达的取消（例如延迟的日志写入正在
        // 进行时），仅取消执行尝试的令牌。之后，执行尝试使任务进入终态，因此它返回的待处理操作
        // （例如延迟）不会在取消后继续存在。
        if (!response.IsCompleted
            && _storage.TryGetTask(context.Id, out var taskState)
            && taskState.CancellationRequestedAt.HasValue)
        {
            response = DurableTaskResponse.Canceled;
        }

        try
        {
            // 已取消执行尝试的响应被丢弃：其任务将在下一激活实例中继续，或已经具有结果。
            if (response.IsCompleted && !context.IsAttemptEnded)
            {
                Debug.Assert(!_stopping && !IsUnregistered(context), "The runtime cancels every attempt which it stops or discards.");
                await SetResponseAsync(context.Id, response, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _shared.Logger.LogWarning(exception, "{Id} error committing the response of durable task '{TaskId}'.", GrainId, context.Id);

            // 准备失败会保留已暂存的响应，因此之后的写入会提交该响应，并完成句柄。
            if (exception is JournaledStatePreCommitException && !_stopping)
            {
                QueueBackgroundWrite();
            }
        }
        finally
        {
            // 抽象层会结束重放偏离其记录的执行尝试（例如已记录的结果属于另一
            // 类型），而无需运行时介入；该尝试之后像操作发生分歧的尝试一样停滞。
            if (context.AttemptEndReason is DurableTaskAttemptCancellationReason.Diverged or DurableTaskAttemptCancellationReason.UnsupportedVersion
                && _stalledExecutions.Add(context.Id))
            {
                ReportStall(context.Id, context.AttemptEndReason.GetValueOrDefault(), response.Exception?.InnerException ?? response.Exception ?? new DurableTaskDivergenceException($"Durable task '{context.Id}' diverged from its record."));
            }

            context.OnAttemptCompleted();
            if (_runningRequests.TryGetValue(context.Id, out var running)
                && ReferenceEquals(running.Context, context))
            {
                _runningRequests.Remove(context.Id);
                _executionContexts.Remove(context.Id);
                DurableTaskEvents.EmitExecutionStopped(GrainContext, context.Id);
            }
        }
    }

    private sealed record RunningRequest(
        Task Task,
        GrainDurableTaskContext Context);

    /// <summary>
    /// 返回执行尝试的任务是否已随其执行树删除；删除操作会取消注册该执行尝试的上下文，
    /// 因此执行尝试的结果被丢弃。
    /// </summary>
    private bool IsUnregistered(GrainDurableTaskContext context) =>
        !_executionContexts.TryGetValue(context.Id, out var registered) || !ReferenceEquals(registered, context);

    /// <remarks>
    /// Journaling 在下一次写入中提交所有已暂存的变更，无论由哪个操作请求写入，因此只有当主体的状态
    /// 变更及其结果在同一工作项中暂存时，两者才会一起提交。执行尝试在主体运行返回响应后
    /// 提交暂存的响应。
    /// </remarks>
    void IDurableTaskGrainRuntime.StageBodyResponse(GrainDurableTaskContext context, DurableTaskResponse response)
    {
        Debug.Assert(!context.IsAttemptEnded && !_stopping && !IsUnregistered(context), "Only a running attempt stages its response.");
        try
        {
            // 当写入提交响应时完成句柄，与执行尝试提交响应的情况相同。
            _pendingHandleResponses[context.Id] = StageResponse(context.Id, response);
        }
        catch (Exception exception)
        {
            // 此操作在主体的延续中运行，没有调用方观察其失败。执行尝试在主体运行返回响应后
            // 再次暂存响应。
            _shared.Logger.LogWarning(exception, "{Id} error staging the response of durable task '{TaskId}'.", GrainId, context.Id);
        }
    }

    /// <summary>
    /// 已提交写入所传播的取消：携带 <see cref="CancellationReason"/> 的任务取消，针对
    /// 正在运行的执行尝试，以及对延迟等叶任务的取消。若提供了 <see cref="Completion"/>，
    /// 则在传播操作运行完成后完成它。
    /// </summary>
    private sealed record CancellationPropagation(
        List<GrainDurableTaskContext> Contexts,
        List<IScheduledTaskHandle> Handles,
        string? CancellationReason = null,
        TaskCompletionSource? Completion = null);

    private async Task SetResponseAsync(
        TaskId taskId,
        DurableTaskResponse response,
        CancellationToken cancellationToken)
    {
        if (_stopping)
        {
            return;
        }

        await CommitResponseAsync(taskId, response, cancellationToken);
    }

    /// <summary>
    /// 暂存终态响应及其完成通知，提交这些内容，并完成本地句柄。
    /// </summary>
    /// <returns>第一个终态响应，优先于之后的响应。</returns>
    private async ValueTask<DurableTaskResponse> CommitResponseAsync(
        TaskId taskId,
        DurableTaskResponse response,
        CancellationToken cancellationToken)
    {
        var stored = _storage.TryGetTask(taskId, out var state) && state.Result is { IsCompleted: true };
        var winningResponse = StageResponse(taskId, response);
        if (!stored)
        {
            // 当一次写入提交响应时完成句柄：可能是本次写入，也可能是在本次写入提交前失败时的后续
            // 写入，因为准备失败会保留暂存的变更。
            _pendingHandleResponses[taskId] = winningResponse;
        }
        else if (!_pendingHandleResponses.ContainsKey(taskId))
        {
            // 若已存储的结果没有待处理的句柄完成操作，则表明结果已经提交：携带该结果的写入（例如
            // 提交主体最后一段代码暂存的结果的写入）已完成句柄。待处理的句柄完成操作
            // 随捕获它的写入完成，因此条目保持为该写入捕获时的状态。
            CompleteTaskHandle(taskId, winningResponse);
        }

        await WriteStateAsync(cancellationToken);
        if (DeleteExpiredTasks())
        {
            await WriteStateAsync(cancellationToken);
        }

        return winningResponse;
    }

    /// <summary>
    /// 暂存终态响应及其完成通知，除非已暂存终态响应。
    /// </summary>
    /// <returns>第一个终态响应，优先于之后的响应。</returns>
    private DurableTaskResponse StageResponse(TaskId taskId, DurableTaskResponse response)
    {
        if (!response.IsCompleted)
        {
            throw new InvalidOperationException("Only terminal durable task responses can complete a task.");
        }

        if (_shared.Logger.IsEnabled(LogLevel.Trace))
        {
            _shared.Logger.LogTrace("{Id} task {TaskId} completed with result '{Result}'.", GrainId, taskId, response);
        }

        if (!_storage.TryGetTask(taskId, out var state))
        {
            throw new InvalidOperationException($"Cannot complete unknown task '{taskId}'.");
        }

        // 切勿覆盖终态结果：已暂存的结果会提交，修改它可能改变已经观察到的结果。
        if (state.Result is { IsCompleted: true } terminal)
        {
            return terminal;
        }

        // 发件箱根据已存储的结果读取每个完成通知，因此首先暂存结果。本 Grain 上的订阅者
        // 在接收结果时退役，所以先复制目的地。
        response = StoreResult(taskId, state, response);
        foreach (var destination in state.CompletionDestinations.ToArray())
        {
            SendCompletion(taskId, destination);
        }

        return response;
    }

    /// <summary>
    /// 存储任务的终态结果。若没有完成通知目的地，则结果的保留期从现在开始。
    /// </summary>
    /// <param name="subscribed">
    /// 订阅子任务的结果是否为被订阅任务的结果。订阅子任务如果
    /// 以其他方式结束（例如取消或中止），则结束其订阅。
    /// </param>
    /// <returns>已存储的结果：<paramref name="response"/>，或者替代无法序列化的结果的失败。</returns>
    private DurableTaskResponse StoreResult(TaskId taskId, IDurableTaskState state, DurableTaskResponse response, bool subscribed = false)
    {
        response = SetResponse(taskId, state, response);
        if (!subscribed && TryGetSubscription(state, out var target, out var subscribedTaskId))
        {
            Unsubscribe(target, subscribedTaskId, taskId);
        }

        if (state.CompletionDestinations.Count == 0)
        {
            TrackExpiry(GetRetentionExpiry(state));
        }

        return response;
    }

    private void QueueBackgroundWrite()
    {
        TrackBackgroundTask(
            PersistBackgroundChangesAsync(),
            "persisting deferred durable task cleanup");
    }

    /// <summary>跟踪激活实例上的后台工作（停用时会排空这些工作），并记录其失败。</summary>
    private void TrackBackgroundTask(Task task, string operation)
    {
        var id = ++_backgroundWriteId;
        var observed = ObserveBackgroundTaskAsync(id, task, operation);
        if (!observed.IsCompleted)
        {
            _backgroundTasks[id] = observed;
        }
    }

    private async Task ObserveBackgroundTaskAsync(long id, Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            _shared.Logger.LogWarning(
                exception,
                "{Id} error {Operation}.",
                GrainId,
                operation);
        }
        finally
        {
            _backgroundTasks.Remove(id);
        }
    }

    private async Task PersistBackgroundChangesAsync()
    {
        await Task.Yield();
        await WriteStateAsync(CancellationToken.None);
    }


    /// <summary>将挂起的等待由 <paramref name="awaited"/> 解除的执行尝试标记为可运行。</summary>
    private void NotifyParkedAttempts(TaskId awaited)
    {
        foreach (var context in _executionContexts.Values)
        {
            context.OnAwaitedCompleted(awaited);
        }
    }

    private void CompleteTaskHandle(TaskId taskId, DurableTaskResponse response)
    {
        NotifyParkedAttempts(taskId);
        if (_taskHandles.TryGetValue(taskId, out var handle) && handle is TaskHandle localHandle)
        {
            localHandle.TrySetResponse(response);
        }

        CompleteSignalWaits(taskId);
    }

    /// <summary>
    /// 删除保留期已结束的执行树，以及已无法再匹配调用的取消记录；
    /// 在最早过期时间到达后执行此操作。
    /// </summary>
    /// <returns><see langword="true"/> 表示已暂存删除操作。</returns>
    /// <remarks>
    /// 在最早过期时间经过之前，检查不产生开销；随后的扫描重新计算该时间。具有
    /// 待处理完成通知的任务没有过期时间：完成通知的退役会删除任务或启动任务的保留期。
    /// </remarks>
    private bool DeleteExpiredTasks()
    {
        var now = UtcNow;
        if (now < _nextExpiry)
        {
            return false;
        }

        var nextExpiry = DateTimeOffset.MaxValue;
        List<(TaskId Id, IDurableTaskState State)>? expired = null;
        foreach (var (taskId, state) in _storage.Tasks)
        {
            if (state.CompletionDestinations.Count > 0)
            {
                continue;
            }

            var isCancellationRecord = IsCancellationRecord(state);
            if ((!state.CompletedAt.HasValue && !isCancellationRecord)
                || (!IsInvoked(state) && HasStoredAncestor(taskId)))
            {
                // 待处理任务没有过期时间；属于另一任务执行树的任务则随该任务删除。
                continue;
            }

            var expiry = state.CompletedAt.HasValue ? GetRetentionExpiry(state) : DateTimeOffset.MinValue;
            if (isCancellationRecord)
            {
                // 取消先于其到达的调用仅在该记录的保留期内被准入，否则永远不会准入。
                var recordExpiry = AddSaturating(state.CreatedAt, _shared.EarlyRecordRetention);
                expiry = recordExpiry > expiry ? recordExpiry : expiry;
            }
            else if (IsSignalRecord(state))
            {
                // 不含对应任务的信号记录先于调用到达，该调用仅在记录保留期内被准入，
                // 否则永远不会准入。
                expiry = GetEarlySignalExpiry(state);
            }

            if (expiry <= now)
            {
                if (IsSignalRecord(state))
                {
                    _shared.Logger.LogWarning(
                        "{Id} discarding a signal for durable task '{TaskId}', whose invocation wasn't admitted within the invocation admission window.",
                        GrainId,
                        taskId.Parent());
                }

                (expired ??= []).Add((taskId, state));
            }
            else if (expiry < nextExpiry)
            {
                nextExpiry = expiry;
            }
        }

        _nextExpiry = nextExpiry;
        if (expired is null)
        {
            return false;
        }

        // 已过期的记录可能位于另一棵已过期的执行树中，删除该执行树时已将该记录移除。
        foreach (var (taskId, state) in expired)
        {
            if (_storage.TryGetTask(taskId, out _))
            {
                DeleteTree(taskId, state);
            }
        }

        return true;
    }

    /// <summary>返回终态结果的保留期结束时间。</summary>
    /// <remarks>保留期在任务完成时开始，或者在之后某次转为死信的完成通知重新启动保留期时开始。</remarks>
    private DateTimeOffset GetRetentionExpiry(IDurableTaskState state)
    {
        var completedAt = state.CompletedAt!.Value;
        var retainedSince = state.RetainedSince is { } restarted && restarted > completedAt ? restarted : completedAt;
        return AddSaturating(retainedSince, _shared.Options.ResultRetentionPeriod);
    }

    private static DateTimeOffset AddSaturating(DateTimeOffset time, TimeSpan duration) =>
        duration >= DateTimeOffset.MaxValue - time ? DateTimeOffset.MaxValue : time + duration;

    private void TrackExpiry(DateTimeOffset expiry)
    {
        if (expiry < _nextExpiry)
        {
            _nextExpiry = expiry;
        }
    }

    /// <summary>返回任务是否作为请求被调度，因此拥有自己的执行树。</summary>
    /// <remarks>
    /// 完成决策、分离任务的记录、信号记录、信号发送标记及效果记录的
    /// 指纹以 <c>$</c> 开头，但它们属于父任务的执行树；请求指纹是
    /// 十六进制值。
    /// </remarks>
    private static bool IsInvoked(IDurableTaskState state) =>
        state.RequestFingerprint is { } fingerprint
        && !fingerprint.StartsWith('$');

    /// <summary>返回任务是否仅记录了先于其调用到达的取消。</summary>
    private static bool IsCancellationRecord(IDurableTaskState state) =>
        state.Kind == DurableTaskRecordKind.EarlyCancellation;

    private bool HasStoredAncestor(TaskId taskId)
    {
        for (var ancestorId = taskId.Parent(); !ancestorId.IsDefault; ancestorId = ancestorId.Parent())
        {
            if (_storage.TryGetTask(ancestorId, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 删除拥有自身生命周期的任务及其执行树，即该任务的所有本身并非被调用
    /// 任务的后代。
    /// </summary>
    /// <remarks>
    /// 开销与执行树大小成正比。执行树中的每个非终态任务都会被取消：待处理远程
    /// 子任务的取消与删除操作一同暂存，正在运行的执行尝试则被取消且其结果被丢弃。
    /// </remarks>
    private void DeleteTree(TaskId taskId, IDurableTaskState state)
    {
        Debug.Assert(state.CompletionDestinations.Count == 0, "A pending completion retains its task's result.");
        DeleteTreeCore(taskId, state);
    }

    private void DeleteTreeCore(TaskId taskId, IDurableTaskState state)
    {
        foreach (var (childId, child) in _storage.GetChildren(taskId).ToList())
        {
            if (!IsInvoked(child))
            {
                DeleteTreeCore(childId, child);
            }
        }

        if (_shared.Logger.IsEnabled(LogLevel.Trace))
        {
            _shared.Logger.LogTrace("{Id} deleting task {TaskId}", GrainId, taskId);
        }

        var terminal = state.CompletedAt.HasValue;
        if (!terminal && !state.RemoteTarget.IsDefault)
        {
            // 已记录的取消请求已经暂存其消息，该消息在退役前归发件箱所有；再次发送相同的
            // 标识将成为具有不同准入截止时间的新发送操作。
            if (!state.CancellationRequestedAt.HasValue)
            {
                StageCancellation(taskId, state.RemoteTarget, state.CancellationReason);
            }
        }
        else if (!terminal && TryGetSubscription(state, out var target, out var subscribedTaskId))
        {
            Unsubscribe(target, subscribedTaskId, taskId);
        }

        if (_executionContexts.Remove(taskId, out var context))
        {
            CancelAttempt(context, DurableTaskAttemptCancellationReason.TaskDeleted);
        }

        if (_runningRequests.Remove(taskId, out var running))
        {
            // 停用等待已取消的执行尝试排空。
            TrackBackgroundTask(running.Context.Settled, "running a deleted durable task");
            DurableTaskEvents.EmitExecutionStopped(GrainContext, taskId);
        }

        if (_taskHandles.Remove(taskId, out var handle))
        {
            if (handle is TaskHandle { ResponseTask.IsCompleted: false } localHandle)
            {
                localHandle.TrySetResponse(terminal ? state.Result! : DurableTaskResponse.Canceled);
                NotifyParkedAttempts(taskId);
            }
        }

        _pendingStarts.Remove(taskId);
        _pendingHandleResponses.Remove(taskId);
        _logicalTimes.Remove(taskId);
        RemoveTask(taskId);
        SignalAcceptance(taskId);
    }

    /// <inheritdoc/>
    public async ValueTask<DurableTaskResponse> SubscribeOrPollAsync(TaskId taskId, SubscribeOrPollOptions options, CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        if (_shared.Logger.IsEnabled(LogLevel.Trace))
        {
            _shared.Logger.LogTrace("{Id} received polling request for task {TaskId}", GrainId, taskId);
        }

        // 保留期在截止时间结束；即使 Grain 处于空闲状态，截止时间仍可能到来。
        if (DeleteExpiredTasks())
        {
            await WriteStateAsync(cancellationToken);
        }

        if (!_storage.TryGetTask(taskId, out var state))
        {
            throw new DurableTaskNotFoundException(taskId);
        }

        var handle = GetScheduledTaskHandle(taskId);
        var response = await handle.PollAsync(new PollingOptions { PollTimeout = options.PollTimeout }, cancellationToken);
        if (response.IsCompleted)
        {
            return response;
        }

        return DurableTaskResponse.Pending;
    }

    async IAsyncEnumerable<(TaskId TaskId, DurableTaskDiagnosticState State)> IDurableTaskGrainExtension.GetTasksAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        await Task.CompletedTask;

        foreach (var (taskId, taskState) in _storage.Tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = GetDiagnosticState(taskId, taskState);

            yield return (taskId, state);
        }

        static DurableTaskDiagnosticState GetDiagnosticState(TaskId taskId, IDurableTaskState taskState)
        {
            var (status, response) = DescribeResult(taskState);
            return new DurableTaskDiagnosticState
            {
                CompletedAt = taskState.CompletedAt,
                CreatedAt = taskState.CreatedAt,
                DueTime = taskState.DueTime,
                RemoteTarget = taskState.RemoteTarget.IsDefault ? null : taskState.RemoteTarget.ToString(),
                Response = response,
                Request = DescribeRequest(taskState),
                Status = status,
                Waiters = [.. taskState.CompletionDestinations.Select(destination => DescribeSubscriber(taskId, destination))],
            };
        }

        // 诊断会解码已存储的有效载荷，所以无法再解码的有效载荷会被报告，而不是抛出异常。
        // 正在取消或已取消的任务报告其取消原因。
        static (string Status, string? Response) DescribeResult(IDurableTaskState taskState)
        {
            if (!HasStoredResult(taskState))
            {
                return taskState switch
                {
                    { CancellationRequestedAt: not null } => ("Canceling", taskState.CancellationReason),
                    { DueTime: not null } => ("Delayed", null),
                    _ => ("Pending", null),
                };
            }

            try
            {
                var result = taskState.Result!;
                return result.Status switch
                {
                    DurableTaskStatus.CompletedSuccessfully => ("Completed", result.Result?.ToString()),
                    DurableTaskStatus.Canceled => ("Canceled", taskState.CancellationReason),
                    _ => ("Faulted", DescribeException(result.Exception)),
                };
            }
            catch (Exception exception)
            {
                return ("Unreadable", DescribeException(exception));
            }
        }

        static string? DescribeRequest(IDurableTaskState taskState)
        {
            if (TryGetSubscription(taskState, out var target, out var subscribedTaskId))
            {
                return DescribeSubscription(target, subscribedTaskId);
            }

            try
            {
                return taskState.Request?.ToMethodCallString();
            }
            catch (Exception exception)
            {
                return $"Unreadable request: {DescribeException(exception)}";
            }
        }

        static string? DescribeException(Exception? exception) =>
            exception is null ? null : $"{exception.GetType().Name}: {exception.Message}";
    }

    async IAsyncEnumerable<TaskId> IDurableTaskGrainExtension.GetRunningTasksAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        await Task.CompletedTask;
        var running = _runningRequests.Keys.ToList();

        foreach (var taskId in running)
        {
            yield return taskId;
        }
    }

    public ValueTask SignalCancellationAsync(TaskId taskId, CancellationToken cancellationToken) =>
        SignalCancellationAsync(taskId, callerId: default, reason: null, cancellationToken);

    /// <summary>
    /// 暂存通过收件箱收到的取消，并在收件箱批次提交后传播取消。
    /// </summary>
    internal void SignalCancellationFromInbox(TaskId taskId, GrainId callerId, string? reason)
    {
        var propagation = StageCancellationTree(taskId, callerId, reason);
        if (propagation.Contexts.Count > 0 || propagation.Handles.Count > 0)
        {
            _pendingCancellationPropagations[++_cancellationPropagationId] =
                new(propagation.Contexts, propagation.Handles, CancellationReason: reason);
        }
    }

    private async ValueTask SignalCancellationAsync(
        TaskId taskId,
        GrainId callerId,
        string? reason,
        CancellationToken cancellationToken)
    {
        // 取消在传播前已持久化。
        var propagation = StageCancellationTree(taskId, callerId, reason);
        if (!propagation.Changed)
        {
            return;
        }

        var propagated = StagePropagation(propagation.Contexts, propagation.Handles, reason);
        await WriteStateAsync(cancellationToken);
        await propagated.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 暂存取消向其波及的执行尝试和叶任务的传播，该传播由
    /// 提交取消的写入运行，并返回传播完成操作。
    /// </summary>
    private Task StagePropagation(List<GrainDurableTaskContext> contexts, List<IScheduledTaskHandle> handles, string? reason)
    {
        if (contexts.Count == 0 && handles.Count == 0)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCancellationPropagations[++_cancellationPropagationId] =
            new(contexts, handles, CancellationReason: reason, Completion: completion);
        return completion.Task;
    }

    /// <remarks>
    /// 请求新波及的每个任务都会记录 <paramref name="reason"/>，因此请求收集的每个执行实例和句柄
    /// 均在携带该原因的情况下被取消。
    /// </remarks>
    private (bool Changed, List<GrainDurableTaskContext> Contexts, List<IScheduledTaskHandle> Handles) StageCancellationTree(
        TaskId taskId,
        GrainId callerId,
        string? reason)
    {
        ThrowIfStopping();
        if (taskId.IsDefault)
        {
            throw new ArgumentException("Invalid TaskId.", nameof(taskId));
        }

        if (!_storage.TryGetTask(taskId, out var taskState))
        {
            // 只有通过收件箱投递的、调用方针对自身子任务的取消，才可能先于该子任务的
            // 调用到达：记录会在调用到达时取消它。直接请求指定本
            // Grain 保留的任务，因此针对其他任务的请求不会暂存任何内容。
            if (callerId.IsDefault)
            {
                throw new DurableTaskNotFoundException(taskId);
            }

            taskState = GetOrCreateTask(taskId, DurableTaskRecordKind.EarlyCancellation, request: null);
            StageCaller(taskId, taskState, callerId);
            _storage.RequestCancellation(taskId, taskState, reason);
            TrackExpiry(AddSaturating(taskState.CreatedAt, _shared.EarlyRecordRetention));
            return (true, [], []);
        }

        ValidateCaller(taskId, taskState, callerId);
        StageCaller(taskId, taskState, callerId);
        List<GrainDurableTaskContext> canceledContexts = [];
        List<IScheduledTaskHandle> canceledHandles = [];
        var changed = RequestCancellationCore(taskId, taskState, canceledContexts, canceledHandles);
        return (changed, canceledContexts, canceledHandles);

        bool RequestCancellationCore(
            TaskId currentTaskId,
            IDurableTaskState currentTaskState,
            List<GrainDurableTaskContext> contexts,
            List<IScheduledTaskHandle> handles)
        {
            if (currentTaskState.CompletedAt.HasValue)
            {
                return false;
            }

            if (currentTaskState.CancellationRequestedAt.HasValue)
            {
                return false;
            }

            foreach (var (childTaskId, childTaskState) in _storage.GetChildren(currentTaskId).ToList())
            {
                // 命名组合器的输入是组合器 ID 下距离本任务最近的已存储后代任务。
                Debug.Assert(currentTaskId.IsAncestorOf(childTaskId));

                // 祖先的取消不会传播到受屏蔽保护的子任务；
                // 任务自身的取消、执行树的中止及执行树的删除仍会作用于该任务。
                if (!childTaskState.Shielded)
                {
                    _ = RequestCancellationCore(childTaskId, childTaskState, contexts, handles);
                }
            }

            _storage.RequestCancellation(currentTaskId, currentTaskState, reason);
            if (TryGetExecutionContext(currentTaskId, out var context))
            {
                if (currentTaskState.Request is IDurableTaskRequest request
                    && request.Context is { } requestContext
                    && requestContext.TargetId != GrainId)
                {
                    // 记录的请求涵盖执行实例自身对远程调用的取消；参见 CancelRemoteAsync。
                    StageCancellation(currentTaskId, requestContext.TargetId, reason);
                }

                contexts.Add(context);
            }
            else if (!currentTaskState.RemoteTarget.IsDefault)
            {
                StageCancellation(currentTaskId, currentTaskState.RemoteTarget, reason);
            }
            else if (TryGetScheduledTaskHandle(currentTaskId, out var handle))
            {
                handles.Add(handle);
            }

            return true;
        }
    }

    ValueTask IDurableTaskServer.CancelAsync(TaskId taskId, CancellationToken cancellationToken) =>
        SignalCancellationAsync(taskId, callerId: default, reason: null, cancellationToken);

    ValueTask IDurableTaskServer.CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken) =>
        SignalCancellationAsync(taskId, callerId: default, reason, cancellationToken);

    ValueTask IDurableTaskServer.AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken) =>
        AbortTaskAsync(taskId, reason, cancellationToken);

    ValueTask IDurableTaskManager.AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken) =>
        AbortTaskAsync(taskId, reason, cancellationToken);

    /// <summary>
    /// 中止任务及其执行树：将树中的每个未完成任务以
    /// <see cref="DurableTaskAbortedException"/> 失败的状态提交的写入，也会暂存针对每个待处理的其他 Grain 调用的中止；
    /// 执行树中每个正在运行的执行尝试均被取消，且其结果被丢弃。
    /// </summary>
    /// <remarks>终态任务或本 Grain 未保留的任务保持不变。</remarks>
    internal async ValueTask AbortTaskAsync(TaskId taskId, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (StageAbort(taskId, reason, callerId: default))
        {
            await WriteStateAsync(cancellationToken);
        }
    }

    /// <summary>暂存通过收件箱收到的中止，并取消被中止的执行尝试。</summary>
    /// <remarks>
    /// 中止与取消一样可能先于其调用到达；此时将其记录为取消，因此调用
    /// 到达时会进入终态。调用方已经使其任务记录进入终态。
    /// </remarks>
    internal void AbortFromInbox(TaskId taskId, string reason, GrainId callerId)
    {
        if (!_storage.TryGetTask(taskId, out _))
        {
            SignalCancellationFromInbox(taskId, callerId, reason: null);
            return;
        }

        StageAbort(taskId, reason, callerId);
    }

    /// <returns><see langword="true"/> 表示中止操作暂存了变更。</returns>
    private bool StageAbort(TaskId taskId, string reason, GrainId callerId)
    {
        ThrowIfStopping();
        if (!_storage.TryGetTask(taskId, out var state))
        {
            return false;
        }

        ValidateCaller(taskId, state, callerId);
        if (HasStoredResult(state))
        {
            return false;
        }

        // 执行树中的每个任务均记录相同的失败，因此一个响应即可用于所有这些任务。
        var response = DurableTaskResponse.FromException(new DurableTaskAbortedException(reason));
        AbortTreeCore(taskId, state, reason, response);
        return true;
    }

    private void AbortTreeCore(
        TaskId taskId,
        IDurableTaskState state,
        string reason,
        DurableTaskResponse response)
    {
        foreach (var (childId, child) in _storage.GetChildren(taskId).ToList())
        {
            if (!IsInvoked(child))
            {
                AbortTreeCore(childId, child, reason, response);
            }
        }

        if (HasStoredResult(state))
        {
            return;
        }

        if (!state.RemoteTarget.IsDefault)
        {
            MessageTransport.SendAbort(GrainId, state.RemoteTarget, GetWireId(taskId), reason);
        }

        // 取消注册执行尝试会丢弃其结果；停用等待它排空。
        if (_executionContexts.Remove(taskId, out var context))
        {
            CancelAttempt(context, DurableTaskAttemptCancellationReason.TaskAborted);
        }

        if (_runningRequests.Remove(taskId, out var running))
        {
            TrackBackgroundTask(running.Context.Settled, "running an aborted durable task");
            DurableTaskEvents.EmitExecutionStopped(GrainContext, taskId);
        }

        _pendingStarts.Remove(taskId);
        _pendingHandleResponses[taskId] = StageResponse(taskId, response);
    }

    // ScheduledTask 的基类构造函数在抽象层中是 internal 的，因此无类型句柄使用 object 结果。
    ScheduledTask IDurableTaskManager.GetTask(TaskId taskId) => ((IDurableTaskManager)this).GetTask<object?>(taskId);

    // 运行时直接处理句柄的轮询与取消，如同处理 Grain 调用的轮询与取消。
    ScheduledTask<TResult> IDurableTaskManager.GetTask<TResult>(TaskId taskId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        return new AttachedScheduledTask<TResult>(taskId, GrainId, this);
    }

    ValueTask IDurableTaskManager.CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken) =>
        SignalCancellationAsync(taskId, callerId: default, reason, cancellationToken);

    private bool TryGetScheduledTaskHandle(TaskId taskId, [NotNullWhen(true)] out IScheduledTaskHandle? handle)
    {
        if (_taskHandles.TryGetValue(taskId, out var taskHandle))
        {
            handle = taskHandle;
            return true;
        }

        if (!_storage.TryGetTask(taskId, out var taskState))
        {
            handle = null;
            return false;
        }

        // 已提交的结果恢复为已完成的句柄；已暂存的结果则在提交后完成句柄。
        if (!_pendingHandleResponses.ContainsKey(taskId) && taskState.Result is { } response)
        {
            Debug.Assert(response.IsCompleted);
            handle = new CompletedTaskHandle(taskId, response);
            return true;
        }

        handle = GetOrAddTaskHandle(taskId, taskState.RemoteTarget);
        return true;
    }

    public IScheduledTaskHandle GetScheduledTaskHandle(TaskId taskId)
    {
        ThrowIfStopping();
        if (!TryGetScheduledTaskHandle(taskId, out var handle))
        {
            throw new KeyNotFoundException($"A task with the identifier '{taskId}' was not found.");
        }

        return handle;
    }

    /// <remarks>
    /// 订阅或分离启动的记录指定其观察的任务以及该任务所在的 Grain，其他参与方
    /// 可以寻址它。对某个 Grain 的调用通过调用的传输标识符访问目标在本 Grain 的作用域下存储的任务，
    /// 目标保留该任务直到本 Grain 接受其完成通知。其他任何操作，或者不记录任何内容的操作
    /// （例如先前取消所阻止的启动操作），均使用自身的标识符。
    /// </remarks>
    public TaskId GetTargetTaskId(TaskId taskId, out GrainId addressedHost)
    {
        addressedHost = default;
        if (!_storage.TryGetTask(taskId, out var state))
        {
            return taskId;
        }

        if (TryGetSubscription(state, out var target, out var observedId))
        {
            addressedHost = target;
            return observedId;
        }

        return state.RemoteTarget.IsDefault ? taskId : CallerScope.GetTaskId(GrainId, GetWireId(taskId));
    }

    /// <summary>
    /// 返回 <paramref name="nodeId"/> 的传输标识符，该节点是本 Grain 某棵执行树中已存储的节点，或
    /// 其执行实例即将记录的节点：与其他 Grain 之间的消息使用该标识符指定它。
    /// </summary>
    /// <remarks>
    /// 标识符包含节点所属执行树的运行世代标识，因此根标识符的每次运行发送各自的消息。
    /// 参见 <see cref="CallerScope"/>。
    /// </remarks>
    internal TaskId GetWireId(TaskId nodeId) => CallerScope.GetWireId(GetIncarnation(nodeId), nodeId);

    /// <summary>
    /// 返回 <paramref name="wireId"/> 所指定的节点；若该节点已存储且属于
    /// <paramref name="wireId"/> 所指定的执行树运行，则还返回其状态。
    /// </summary>
    /// <returns>
    /// 当传输标识符指定的运行已被删除时返回 <see langword="false"/>：节点不存在于存储中，或后续
    /// 的执行树运行已经存储该节点。
    /// </returns>
    /// <exception cref="InvalidOperationException"><paramref name="wireId"/> 不是传输标识符。</exception>
    private bool TryGetNode(TaskId wireId, out TaskId nodeId, [NotNullWhen(true)] out IDurableTaskState? state)
    {
        if (!CallerScope.TryParseWireId(wireId, out var incarnation, out nodeId))
        {
            throw new InvalidOperationException(
                $"Task ID '{wireId}' doesn't name a node of an execution tree of grain '{GrainId}'.");
        }

        if (_storage.TryGetTask(nodeId, out state) && GetIncarnation(nodeId) == incarnation)
        {
            return true;
        }

        state = null;
        return false;
    }

    /// <remarks>
    /// 每个执行实例都属于某个被调用任务的执行树，该任务在其调用首次
    /// 存储时记录其运行世代标识，因此此遍历始终在一个已存储的被调用任务处结束。
    /// </remarks>
    public Guid GetIncarnation(TaskId executionId)
    {
        for (var taskId = executionId; !taskId.IsDefault; taskId = taskId.Parent())
        {
            if (_storage.TryGetTask(taskId, out var state) && IsInvoked(state))
            {
                Debug.Assert(state.Incarnation != Guid.Empty, "An invoked task records its incarnation with its invocation.");
                return state.Incarnation;
            }
        }

        throw new InvalidOperationException($"Durable task '{executionId}' belongs to no invoked task of grain '{GrainId}'.");
    }

    public DateTimeOffset? GetCompletedAt(TaskId taskId) =>
        _storage.TryGetTask(taskId, out var state) ? state.CompletedAt : null;

    public void OnReplayEnded(TaskId executionId) => DurableTaskEvents.EmitReplayEnded(GrainContext, executionId);

    /// <remarks>
    /// 执行实例已记录的操作是其最近的已存储后代节点：自身也是执行实例的后代节点，
    /// 例如本地步骤，保存各自的操作。信号值属于被调用任务，发送方投递信号时
    /// 不依赖其执行尝试；被调用的后代任务保留自己的生命周期；先于请求到达的取消
    /// 不记录本执行实例的任何操作；版本决策记录执行实例采用的代码，而不是它
    /// 到达的操作，因此移除版本检查的部署不会留下一个
    /// 其重放永远无法到达的操作；取消点记录执行实例观察到
    /// 取消的位置，重放通过计数其操作到达该点。
    /// </remarks>
    public HashSet<TaskId>? GetRecordedOperations(TaskId executionId)
    {
        HashSet<TaskId>? operations = null;
        foreach (var (id, state) in _storage.GetChildren(executionId))
        {
            if (!IsInvoked(state)
                && !IsSignalRecord(state)
                && !IsCancellationRecord(state)
                && !IsVersionRecord(state)
                && !IsCancellationPointRecord(state))
            {
                (operations ??= []).Add(id);
            }
        }

        return operations;
    }

    internal Task StopAsync(CancellationToken _) => _stopTask ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        // 运行时停止时会取消所有执行尝试，因此激活实例停止后没有执行尝试会记录任何内容。
        _stopping = true;
        foreach (var context in _executionContexts.Values)
        {
            CancelAttempt(context, DurableTaskAttemptCancellationReason.ActivationStopping);
        }

        await _deactivationCts.CancelAsync();

        // 待处理任务在下一激活实例继续执行，所以本激活实例的句柄的等待者会失败，如同
        // 运行时停止期间到达的调用一样，而不会将停止观察为任务的结果。
        var stopping = new DurableTaskAttemptCanceledException(DurableTaskAttemptCancellationReason.ActivationStopping, _deactivationCts.Token);
        foreach (var handle in _taskHandles.Values)
        {
            handle.SetStopping(stopping);
        }

        while (true)
        {
            // 已挂起的执行尝试被丢弃，永远不会恢复，因此立即结束；正在运行代码段的执行尝试则在
            // 代码段结束时完成。放弃排空会导致替换激活实例与本激活实例重叠运行。
            // 适配器控制的等待会观察生命周期令牌。任意用户代码可以忽略该令牌，因此停用
            // 有意等待每个正在运行的代码段结束，然后替换激活实例的重放才可开始。
            var running = _runningRequests.Values
                .Select(static request => request.Context.Settled)
                .Where(static settled => !settled.IsCompleted)
                .Concat(_backgroundTasks.Values)
                .ToArray();
            if (running.Length == 0)
            {
                return;
            }

            await Task.WhenAll(running).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private bool IsRequestRunning(TaskId taskId) => _runningRequests.ContainsKey(taskId);

    private static InvalidOperationException CreateUnboundException() => new(
        "Durable Tasks requires grain activations to derive from Orleans.Durable.Journaling.DurableGrain so the runtime binds to the activation's journal and inbox.");

    private void ThrowIfStopping()
    {
        if (_grainContext is null)
        {
            throw CreateUnboundException();
        }

        if (_stopping)
        {
            throw new DurableTaskAttemptCanceledException(DurableTaskAttemptCancellationReason.ActivationStopping, _deactivationCts.Token);
        }
    }

    /// <summary>
    /// 取消 <paramref name="context"/> 的执行尝试（原因为 <paramref name="reason"/>），并在
    /// 后续工作项中发出执行尝试令牌的取消信号，停用会排空该工作项。
    /// </summary>
    /// <remarks>
    /// 停用、中止及任务执行树的删除均以这种方式在激活实例的调度器上取消执行尝试。
    /// 自取消起，执行尝试的持久化操作会抛出 <see cref="DurableTaskAttemptCanceledException"/>，
    /// 因此运行时不记录任何此类操作，并丢弃执行尝试的结果。其代码对日志化状态所做的变更，
    /// 例如清理操作，随 Grain 的下一次日志写入提交。在运行时当前操作之外，令牌被发出取消信号后，代码
    /// 便通过这些令牌观察到取消。
    /// </remarks>
    private bool CancelAttempt(GrainDurableTaskContext context, DurableTaskAttemptCancellationReason reason)
    {
        if (!context.EndAttempt(reason))
        {
            return false;
        }

        TrackBackgroundTask(ApplyCancellationAsync(context), "canceling a durable task attempt");
        return true;

        static async Task ApplyCancellationAsync(GrainDurableTaskContext context)
        {
            await Task.Yield();
            context.ApplyCancellation();
        }
    }
}
