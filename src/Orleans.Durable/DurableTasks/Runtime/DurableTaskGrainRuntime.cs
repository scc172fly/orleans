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
/// 执行一个 grain 激活的耐久任务。
/// </summary>
/// <remarks>
/// <para>
/// 任务状态、 发送信件和逻辑时间共享 grain日记( Q0Q] ) 。 每个操作都验证并准备
/// 第一阶段,然后是阶段性,只有安全可以执行的改变。
/// 承诺下一份成功日记写; 存储失败的刻录栅栏会给日记设置围栏, 并停止 grain。
/// </para>
/// <para>
/// 运行时间也是日记状态机器, 不写入条目。 抓取标记取决于效果
/// 开始承诺请求,完成处理
/// 承诺的响应,宣传承诺的取消。
/// </para>
/// <para>
/// 延迟阶段 向自 grain 发送回信的延迟状态及其与
/// <see cref="Orleans.Durable.Messaging.Envelope.DeliverAt"/> 延迟的到期时间。 grain的收件箱接收此消息
/// 在同一日记中与延迟一样写入,因此只要延误是可持久的,且信箱处理
/// 适应器排期无法自订可持久的工作:它用收件箱登记了它的提醒延迟,而
/// 情报部门报告说,它可执行的处决是可持久的,因此,通信的警醒使 grain重新启动。
/// </para>
/// <para>
/// 运行时间是其完成信件的 <see cref="IOutboxMessageSource"/> :发件箱日记
/// 每次完成, 运行时间将任务存储结果中的讯息化为运行时间, 因此结果会被日记
/// 任务完成目的地是其用户:
/// 和持久处决儿童,他们根据自己的
/// 识别符号。 此 grain 的订阅者收到存储该标记的写作结果。
/// 完成的纸上写着一份结果副本
/// 保留期的任务自有任务,因此民意调查和重新恢复活动仍能观察。
/// 更长的解码仅能完成读取它的业务:发箱用破信后完成,其退休
/// 启动从不解码存储结果
/// </para>
/// <para>
/// 援引的任务,原计划作为请求提出,它拥有一棵处决树:任务及其后代,即
/// 诸如当地儿童、拖延、完成决定、独立任务记录等任务本身,
/// 任务识别器由调用员选择,所以
/// 被援引的后代有自己的生命周期。
/// 一旦援引的任务为终端任务,且没有完成未完成,运行时间将删除其执行树:在书中
/// ,因为目的地长期保持其结果,或以其他方式
/// [质 完成或自完成<see cref="Orleans.Configuration.DurableTaskOptions.ResultRetentionPeriod"/>
/// 完成日期为死字母,没有储存的祖先的任务,例如,在耐久时间之外安排的远程儿童
/// 取消树的不终止任务: 它在
/// 取消每个待处理的远程儿童,并取消每次尝试,结果被丢弃。
/// 完成和恢复的补全和复件,如果到达删除任务或任务树早期运行时,将被忽略:每运行
/// 根任务 ID 本身有其自成的化名,即指定其节点覆盖位置的电文识别符号的标识符,作为
/// <see cref="CallerScope"/> 描述。在它之前发出的取消或信号
/// 祈祷记录下来,祈祷在树上找到 grain 允许一次祈祷,直到
/// <see cref="Orleans.Durable.Messaging.Configuration.DurableMessagingOptions.DeduplicationWindow"/> 自其发件人以来已过期
/// 开始先进行,后再写一个死字母。在以下删除了那些无法调用的记录:
/// <see cref="DurableTaskGrainRuntimeShared.EarlyRecordRetention"/>,它覆盖发件人时钟和发件人时钟之间
/// 排 grain's.
/// 订阅一项 grain 无法保留的任务,因为所援引的任务完成后即刻找不到。
/// </para>
/// <para>
/// 取消任务是合作性的:请求记录在任务状态,取消
/// 尝试的<see cref="DurableTaskContext.CancellationToken"/> ,仅终止假期,如延误
/// 任务,任务,任务,而其身体还没有开始。一个刚开始的机构决定其任务的结果,恢复则用它来重新显示
/// 请求应用。 中止会执行任务执行树, 在一个写入中失败, 并取消树的运行
/// 尝试,结果被抛弃。
/// </para>
/// <para>
/// 尝试取消取消时停止尝试: 中止、 中止和删除树取消尝试。 A 已取消
/// 连续作业 <see cref="DurableTaskAttemptCanceledException"/>,它日记的状态变化,
/// 例如清理, 与 grain下一份日记写入承诺, 其标记将在以后的工作项目中取消。 a
/// 中止或删除的尝试排水管:其停靠的等待随着取消而恢复。
/// 停止丢弃的拖放从不恢复, 停止作用只等待其运行段在下一次激活前
/// 重播它的任务。
/// </para>
/// <para>
/// 每次尝试都按段进行, 本地请求启动; 见 <see cref="GrainDurableTaskContext"/>。
/// 运行时状态仅限于激活的调度器, 与 grain 状态相同, 所以它不使用锁。
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
    /// <summary>运行时的运行时间的“ 挂勾” 日记状态机器名称。</summary>
    internal const string StateMachineName = "orleans.durable-tasks.runtime";

    // 运行时间状态仅限于启动调度器,如 grain 状态。
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

    // 处决的企图因运行代码无法重播而停止, 并且等待执行。 grain
    // 它可以唤醒每个 DurableTaskOptions.StalledExecutionRetryInterval ,而它却能保持一个,所以以后的激活就会重弹。
    private readonly HashSet<TaskId> _stalledExecutions = [];
    private TaskId[] _committingStarts = [];
    private KeyValuePair<TaskId, DurableTaskResponse>[] _committingHandleResponses = [];
    private long[] _committingCancellationPropagations = [];

    // 等待任务取消的强制效果, 以及先前或同一人所写的尝试;
    // 执行命令时,无一等待,或无一等待时无效;见 RunCommittedEffects。
    private List<CommittedEffects>? _orderedCommittedEffects;

    // 被恢复的处决 发现并开始 一旦启动完成。
    private List<TaskId>? _recoveredStarts;
    private bool _recovered;
    private Task? _stopTask;
    private long _backgroundWriteId;
    private long _cancellationPropagationId;
    private IGrainContext? _grainContext;
    private bool _stopping;

    // 发件箱中待补全的完成,通过电文编号。发件箱解决了在运行前回收完结
    // 查找恢复值,因此索引是从任务状态中建起的,首次使用时,然后随着完成发送而保持
    // 退休。
    private Dictionary<MessageId, PendingCompletion>? _pendingCompletions;

    // 保留的结果或注销记录可能过期的最早时间。
    // 所以第一个检查会扫描每个任务; 之后每次更改开始保留期或记录
    // 取消后将其降低, 并且每次扫描都从剩余任务中重新计算它 。
    private DateTimeOffset _nextExpiry = DateTimeOffset.MinValue;

    public GrainId GrainId => GrainContext.GrainId;

    /// <summary>获得运行时间将注定要启动的激活。</summary>
    /// <remarks>
    /// 绑定是明确的, 所以从激活的调度器上跳出代码, 环境 grain 上下文是
    /// 仍然读取激活程序的身份
    /// </remarks>
    internal IGrainContext GrainContext => _grainContext ?? throw CreateUnboundException();

    public DateTimeOffset UtcNow => _shared.TimeProvider.GetUtcNow();

    private IDurableTaskMessageTransport MessageTransport => _messageTransport ?? throw new InvalidOperationException(
        "Durable messaging is not configured. Call AddDurableTasks on the silo builder.");

    /// <summary>将运行时间绑绑到 <paramref name="grainContext"/>被它执行的死刑</summary>
    internal void InitializeForActivation(IGrainContext grainContext) => _grainContext = grainContext;

    /// <summary>
    /// 恢复启动期间已承诺的任务状态,将被追回的死刑推迟到
    /// <see cref="StartRecoveredExecutions"/>.
    /// </summary>
    internal Task RecoverAsync(CancellationToken cancellationToken)
    {
        _recovered = true;
        return ResumePendingTasksCoreAsync(cancellationToken);
    }

    /// <summary>开始处决 <see cref="RecoverAsync"/> 恢复一次</summary>
    /// <remarks>
    /// 之后运行 grain's <c>OnActivateAsync</c> 已完成, 因而已收回 grain 代码对初始化的值
    /// grain,就像 grain 呼叫一样。一个到达回收任务的第一个呼叫开始,而它跳过它。
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
    /// 返回先完成的候选者:已储存且完成程序最低的候选人,需要
    /// 没有记录,或有赢家在 <paramref name="decisionId"/>下记录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存储邮票每次产生时,其顺序在逐步形成结果后,其顺序顺序上,以后的结果得到更高
    /// 序列。结果被评分的候选人以与每个结果相同的写法或比预写的早期写法承诺
    /// 程序更高级,所以当候选人的处理程序完成后,每个低顺序的候选人都被储存起来。
    /// 因此,在一次现场尝试中储存的最低序列相同。 现场尝试选择了迄今为止取得的结果,
    /// 和每个重播, 其中更多的候选人可以完成。
    /// </para>
    /// <para>
    /// 候选人可以完成没有序列的完整: 一个被取消的祖先的孩子, 但没有存储, 或者一个结果被储存
    /// 在顺序标记之前先加盖标记。 当首先观测到完成时间, 或者
    /// 已记录的决定已经存在, 选择记录在 <paramref name="decisionId"/> 下, 重放
    /// 记录。
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

            // 在一个没有储存的、顺序结果没有耐久命令的情况下完成的把手。
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
    /// 返回存储候选人的索引,该候选人的完成顺序最低,或 -1,以及是否是候选人
    /// 存储结果没有序列 。
    /// </summary>
    /// <remarks>只读存储的完成元数据, 而不解码结果。</remarks>
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
    /// 返回根据 <paramref name="decisionId"/>记录的记录的赢家,或按候选人顺序记录第一个候选人
    /// 其手柄已经完全完成。
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
            // 当几个候选人完成考试后,第一个候选人按顺序获胜。
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
    /// 等到候选人的处理程序完成, 停车 <paramref name="attempt"/> 同时, 然后取消和排水
    /// 等待其他的。
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
            // 等待宣传失败, 如民调失败一样。
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

        // 选择接收预定输入。 祖先的取消拒绝启动, 而不储存它们 。
        return IsAncestorCancellationRequested(taskId, state: null, shielded: false)
            ? new CompletedTaskHandle(taskId, DurableTaskResponse.Canceled)
            : GetScheduledTaskHandle(taskId);
    }

    // 请求指纹是十六进制杂草, 所以它从来没有这个前缀 。
    private const string CompletionDecisionFingerprintPrefix = "$completion-decision:";

    /// <summary>
    /// 创建错误,用于在一个记录另一个操作的标识符下调度操作,即
    /// 重放与记录的执行表面不同。 尝试的上下文会拖慢尝试 。
    /// </summary>
    private static DurableTaskDivergenceException CreateDivergenceException(string message, IDurableTaskState recorded, string requested) =>
        new(DescribeDivergence(message, recorded, requested));

    /// <summary>
    /// 返回先前在 <paramref name="taskId"/>下记录的尝试的结果,在第一次访问时解码。
    /// </summary>
    /// <exception cref="DurableTaskDivergenceException">记录的结果不再编码。</exception>
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

    /// <summary>创建错误,用于在记录另一个操作的任务标识符下安排请求。</summary>
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

    /// <summary>描述诊断请求:其方法及其论据的指纹。</summary>
    private string DescribeRequest(IDurableTaskRequest request) =>
        $"a request for {DescribeMethod(IDurableTaskRequest.GetMethod(request))} ({AbbreviateFingerprint(IDurableTaskRequest.GetFingerprint(request, _shared.Serializer))})";

    /// <summary>描述呼叫另一个 grain 诊断:其目标、方法及其论据的指纹。</summary>
    private string DescribeCall(GrainId target, IDurableTaskRequest request) =>
        DescribeCall(target, IDurableTaskRequest.GetMethod(request), IDurableTaskRequest.GetFingerprint(request, _shared.Serializer));

    private static string DescribeCall(GrainId target, string? method, string? fingerprint) =>
        $"a call to {DescribeMethod(method)} on grain '{target}' ({AbbreviateFingerprint(fingerprint)})";

    private static string DescribeMethod(string? method) => method is null ? "an unrecorded method" : $"method '{method}'";

    // WhenAny,种族,超时和信号最后期限 记录他们的选择都一样, 所以描述没有 名字他们。
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
    /// 创建新的执行环境,在当地执行背景汇编中登记。
    /// </summary>
    /// <param name="taskId">任务id。</param>
    /// <returns>新执行环境。</returns>
    /// <remarks>
    /// 请求取消任务后开始或重播的尝试在此收到请求,
    /// 其身体在取消点观察过它:当先尝试时,处决记录到的
    /// 或从第一个运行开始, 或者重放结束的点。 从那时起, 它
    /// <see cref="DurableTaskContext.CancellationToken"/> 被取消了,
    /// <see cref="DurableTaskContext.IsTaskCancellationRequested"/> 已经设置,并且
    /// <see cref="DurableTaskContext.TaskCancellationReason"/> 报告记录的理由。
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
    /// 如果存在,则获取与所提供的任务相应的执行环境,然后返回。
    /// </summary>
    /// <param name="taskId">从中获取执行上下文的任务。</param>
    /// <param name="executionContext">执行上下文。</param>
    /// <returns><see langword="true"/> 如果发现执行上下文, <see langword="false"/> 。</returns>
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

        // 存储记录从另一个 grain调用的每一次调用电话的调用者,因此检查解码无需请求。
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

    /// <summary>将终端任务的结果发送给其一个用户。</summary>
    /// <remarks>
    /// 另一个 grain 的用户通过发件箱接收完成信息,运行时间从存储的
    /// 输入此 grain 用户的人将接收
    /// 直接产生,在同一写法中,同时退休。
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

            // 每一个完成的任务的目的地都有一个完成的目标,等待发件箱中的发件箱:完成阶段与
            // 输出箱退出时,目标被删除。此 grain 的目的地
            // 记录在存储结果的书写中。
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
    /// 运行时间提供其待职待聘职位表所持的职前工作,以及完成
    /// 工作地点的目的地。它也从未退出:在表格中保留待发的电话,以及尚未完成的
    /// 目标保持任务和结果,直到 <see cref="IOutboxMessageSource.OnRetired"/> 消除任务和任务
    /// 保存结果传播的解码例外,所以发件箱
    /// 完成时用字条写
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
    /// 记录在开始或取消退休完成的已退休职业
    /// 目标, 一旦结果交付给每个目的地, 删除任务, 并用其执行树 。
    /// </summary>
    /// <remarks>
    /// 发件箱将信件的删除从同一日记写中进行, 因此更改会随着退休而发生 。
    /// 退休仅更新任务元数据, 所以当存储的结果不再解码时它就会成功; 投票结果
    /// 导致向授粉器解码错误的表面。
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
    /// 删除持有或无法再接收终端任务结果的目的地, 并用它
    /// 一旦没有目的地,即行处决树。
    /// </summary>
    /// <remarks>
    /// 交付目的地长期持久地持有结果,因此任务不需要保留。 grain
    /// 电话人,例如客户计划或持久执行分离的任务,在其后保留其留任期的结果。
    /// 完成,所以民意测验观察它。一个字母的完成重新开始结果保留期, 所以投票员,
    /// 或再次援引该任务的呼叫者仍然可以遵守;当任务结束时,任务将删除。
    /// </remarks>
    private void RetireDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination, bool deadLettered)
    {
        Debug.Assert(IsInvoked(state), "Only invoked tasks have completion destinations.");
        _storage.RemoveCompletionDestination(taskId, state, destination);
        if (deadLettered)
        {
            // 保留从现在开始: 死信可以经过很长的回溯期,结果必须保持
            // 完全保留期内,授粉者或重新恢复活动可使用。
            _storage.RetainResult(taskId, state);
        }

        if (state.CompletionDestinations.Count > 0)
        {
            return;
        }

        // 保留因完成日期为无字母、超过交货而重新开始。
        if (state.RetainedSince.HasValue || state.CallerId.IsDefault)
        {
            TrackExpiry(GetRetentionExpiry(state));
        }
        else
        {
            DeleteTree(taskId, state);
        }
    }

    /// <summary>发信箱的完成未完成。</summary>
    private readonly record struct PendingCompletion(TaskId TaskId, DurableTaskSubscriber Destination);

    private void StageCancellation(TaskId taskId, GrainId target, string? reason) =>
        MessageTransport.SendCancellation(GrainId, target, GetWireId(taskId), reason);

    private ValueTask WriteStateAsync(CancellationToken cancellationToken) => _storage.WriteAsync(cancellationToken);

    /// <summary>返回任务状态, 运行不存在的任务状态。</summary>
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

    /// <summary>阶段任务终端结果,并返回其所生成的结果。</summary>
    /// <remarks>
    /// 存储在结果发生任何阶段前, 将结果序列化, 当结果没有连续化时丢弃, 例如 a
    /// 数值或没有编码器的类型的例外。任务然后与 <see cref="SerializerException"/>发生故障
    /// 指定类型,如果该类型与任何结果一样,则该类型将产生类似结果,任务结束而不是持有不写入的结果
    /// 能够承诺。
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

    /// <summary>阶段性地对执行过程进行新的终端记录,例如效果结果或版本决定。</summary>
    private void AddRecord(TaskId taskId, DurableTaskRecordKind kind, string fingerprint, DurableTaskResponse response)
    {
        _storage.AddRecord(taskId, kind, fingerprint, response);
        DurableTaskEvents.EmitTaskCreated(GrainContext, taskId);
        DurableTaskEvents.EmitTaskCompleted(GrainContext, taskId, response);
    }

    /// <summary>任务删除阶段。</summary>
    private void RemoveTask(TaskId taskId)
    {
        if (_storage.RemoveTask(taskId))
        {
            DurableTaskEvents.EmitTaskDeleted(GrainContext, taskId);
        }
    }


    /// <summary>
    /// 援引 <paramref name="request"/> 在一个 grain 儿童作为边远儿童 <paramref name="taskId"/>,
    /// 返回儿童记录的绝症反应,或 <see cref="DurableTaskResponse.Pending"/>。
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

        // 记录接受情况表明,该职业退休,目标承担这项任务。
        if (state.Accepted is null)
        {
            var wireId = GetWireId(taskId);
            SendInvocation(transport.CreateInvocation(GrainId, context.TargetId, wireId, request), request, recordId: wireId);
        }

        return DurableTaskResponse.Pending;
    }

    // 执行树上的操作指纹以$开头,所以从不等于十六进制的指纹。
    private const string EffectFingerprint = "$effect";

    private static bool IsEffectRecord(IDurableTaskState state) => state.Kind == DurableTaskRecordKind.Effect;

    /// <summary>
    /// 返回 <paramref name="effectId"/> 记录或运行 <paramref name="effect"/> 和阶段a的结果
    /// 其结果记录,未书面写成。
    /// </summary>
    /// <remarks>
    /// 启动时的调用器运行着调用器, 因此在启动时, 记录和状态的变化 效果的改变会以
    /// 工作项目相同,并按要求在下个日记写中进行,无论操作请求如何。记录是一个终端
    /// 执行执行的树的条目,与树一起删除。结果按任务结果序列排列,然后
    /// 并不序列化的结果被记录并返回作为失败, 所以每运行都观察到同样的失败。
    /// 另一种记录 在效果识别符号下, 或者一个命名的组合器在它下面的投入, 是差异。
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

        // 命名的组合器记录着它输入的内容, 在其识别符号下。
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

    // 版本记录是其执行的最后记录,如效果记录一样,其结果就是版本。
    private const string VersionFingerprint = "$version";

    /// <summary>执行下版本决定记录段的前缀。</summary>
    internal const string VersionSegmentPrefix = "$version:";

    /// <remarks>
    /// 搜查从行刑经过处决, 包括一步之母等,
    /// 树的自有性, 被召人或被传传后代的决定 继续保留自己。
    /// 执行树的终端条目,以调用工作项目为台阶,与树一起删除。
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

    /// <summary>记录中保留了某一执行部分观察其取消的注销点的部分。</summary>
    internal const string CancellationPointSegment = "$cancellation-observed";

    private const string CancellationPointFingerprint = "$cancellation-observed";

    /// <remarks>早先的运送尝试记录了这一点,后来的尝试读了这一点。</remarks>
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
    /// 记录是行刑树的终点入口 以调用工作项目为舞台
    /// grain的下个日记写入, 并用树木删除。 在写前开始的重放即已执行
    /// 将取消作为无记录执行。
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

        // 取消及其信息承诺在一起;发件箱只有在发出后才发送该信息,而且
        // 发送者在登机截止时重新进行重试。 记录的请求, 如重播重复的请求,
        // 因此,没有分阶段进行任何步骤:发件箱退出后再发送身份,这将是一个新的发送方式,该发送方式
        // 处理第一个拒绝的接收器 因为它的不同截断点。
        if (!state.CancellationRequestedAt.HasValue)
        {
            _storage.RequestCancellation(taskId, state, reason);
            StageCancellation(taskId, target, reason);
        }

        await WriteStateAsync(cancellationToken);
    }

    /// <summary>要求通过处理取消本地任务。</summary>
    /// <remarks>
    /// 取消叶, 如延迟或任务, 其身体还没有开始, 结束如在写作中取消
    /// 开始的机构通过持久标记来观察请求,并决定其结果。
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

    /// <summary>返回任务正文是否已经开始, 并记录执行的逻辑时间。</summary>
    private bool HasStarted(TaskId taskId) => _logicalTimes.ContainsKey(taskId);

    /// <summary>
    /// 返回请求的取消是否在不运行一个决定它的机构的情况下将任务取消而结束
    /// 结果。
    /// </summary>
    /// <remarks>
    /// 儿童的身体还没有开始的拖延和任务都是休假。
    /// 开始运行身体, 或者重放, 其耐久的标记已取消 。
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
            // 延迟重现仅从延误记录或从被援引任务(其机构为迟延)的记录中重复:
            // 其它操作的结果或状态绝不是延迟的结果。
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
                // 持久拖延是一个承诺:重播保留记录的适当时间,即使现在工作流程
                // 请求另一个, 例如在配置的长度更改后 。
                if (existingDueTime != dueTime)
                {
                    _shared.Logger.LogWarning(
                        "{Id} durable delay '{TaskId}' is due at {RecordedDueTime:O}; its replay requested {RequestedDueTime:O}. The recorded due time is kept.",
                        GrainId,
                        taskId,
                        existingDueTime,
                        dueTime);
                }

                // 重播可以观察到早期尝试的延迟,而其写作尚未完成。
                await WriteStateAsync(cancellationToken);
                return DurableTaskResponse.Pending;
            }
        }

        state ??= GetOrCreateTask(taskId, DurableTaskRecordKind.Delay, request: null);
        var generation = checked(state.ResumeGeneration + 1);
        _storage.SetDelay(taskId, state, dueTime, generation);

        // grain自己的收件箱接受与延迟相同日记的复录,因此每次持久延误都有一个
        // 持久恢复。
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
    /// 将延误的结果分阶段进行,预定的回电到期后即完成处理。
    /// </summary>
    /// <param name="taskId">延迟。</param>
    /// <param name="incarnation">延迟树的化形, 当恢复时间排定时。</param>
    /// <param name="generation">恢复时间排定时延迟的生成。</param>
    /// <param name="sender">下 grain 将恢复的发件人发送到 grain.</param>
    /// <remarks>
    /// 请求取消的延迟被终止为取消。
    /// 完成各代的相组。 延时树的早期运行恢复, 后一个运行会推迟, 与
    /// 身份识别特征无法按其代、 早期一代、 终端延迟或丢失的代、 早期一代、 终端延误、 或
    /// 任务改变不了任何东西。
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
    /// 阶段: <paramref name="sender"/> 中 grain'等待的节点:一个远程
    /// 儿童自己的结果或订阅现有任务的结果 <paramref name="sourceTaskId"/>
    /// 儿童已签名。完成后, 以其线条识别符号 <paramref name="wireId"/>命名节点。
    /// 承诺后完成。
    /// </summary>
    /// <remarks>
    /// 收件箱接受直接完成任务状态, 因此此存储回复是它唯一的日记副本 。
    /// 发送者保留其结果,直到收件箱将接受情况报告给发件人发件人发件人发件人发件人发件人发件人发件人发件人发件人发件人发件人
    /// 不再存在的结点,或属于其树上另一条线的结点,而不是线标识符所指定的线
    /// 接受名称,但不变状态:等待其的跑步与行刑树一起删除,后者
    /// 取消它,没有任何东西等待它的结局。一个订阅已经终止,例如因为
    /// 已取消, 也接受不修改的完成 。
    /// </remarks>
    internal void AcceptResponseFromInbox(TaskId wireId, TaskId sourceTaskId, DurableTaskResponse response, GrainId sender)
    {
        ThrowIfStopping();
        if (!TryGetNode(wireId, out var taskId, out var state))
        {
            return;
        }

        ThrowIfNotStartedBy(taskId, wireId, sourceTaskId, state, sender);

        // 任务的结果表明,它 grain 保留任务, 记录被请求接受的
        // 否则退休记录就会记录在案。
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

    /// <summary>恢复执着的任务状态,并开始执行被追回的处决。</summary>
    internal async Task ResumePendingTasksAsync(CancellationToken cancellationToken)
    {
        await RecoverAsync(cancellationToken);
        StartRecoveredExecutions();
    }

    /// <summary>返回任务是否存储其终端结果,而不解码结果。</summary>
    /// <remarks>
    /// 存储印章 <see cref="IDurableTaskState.CompletedAt"/> ,每个储存的结果都保存在封存处,总是最后。
    /// 激活检查以这种方式完成,因此储存的结果不再编码,不再解码,只能使操作失败
    /// 读过,例如完成或投票。
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

            // 被取消的叶子已经绝迹。一个遥远的孩子等待着被召唤者的结果, 开始身体重现,
            // 或与其父子关系, 其耐久的标志取消 。
            if (state.CancellationRequestedAt.HasValue && IsCancellationLeaf(taskId, state))
            {
                await SetResponseAsync(taskId, DurableTaskResponse.Canceled, cancellationToken);
                continue;
            }

            // 延迟的回信信息会随着延误而持久,收件箱也会在到期时处理。
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
    /// 确实可以安排对此案提出援引请求。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>A <see cref="DurableTaskResponse"/> 表示请求的状况。类型 <see cref="PendingDurableTaskResponse"/> 的答复表示,调用者可以再次将这种方法调用民意测验以完成。</returns>
    /// <exception cref="InvalidOperationException"></exception>
    async ValueTask<DurableTaskResponse> IDurableTaskServer.ScheduleAsync(
        TaskId taskId,
        IDurableTaskRequest request,
        CancellationToken cancellationToken)
    {
        // 留存额在最后期限截止时终止,期限可在 grain 空闲期间通过,本写作承诺
        // 删除请求。
        ThrowIfStopping();
        DeleteExpiredTasks();
        var scheduled = StageSchedule(taskId, request, fromInbox: false);

        // 在处决开始前提交请求身份和电话订阅。
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
    /// 阶段 通过收件箱收到请求, 开始于收件箱批次完成后。
    /// </summary>
    internal DurableTaskResponse ScheduleFromInbox(TaskId taskId, IDurableTaskRequest request) =>
        StageScheduleWithCommitEffects(taskId, request, fromInbox: true);

    /// <summary>
    /// 请求在进行分阶段修改的写作后,开始处理或完成处理阶段。
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

        // 仅在保留调用范围根下存储任务的其他 grains 引用 。
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
            // 完成的单项从不针对其本身 grain: StageRemote 拒绝自寻其口。
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

        // 请求对同一调用人采用相同方法时,与存储的任务相同操作。
        // 检查了ValidateCaller :重试其参数序列序列不同而要达到存储任务。
        // 其执行本身的操作,例如效果,有指纹,但没有任何方法。
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

        // 运行的内化在它的援引首次存储时被刻模,这是早期取消的记录
        // 前面的一段,那段记录变成了引用
        if (state.Kind == DurableTaskRecordKind.EarlyCancellation)
        {
            _storage.SetInvocation(taskId, state);
        }

        if (state.Incarnation == Guid.Empty)
        {
            _storage.SetIncarnation(taskId, state, Guid.NewGuid());
        }

        // 在一个在开始取消之前就被取消的援引中, 其取消被取消作为取消的终止。 一个启动的机构, 重复
        // 请求达到,决定自己的结果。
        if (state.CancellationRequestedAt.HasValue
            && state.Result is not { IsCompleted: true }
            && IsCancellationLeaf(taskId, state))
        {
            // 援引前的取消没有接到任何请求。
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

        // 一项请求,如果试图提出但书写尚未完成,则尚未开始。
        return new StartedRequest(subscribed ? DurableTaskResponse.Subscribed : DurableTaskResponse.Pending);
    }

    /// <summary>调控存储请求,请求在完成承诺的写作后开始。</summary>
    private sealed class StartedRequest(DurableTaskResponse response)
    {
        public DurableTaskResponse Response { get; } = response;
    }

    /// <summary>请求完成了一项已经完成的任务。</summary>
    private sealed class CompletedRequest(DurableTaskResponse response)
    {
        public DurableTaskResponse Response { get; } = response;
    }

    /// <summary>
    /// 请求之前取消一次, 所以在请求前取消, 启动后将任务存储为被取消, 请求处理完成一次
    /// 完成那部经典的经文。
    /// </summary>
    private sealed class CanceledRequest(DurableTaskResponse response)
    {
        public DurableTaskResponse Response { get; } = response;
    }

    /// <summary>启动请求的结果</summary>
    private readonly union ScheduledRequest(StartedRequest, CompletedRequest, CanceledRequest);

    private static DurableTaskResponse GetResponse(ScheduledRequest scheduled) => scheduled switch
    {
        StartedRequest started => started.Response,
        CompletedRequest completed => completed.Response,
        CanceledRequest canceled => canceled.Response,
    };

    /// <summary>
    /// 返回 <paramref name="taskId"/> 完成日期,返回引用该日期的 grain ,其中列出
    /// 索尔的自生子
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
            // 目的地的完成情况尚未在发信箱中公布。
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

        // 在请求开始之前提出取消的请求,没有机构予以遵守。
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
    /// 返回 <paramref name="taskId"/> 存储该任务的请求,或在请求不再存在时进行检疫
    /// 解码
    /// </summary>
    /// <remarks>
    /// 隔离任务仍待处理,其储存的请求和摊位不变:运行时间报告一次
    /// 以 <see cref="DurableTaskAttemptCancellationReason.Diverged"/> 激活并保持 grain的慢警醒,所以
    /// 之后启动,例如其代码解码请求的激活,开始启动。 grain的其他任务不受影响,
    /// 投票结果报告任务尚未完成。中止任务结束任务
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
    /// 报告工作, grain 则执行可撤销的处决,因此收件箱持久地唤醒了 grain,并
    /// 停止执行或坠机后恢复执行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 尝试在其中一个部分运行或排队时是可运行的,而其一个片段运行或排队。
    /// 信息传递:儿童的结果、信号、延迟的恢复或若干完成的第一个。
    /// 重新启动 grain。日记笔迹也使一旦实施尝试即可运行:它开始请求
    /// 接受、完成结果的处理和存储信号,并宣传其记录的注销。
    /// 收件箱在每次写作时,在运行时间之前,在准备这些效果并进行确保
    /// 承诺取消的取消在背景工作中传播,这算
    /// 工作到结束
    /// </para>
    /// <para>
    /// 当启动开始时,即在此运行时间恢复处决之前,通讯通信咨询其工作来源。
    /// 直至恢复持续,等待处决的死刑 储存库中计为可运行的。
    /// 恢复状态,所以投入的工作从不搁浅。
    /// </para>
    /// <para>
    /// 启动的拆除装置在启动时,通信也咨询它们,直到泵停止。
    /// 尝试启动退出时, 并开始尝试停止功能, 因此它们中没有一个不再可以运行, 而一个在随后运行的警醒将会结束, 没有
    /// 将启动停止或实施其等待事件时可执行的处决捆绑起来
    /// 停止运行时间因此报告其待执行的处决也可运作。
    /// 保持清醒又需要一次激活, 恢复后又会重新公园。
    /// </para>
    /// <para>
    /// 延迟, <see cref="Orleans.Configuration.DurableTaskOptions.RunnableExecutionWakeupDelay"/>, 限制
    /// 崩溃的grain 恢复了可运行的尝试。 提醒只在崩溃或停用后才重要: 而
    /// grain 活动, 唤醒自己只重新安排时间, 所以长时间的拖延会维持低廉的投票而不是投票。
    /// </para>
    /// <para>
    /// 死刑的暂停等待代码的重放, 它只是后一启动运行, 所以它保持缓慢
    /// 提醒, <see cref="Orleans.Configuration.DurableTaskOptions.StalledExecutionRetryInterval"/>:一旦激活结束,例如当
    /// 滚动部署停止了 silo,警醒重新启动了 grain ,恢复重播执行。
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

    /// <summary>返回存储是否具有回收运行的操作。</summary>
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

    /// <summary>返回中止执行是否仍然未执行,忘记了终止执行的处决,例如中止执行。</summary>
    private bool HasStalledExecutions()
    {
        _stalledExecutions.RemoveWhere(taskId => !_storage.TryGetTask(taskId, out var state) || HasStoredResult(state));
        return _stalledExecutions.Count > 0;
    }

    /// <summary>
    /// 取消执行的尝试,运行代码无法重放,任务未完成,报告
    /// 缓存。
    /// </summary>
    /// <remarks>
    /// 尝试因停止、中止或删除而取消,所以其后来的持久操作被撤销
    /// 其结果被抛弃,而他没有成果。 grain慢慢的唤醒,等一会儿
    /// 激活重放它。
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
            // 之后的传播活动取消后并没有承诺
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
        // 效果一直等待时间, 直到捕获它们的文件完成, 所以一个捕捉, 没有写字节
        // 留待下次写作时再写
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
            // 同一任务的答复的后一阶段,即以后写作时承诺的答复,仍待落实。
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
            // 试图在它或后来写的任何结果之前观察其任务的取消,例如
            // 儿童在孩子的标记注册上运行了取消结束的日期。尝试任务取消时运行着他们的标记登记,所以它
            // 运行于此写入确认后, 此写完成的控点将等待它 。
            if (_orderedCommittedEffects is null)
            {
                _orderedCommittedEffects = [];
                TrackBackgroundTask(RunOrderedCommittedEffectsAsync(), "propagating committed durable task cancellation");
            }

            _orderedCommittedEffects.Add(new(committedPropagations ?? [], handleResponses));
        }

        if (!_stopping)
        {
            // 检查过期任务在最早到期通行证之前是廉价的。
            if (!(_pendingHandleResponses.Count == 0) || DeleteExpiredTasks())
            {
                QueueBackgroundWrite();
            }
        }
    }

    /// <summary>注销的传播和处理一个写作的完成。</summary>
    private readonly record struct CommittedEffects(
        List<CancellationPropagation> Propagations,
        KeyValuePair<TaskId, DurableTaskResponse>[] HandleResponses);

    /// <summary>
    /// 运行等待任务取消的已承诺效果,按承诺顺序:每写入一次注销
    /// 尝试,然后处理完后,它取消了它所剩的假期,例如拖延。
    /// </summary>
    /// <remarks>效果, 它在运行时写入承诺, 而它会同时加入, 所以它们的控件会在先前的取消后完成 。</remarks>
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

    /// <summary>要求取消一项承诺的传播企图的任务,并归还其登记失败。</summary>
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
                // 登记失败使其他取消尝试不受影响。
                (failures ??= []).Add(exception);
            }
        }

        return failures;
    }

    /// <summary>
    /// 取消承诺传播的假期,例如延迟和尚未开始身体的儿童,然后
    /// 每一个失败都完成传播。
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

    /// <summary>开始一个孩子, 其祖先的持久取消 到达。</summary>
    public ValueTask<IScheduledTaskHandle> ScheduleChildAsync(
        TaskId taskId,
        DurableTask durableTask,
        CancellationToken cancellationToken) =>
        ScheduleChildAsync(taskId, durableTask, shielded: false, waitForAcceptance: false, cancellationToken);

    /// <summary>开始或重新联系孩子,等待写作。</summary>
    /// <param name="taskId">儿童的身份识别资料</param>
    /// <param name="durableTask">儿童的定义。</param>
    /// <param name="shielded">
    /// 是否由父母启动被保护的儿童保护。 这样的孩子即使祖先取消, 也会创建并运行。
    /// 并且记录着 其祖先的取消没有达到
    /// </param>
    /// <param name="waitForAcceptance">
    /// 儿童主机接受并持久储存该启动是否完成:呼叫另一个 grain
    /// 等待目标被接受, 拒绝的呼声失败了开始。
    /// </param>
    /// <param name="cancellationToken">放弃启动的标记。</param>
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

    /// <summary>儿童启动时,无需书面说明即可开始工作,并返回其控件。</summary>
    /// <remarks>
    /// 开始承诺使用下一期日记写, 以及调用工作项目更改的状态 。
    /// </remarks>
    /// <exception cref="NotSupportedException">自定义主机将定义排在时间表上。</exception>
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
    /// 丢弃儿童定义, 儿童的定义是:
    /// grain当地儿童,呼吁其他 grains和订阅,所有这些运行时间记录和重播。
    /// </summary>
    private static void ThrowIfCustomScheduled(DurableTask durableTask)
    {
        if (durableTask is ISchedulableTask and not IDurableTaskRequest)
        {
            throw new NotSupportedException(
                $"A durable execution runs its grain's local children, durable grain calls, and subscriptions. '{durableTask.GetType()}' is scheduled by another host.");
        }
    }

    /// <summary>当地儿童或呼叫另一个孩子的开始阶段 grain,不写。</summary>
    /// <returns>儿童处理,以及其开始阶段是否改变写作承诺。</returns>
    private StagedChild StageChild(TaskId taskId, DurableTask durableTask, bool shielded)
    {
        if (_shared.Logger.IsEnabled(LogLevel.Trace))
        {
            _shared.Logger.LogTrace("{Id} evaluating task {TaskId}", GrainId, taskId);
        }

        var stateExisted = _storage.TryGetTask(taskId, out var state);
        if (stateExisted)
        {
            // 解码了记录的结果一次, 所以重播接收的手柄就持有它。
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

        // 请求取消取消会终止叶子, 如延迟或身体尚未开始的小孩。 远程
        // 儿童被召者决定结果, 开始的人体重播在下面,
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

        // 取消祖先的子子孙没有创建,除非父母亲启动保护。
        // 请求取消后,请求本身就记录在案。
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

        // 否则,任务必须是当地方法的援引。
        // 例如,其身体的第一次耐久操作或结果,因此同步步骤需要写一个。
        // 其父母的投入和早期结果,以及根据父母的意见和早期的结果得出的身份和投入,因此,重放时没有记录
        // 延迟是指一个当地儿童,其身体根据儿童自己的识别资料记录延迟。
        state ??= GetOrCreateTask(taskId, GetLocalKind(durableTask), request: null);
        MarkShielded(taskId, state, shielded);

        var executionContext = CreateExecutionContext(taskId);
        handle = GetOrCreateRunningTaskHandle(taskId);
        StartInvocation(taskId, static task => task, durableTask, executionContext);
        return new(handle, RequiresWrite: false);
    }

    /// <summary>分阶段启动的控件,以及开始阶段是否改变写入承诺。</summary>
    private readonly record struct StagedChild(IScheduledTaskHandle Handle, bool RequiresWrite);

    // DurableTask.Delay 和 DurableTask.DelayUntil 所创建的定义,其机构记录在其
    // 儿童自己的识别资料。
    private static readonly Type DelayType = DurableTask.Delay(TimeSpan.Zero).GetType();
    private static readonly Type DelayUntilType = DurableTask.DelayUntil(DateTimeOffset.MinValue).GetType();

    /// <summary>返回运行 <paramref name="durableTask"/>的本地儿童的记录类型。</summary>
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

    /// <summary>返回任务祖先之一的持久取消是否达到任务。</summary>
    /// <param name="taskId">任务</param>
    /// <param name="state">如果任务状态存在, 则该任务状态。</param>
    /// <param name="shielded">任务父/母是否开始保护它。</param>
    /// <remarks>
    /// 取消通过祖先完成一项任务,直至最接近被保护的:
    /// 这项任务的祖先没有到达它, 所以它也没有到达它的后代。
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
    /// 返回是否每次运行后,任务祖先之一的持久取消是否拒绝任务的开始
    /// 取消的祖先会观察到它
    /// </summary>
    /// <param name="taskId">要启动的任务。</param>
    /// <param name="state">如果任务状态存在, 则该任务状态。</param>
    /// <param name="shielded">任务父/母是否开始保护它。</param>
    /// <remarks>
    /// 取消在一旦写作后即行处决时,即予取消。
    /// 完成。 一个在工作间隔内开始工作的机构拒绝该工作, 因此它首先观察了自己的取消 :
    /// 拒绝启动丢弃,而 <see cref="DurableTaskContext.IsTaskCancellationRequested"/> 报告
    /// 机构处理取消的处理,例如保护性补偿。
    /// 写后找到已经请求取消的注销。
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

    /// <summary>记录说,一个父母开始保护的儿童,其祖先的取消是无法接触到的。</summary>
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

        // 身体在尝试的第一部分开始,启动程序承认了该请求,因为它接受 grain 电话。
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

        // 取消时, 此尝试仍在运行中, 例如当延迟日记日记写在
        // 飞行时, 只取消尝试的标记, 仅取消尝试的标记。 尝试然后结束任务, 所以一个未完成的操作
        // 返回时,例如延迟,不会超过取消的时间。
        if (!response.IsCompleted
            && _storage.TryGetTask(context.Id, out var taskState)
            && taskState.CancellationRequestedAt.HasValue)
        {
            response = DurableTaskResponse.Canceled;
        }

        try
        {
            // 取消尝试的反应被抛弃:它的任务在下一次启动时继续,或有结果。
            if (response.IsCompleted && !context.IsAttemptEnded)
            {
                Debug.Assert(!_stopping && !IsUnregistered(context), "The runtime cancels every attempt which it stops or discards.");
                await SetResponseAsync(context.Id, response, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _shared.Logger.LogWarning(exception, "{Id} error committing the response of durable task '{TaskId}'.", GrainId, context.Id);

            // 准备失败保留了分阶段反应,因此以后写作时即承诺完成处理。
            if (exception is JournaledStatePreCommitException && !_stopping)
            {
                QueueBackgroundWrite();
            }
        }
        finally
        {
            // 抽象概念结束了重播与记录不符的企图,例如另一个记录的结果。
            // 类型,没有运行时间;尝试后会像操作有分歧的一样拖拉。
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
    /// 返回是否删除了尝试的任务, 并使用其执行树来记录尝试的背景,
    /// 尝试的结果被抛弃了
    /// </summary>
    private bool IsUnregistered(GrainDurableTaskContext context) =>
        !_executionContexts.TryGetValue(context.Id, out var registered) || !ReferenceEquals(registered, context);

    /// <remarks>
    /// 日记承诺在下篇文章中, 任何业务要求的, 都进行每个阶段的变化, 所以一个机构状态
    /// 只有在两个项目都在一个工作项目中进行,才能一起承诺修改及其结果。
    /// 一旦身体运行返回后, 就会作出反应。
    /// </remarks>
    void IDurableTaskGrainRuntime.StageBodyResponse(GrainDurableTaskContext context, DurableTaskResponse response)
    {
        Debug.Assert(!context.IsAttemptEnded && !_stopping && !IsUnregistered(context), "Only a running attempt stages its response.");
        try
        {
            // 当写作时完成答复, 和对尝试回复一样。
            _pendingHandleResponses[context.Id] = StageResponse(context.Id, response);
        }
        catch (Exception exception)
        {
            // 其从身体的继续运行,没有打电话者可以观察失败。
            // 一旦身体运行返回, 就会再次响应 。
            _shared.Logger.LogWarning(exception, "{Id} error staging the response of durable task '{TaskId}'.", GrainId, context.Id);
        }
    }

    /// <summary>
    /// 承诺书写所传播的注销:任务取消,含有 <see cref="CancellationReason"/>,
    /// 运行尝试, 并取消诸如延迟等叶子。 <see cref="Completion"/>提供时,
    /// 一旦传播活动开始, 就会完成 。
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
    /// 阶段是终端反应,并附有完成通知、承诺和完成当地处理。
    /// </summary>
    /// <returns>第一个终端响应, 胜过后一个响应。</returns>
    private async ValueTask<DurableTaskResponse> CommitResponseAsync(
        TaskId taskId,
        DurableTaskResponse response,
        CancellationToken cancellationToken)
    {
        var stored = _storage.TryGetTask(taskId, out var state) && state.Result is { IsCompleted: true };
        var winningResponse = StageResponse(taskId, response);
        if (!stored)
        {
            // 写入后完成响应时, 此手控器将完成此答: 此写入, 或此写失败后完成
            // 在承诺之前,因为准备失败后仍保留分阶段的改动。
            _pendingHandleResponses[taskId] = winningResponse;
        }
        else if (!_pendingHandleResponses.ContainsKey(taskId))
        {
            // 将承诺在未完成处理处理完成之前就储存结果:如随手写作,例如
            // 其结果,即该机关的最终代码已经编妥,处理程序已经完成。
            // 与记录中的文字完成, 所以条目保持了记录。
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
    /// 除非已经进行终端回复,否则将分阶段进行终端回复及其完成通知。
    /// </summary>
    /// <returns>第一个终端响应, 胜过后一个响应。</returns>
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

        // 永远不要覆盖终端结果:分阶段的结果承诺,改变它可以改变观察到的结果。
        if (state.Result is { IsCompleted: true } terminal)
        {
            return terminal;
        }

        // 发件箱读取存储结果的每个完成完成, 因此结果先进行阶段化。 用户对此使用
        // grain 收到结果后退休,因此先抄录目的地。
        response = StoreResult(taskId, state, response);
        foreach (var destination in state.CompletionDestinations.ToArray())
        {
            SendCompletion(taskId, destination);
        }

        return response;
    }

    /// <summary>
    /// 储存任务终端结果。 没有完成目的地, 则该结果的留存期现在开始 。
    /// </summary>
    /// <param name="subscribed">
    /// 订阅儿童的结果是否是订阅任务的结果。
    /// 否则,例如取消或中止的,停止订阅。
    /// </param>
    /// <returns>存储结果 : <paramref name="response"/>,或者取代一个结果的失败 而不序列化。</returns>
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

    /// <summary>跟踪启动的背景工作,即解除排水管的启动和记录其故障。</summary>
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


    /// <summary>标记停泊的等待 <paramref name="awaited"/> 释放为可运行状态的尝试。</summary>
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
    /// 删除保留终止的处决树和不再符合
    /// 援引,一旦最早到期期满即告终止。
    /// </summary>
    /// <returns><see langword="true"/> 如果删除已进行。</returns>
    /// <remarks>
    /// 支票在最早到期日通行证过期前不花费任何费用;随后的扫描将重新计算。
    /// 完成期未到期:完成的退休将删除任务或开始保留。
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
                // 一项待执行的任务没有过期, 另一项任务的树上的执行任务会随之删除 。
                continue;
            }

            var expiry = state.CompletedAt.HasValue ? GetRetentionExpiry(state) : DateTimeOffset.MinValue;
            if (isCancellationRecord)
            {
                // 撤销前的援引在保留记录的范围内得到承认,或永远不予承认。
                var recordExpiry = AddSaturating(state.CreatedAt, _shared.EarlyRecordRetention);
                expiry = recordExpiry > expiry ? recordExpiry : expiry;
            }
            else if (IsSignalRecord(state))
            {
                // 在援引前没有任务而发出信号的记录,在保留记录时予以承认,
                // 或者永远不
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

        // 过期记录可以放在另一棵过期树上, 已经删除了 。
        foreach (var (taskId, state) in expired)
        {
            if (_storage.TryGetTask(taskId, out _))
            {
                DeleteTree(taskId, state);
            }
        }

        return true;
    }

    /// <summary>当终端结果留存结束时返回。</summary>
    /// <remarks>留存从完成时开始,或以后当一个字母已过期的完成重新开始时开始。</remarks>
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

    /// <summary>返回任务是否被排成请求,因此它拥有其执行树。</summary>
    /// <remarks>
    /// 完成决定、独立任务记录、信号记录、信号发送标记和效果记录
    /// 指纹,以 <c>$</c>开头,但属于父母树的一部分;要求的指纹
    /// 十六进制
    /// </remarks>
    private static bool IsInvoked(IDurableTaskState state) =>
        state.RequestFingerprint is { } fingerprint
        && !fingerprint.StartsWith('$');

    /// <summary>返回任务是否只记录在被援引之前就已到达的取消。</summary>
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
    /// 删除一项拥有其生命周期的任务及其执行树:未援引的后代。
    /// 任务本身。
    /// </summary>
    /// <remarks>
    /// 与树面积成比例的成本成本时间。树中的每个非终点任务取消:待决远程
    /// 取消孩子的注销与删除相配合,并取消一次尝试,并放弃其结果。
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
            // 发送通知的发件箱拥有该通知,直到退休;发出
            // 身份再次将是一个新的发送, 与不同的入境禁闭。
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
            // 拆除等待取消的尝试, 排水管。
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

        // 留存额在最后期限截止时终止,期限可在 grain 空闲期间通过。
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

        // 诊断性能解码存储的有效载荷,因此不再报告已解码的有效载荷而不是投掷。
        // 取消或取消任务时报告其取消理由 。
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
    /// 阶段 通过收件箱收到的取消, 在收件箱批次承诺后传播。
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
        // 取消在宣传前就一直存在。
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
    /// 将取消的广告宣传到它所达到的尝试和叶叶上,
    /// 写入已执行的注销运行并返回完成的状态。
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
    /// 请求新到的每项任务都记录 <paramref name="reason"/>,因此每一项执行和处理
    /// 申请的收款已取消,并以此为由。
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
            // 只有打电话者取消自己的孩子,而收信箱提供的孩子才能超过孩子的
            // 援引:记录到达时取消援引。直接请求指定一项任务,而该任务
            // grain 保留,所以请求其他任务阶段时没有要求任何内容。
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
                // 名为组合器的输入是这个任务最接近的 存储在组合器身份卡下的子孙
                Debug.Assert(currentTaskId.IsAncestorOf(childTaskId));

                // 被保护的孩子不能从祖先的取消中得到;
                // 树的取消,树的中止, 和树的删除 仍然到达它。
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
                    // 所记录的请求包括处决者自己取消其远程电话;见 CancelRemoteAsync。
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
    /// 中止任务及其执行树的执行树: 将树上未完成的任务都写入失败
    /// <see cref="DurableTaskAbortedException"/> 也对每个待通知的另外 grain和每个
    /// 树上运行的尝试被取消, 结果被丢弃 。
    /// </summary>
    /// <remarks>终端任务,或此 grain 无法保留的任务,没有变化。</remarks>
    internal async ValueTask AbortTaskAsync(TaskId taskId, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (StageAbort(taskId, reason, callerId: default))
        {
            await WriteStateAsync(cancellationToken);
        }
    }

    /// <summary>阶段 通过收件箱接收的中止, 取消中止尝试 。</summary>
    /// <remarks>
    /// 中止可以先于其援引,如取消;然后作为取消而记录,这样就可援引
    /// 用户已将其自己的工作记录关闭。
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

    /// <returns><see langword="true"/> 如果中止阶段更改。</returns>
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

        // 树上的每一任务记录同样的失败 所以一个反应能为它们服务
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

        // 取消对企图的登记,就抛弃其结果;解除职务等待它排水。
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

    // ScheduledTask 的基建体是抽象内容的内部,所以一个未类型控点使用对象结果。
    ScheduledTask IDurableTaskManager.GetTask(TaskId taskId) => ((IDurableTaskManager)this).GetTask<object?>(taskId);

    // 运行时间直接为接线员的民意测验和取消服务,因为可以拨打 grain 电话。
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

        // 使用完的处理器,即承诺后再水合物;一旦实施后,分阶段再完成处理。
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
    /// 订阅或独立起始记录 grain, 其中
    /// 呼吁 grain 达到本 grain范围内的目标储存量的任务
    /// 调用电线识别符号,并保留到此 grain 接受完成为止。任何其他操作的识别符号,
    /// 或没有记录的任何情况,例如早先取消合同时阻止的开始,是它自己的。
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
    /// 返回 <paramref name="nodeId"/>的线条识别符,此 grain执行树之一的存储节点,或
    /// 执行时将记录的信息:从其他 grains 发出或发送的信息的识别符号,用它命名。
    /// </summary>
    /// <remarks>
    /// 识别符覆盖节点树的化形, 所以每个根标识符运行时都会发送自己的信息 。
    /// 见 <see cref="CallerScope"/>。
    /// </remarks>
    internal TaskId GetWireId(TaskId nodeId) => CallerScope.GetWireId(GetIncarnation(nodeId), nodeId);

    /// <summary>
    /// 返回 <paramref name="wireId"/> 名称的节点,以及节点存储并属于运行时的状态
    /// 其树的果实, <paramref name="wireId"/> 姓名。
    /// </summary>
    /// <returns>
    /// <see langword="false"/> 时,当线条识别名称被删除的运行时间:节点没有存储或稍后
    /// 管理其树的库藏。
    /// </returns>
    /// <exception cref="InvalidOperationException"><paramref name="wireId"/> 并不是一个线条识别器。</exception>
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
    /// 每一次处决都属于援引任务树,当其首先被援引时记录其化身
    /// 存储,所以行走总是以被存储的引用任务结束。
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
    /// 处决记录的行动是其最接近的储存子孙:即自己是处决的子孙;
    /// 信号值属于所援引的任务,发送者提供
    /// 被援引的后代保留自己的生命周期;取消已生效的
    /// 在提出请求之前,请求书记录未执行;执行; 裁决版本记录
    /// 其达到的操作不是它所达到的操作,而是它的后继操作。 因此,删除版本检查的部署留下
    /// 和注销点记录,其中执行的
    /// 取消, 重放以计算其操作来达到此值 。
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
        // 每一次尝试都因运行时间停止而被取消, 所以一旦启动停止后, 没有记录任何记录。
        _stopping = true;
        foreach (var context in _executionContexts.Values)
        {
            CancelAttempt(context, DurableTaskAttemptCancellationReason.ActivationStopping);
        }

        await _deactivationCts.CancelAsync();

        // 下一次启动中,任务将持续进行,因此,在下次激活中,该启动的操作把手上的服务员失败了,作为
        // 到达时运行时间停止会到达, 而不是观察任务结果。
        var stopping = new DurableTaskAttemptCanceledException(DurableTaskAttemptCancellationReason.ActivationStopping, _deactivationCts.Token);
        foreach (var handle in _taskHandles.Values)
        {
            handle.SetStopping(stopping);
        }

        while (true)
        {
            // 停止停放的企图被抛弃,再也不能恢复,因此立即解决;其部分运行的企图就解决
            // 当段段结束时。 放弃排水管会使重置激活与此重叠。
            // 由适应器控制的等待等待观察生命周期标记。 任意用户代码可以忽略它, 所以禁用
            // 刻意等待每个运行的段,然后替换重播才能开始。
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
    /// 取消 <paramref name="context"/>尝试的 <paramref name="reason"/>,并在
    /// 以后的工作项目,该工作项目将拆除排水管。
    /// </summary>
    /// <remarks>
    /// 激活、中止和删除任务树的取消 尝试取消,在激活的调度器上。
    /// 取消后,尝试的持久操作 扔出 <see cref="DurableTaskAttemptCanceledException"/>
    /// 运行时间记录它们中没有一个记录, 尝试的结果被丢弃。 日记状态变化是其代码
    /// 诸如清理等 makes , 以 grain下个日记写入承诺。 它的代码通过它来观察取消
    /// 标记一旦被信号显示, 在运行时间当前操作之外 。
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
