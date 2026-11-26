using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Protocol;

/// <remarks>
/// 服务器操作操作阶段任务状态, 并不运行 grain 代码而执行该状态, 因此它们会与 grain的代码互换
/// 包括其持久处决部分:取消执行部分待执行部分的
/// 其它工作, 而时间表不会等待 grain执行处决 。
/// </remarks>
[Alias("IDurableTaskServerGrainExtension")]
internal interface IDurableTaskServer : IGrainExtension
{
    // 被 DurableTaskRequest.Invoke 调 [以确保安排任务
    [Alias("ScheduleAsync"), AlwaysInterleave]
    ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, IDurableTaskRequest request, CancellationToken cancellationToken = default);

    // API 使用用 ScheduledTask/<T> 检查任务的结果。
    // ScheduledTask 无法查阅原始请求,因此无法提交合理的 IDurableTaskRequest。
    // 民意测验在等待结果时,不会改变状态,所以会间隔: grain 不断处理请求和(Q0Q]
    // 完成任务的信息。
    [Alias("SubscribeOrPollAsync"), AlwaysInterleave]
    ValueTask<DurableTaskResponse> SubscribeOrPollAsync(TaskId taskId, SubscribeOrPollOptions options, CancellationToken cancellationToken = default);

    [Alias("CancelAsync"), AlwaysInterleave]
    ValueTask CancelAsync(TaskId taskId, CancellationToken cancellationToken = default);

    /// <summary>可持久地请求取消此任务 grain,携带 <paramref name="reason"/>.</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">
    /// 任务 <see cref="DurableTaskContext.TaskCancellationReason"/> 报告的原因,或
    /// <see langword="null"/> 毫无理由。
    /// </param>
    /// <param name="cancellationToken">放弃等待请求承诺的符号。</param>
    /// <remarks>
    /// 第一次取消请求决定了原因。 请求执行本 grain 不保留丢弃的任务
    /// <see cref="DurableTaskNotFoundException"/> 并存留任何物品。
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">这个 grain 不再保留任务。</exception>
    [Alias("CancelWithReasonAsync"), AlwaysInterleave]
    ValueTask CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// 行政中止此 grain的任务 : 执行任务及其执行树失败, 执行任务
    /// <see cref="DurableTaskAbortedException"/> 在一个字中写,停止运行尝试,不继续运行
    /// 任务代码。
    /// </summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">中止的原因,记录为例外信息。</param>
    /// <param name="cancellationToken">放弃等待中止执行的标记。</param>
    /// <remarks>任务为终端,或此 grain 不保留的任务不变。</remarks>
    [Alias("AbortAsync"), AlwaysInterleave]
    ValueTask AbortAsync(TaskId taskId, string reason, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Immutable, Alias(nameof(SubscribeOrPollOptions))]
internal readonly struct SubscribeOrPollOptions
{
    [Id(0)]
    public TimeSpan PollTimeout { get; init; }

}

[Alias("IDurableTaskGrainExtension")]
internal interface IDurableTaskGrainExtension : IGrainExtension, IDurableTaskServer
{
    [Alias("GetTasksAsync"), AlwaysInterleave]
    IAsyncEnumerable<(TaskId TaskId, DurableTaskDiagnosticState State)> GetTasksAsync(CancellationToken cancellationToken = default);

    [Alias("GetRunningTasksAsync"), AlwaysInterleave]
    IAsyncEnumerable<TaskId> GetRunningTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>向所援引的任务发出信号 <paramref name="taskId"/> 中 的 grain.</summary>
    /// <param name="taskId">所引用的任务识别符号。</param>
    /// <param name="name">信号名。</param>
    /// <param name="value">成功的响应 保持信号的价值。</param>
    /// <param name="cancellationToken">放弃等待信号承诺的标记。</param>
    /// <returns>一旦任务长期保持信号值, 即完成操作。</returns>
    /// <exception cref="DurableTaskNotFoundException">下 grain 保留不引用任务 <paramref name="taskId"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// 任务为终端,对信号没有价值,或者对信号持有不同的价值。
    /// </exception>
    [Alias("SignalAsync"), AlwaysInterleave]
    ValueTask SignalAsync(TaskId taskId, string name, DurableTaskResponse value, CancellationToken cancellationToken = default);

    /// <summary>向所援引的任务发出信号 <paramref name="taskId"/> 中 的 grain,然后返回任务是如何处理的。</summary>
    /// <param name="taskId">所引用的任务识别符号。</param>
    /// <param name="name">信号名。</param>
    /// <param name="value">成功的响应 保持信号的价值。</param>
    /// <param name="cancellationToken">放弃等待信号承诺的标记。</param>
    /// <returns>
    /// <see cref="DurableSignalResult.Accepted"/> 或 <see cref="DurableSignalResult.Duplicate"/> 任务一旦完成
    /// 持有该值,或任务拒绝的原因,但不会改变任何内容。
    /// </returns>
    [Alias("TrySignalAsync"), AlwaysInterleave]
    ValueTask<DurableSignalResult> TrySignalAsync(TaskId taskId, string name, DurableTaskResponse value, CancellationToken cancellationToken = default);
}

[GenerateSerializer]
[Alias("DurableTaskDiagnosticState")]
internal struct DurableTaskDiagnosticState
{
    [Id(0)]
    public DateTimeOffset? CreatedAt { get; set; }

    [Id(1)]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// 获取或设置诊断状态,或者当数值被默认初始化时 <see langword="null"/>
    /// 或收到未提供该委员的版本。
    /// </summary>
    /// <remarks>
    /// 含有存储结果的任务 <c>Completed</c>, <c>Faulted</c>, <c>Canceled</c>,或 <c>Unreadable</c> 讽 果结果
    /// 不再解码。 没有代码的任务就是 <c>Canceling</c>, <c>Delayed</c>,或 <c>Pending</c>.
    /// </remarks>
    [Id(2)]
    public string? Status { get; set; }

    /// <summary>获取或设置本地请求,格式化为方法调用。</summary>
    [Id(3)]
    public string? Request { get; set; }

    /// <summary>
    /// 获取或设置存储结果的概要:其价值、例外或解码失败。 A
    /// <c>Canceling</c> 或 <c>Canceled</c> 任务报告其取消理由,如果有的话。
    /// </summary>
    [Id(4)]
    public string? Response { get; set; }

    /// <summary>
    /// 获取或设置诊断性完成服务服务员,或者当值为 <see langword="null"/> 时
    /// 默认初始化或接收未提供该成员版本的版本。
    /// </summary>
    [Id(5)]
    public List<string>? Waiters { get; set; }

    /// <summary>获取或设定延迟的到期时间。</summary>
    [Id(6)]
    public DateTimeOffset? DueTime { get; set; }

    /// <summary>获取或设置执行远程儿童任务的 grain 。</summary>
    [Id(7)]
    public string? RemoteTarget { get; set; }

    public override readonly string ToString() => $"[{Status}, Created: {CreatedAt}, Completed: {CompletedAt}, Due: {DueTime}, Request: {Request}, Remote: {RemoteTarget}, Response: {Response}, Waiters: {string.Join(", ", Waiters ?? [])}]";
}

// 客户/ grains 至 DurableTasks的中间请求。
// Orleans- 支持 DurableTasks 或其他持久任务。
// 其部分原因是允许 Orleans 拦截请求,并确保将结果储存在当地。
// 有两个执行:
// 1. Grain 代理:本地存储结果,支持签名。只有 grain中存在。
// 2. 客户代理:不存储本地结果,不支持签名。可在 grain上下文之外查阅。
internal interface IDurableTaskGrainRuntime
{
    DateTimeOffset UtcNow { get; }

    /// <summary>得到 grain 执行运行时间的行刑者</summary>
    GrainId GrainId { get; }

    /// <summary>
    /// 启动或重新连接孩子 <paramref name="taskId"/>,等待日记写作。
    /// 当设定 <paramref name="waitForAcceptance"/> 时,儿童宿主接受该宿主。
    /// </summary>
    ValueTask<IScheduledTaskHandle> ScheduleChildAsync(
        TaskId taskId,
        DurableTask taskDefinition,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken);

    /// <summary>启动时,儿童开始 <paramref name="taskId"/> ,没有书面。</summary>
    IScheduledTaskHandle PostChild(TaskId taskId, DurableTask taskDefinition, bool shielded);

    /// <summary>
    /// 启动 <paramref name="request"/> ,作为独立根任务 <paramref name="rootId"/>,记录并订阅于
    /// <paramref name="recordId"/>,等待日记的开始和
    /// <paramref name="waitForAcceptance"/> 是设置的,以接受任务 grain。
    /// </summary>
    ValueTask<IScheduledTaskHandle> StartDetachedAsync(
        TaskId recordId,
        TaskId rootId,
        IDurableTaskRequest request,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken);

    /// <summary>
    /// 独立根任务 <paramref name="rootId"/>的启动阶段,在
    /// <paramref name="recordId"/>,在激活时,没有书面。
    /// </summary>
    IScheduledTaskHandle PostDetached(TaskId recordId, TaskId rootId, IDurableTaskRequest request, bool shielded);

    /// <summary>
    /// 根任务 <paramref name="rootId"/>的起始阶段, 从耐久执行之外运行的代码开始 。
    /// 启动,不写。启动承诺使用 grain的下一期日记写入 。
    /// </summary>
    void PostRoot(TaskId rootId, IDurableTaskRequest request);

    ValueTask<DurableTaskResponse> ScheduleDelayAsync(TaskId taskId, DateTimeOffset dueTime, CancellationToken cancellationToken);
    ValueTask CancelRemoteAsync(TaskId taskId, GrainId target, string? reason, CancellationToken cancellationToken);
    /// <summary>
    /// 返回先完成的候选者,或记录在
    /// <paramref name="decisionId"/>,在等待时停车 <paramref name="attempt"/> 。
    /// </summary>
    ValueTask<TaskId> SelectCompletionAsync(
        GrainDurableTaskContext attempt,
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken);
    IScheduledTaskHandle GetScheduledTaskHandle(TaskId taskId);

    /// <summary>
    /// 返回其主机存储操作的任务的标识符 <paramref name="taskId"/> 排排排排
    /// 开始执行或观察,当其他当事方处理任务时储存该执行的 grain 。
    /// </summary>
    /// <param name="taskId">操作的识别符号。</param>
    /// <param name="addressedHost">
    /// 存储一项分离或指定任务的 grain ,或用于执行该任务任务的 <see langword="default"/>
    /// 树的树本身。
    /// </param>
    TaskId GetTargetTaskId(TaskId taskId, out GrainId addressedHost);

    /// <summary>
    /// 返回所引用任务中执行树包含 <paramref name="executionId"/>的化身。
    /// </summary>
    Guid GetIncarnation(TaskId executionId);

    /// <summary>返回时记录任务终端结果,或无 <see langword="null"/> 。</summary>
    DateTimeOffset? GetCompletedAt(TaskId taskId);

    /// <summary>
    /// 返回先前尝试 <paramref name="executionId"/> 记录并重放的操作
    /// 达到,如其儿童、标记和记录的效果,或无效果的 <see langword="null"/> 。
    /// </summary>
    /// <remarks>调用者拥有返回的集。</remarks>
    HashSet<TaskId>? GetRecordedOperations(TaskId executionId) => null;

    /// <summary>注意到有人企图 <paramref name="executionId"/> 重播其记录操作的运行运行</summary>
    void OnReplayEnded(TaskId executionId)
    {
    }

    /// <summary>
    /// 返回先前尝试 <paramref name="executionId"/> 的取消点,该点首先被观察到
    /// 任务取消,或无记录时的 <see langword="null"/> 。
    /// </summary>
    int? GetCancellationPoint(TaskId executionId) => null;

    /// <summary>
    /// 阶段 记录 <paramref name="executionId"/> 首次遵守任务时的取消点
    /// 取消, 不写 。
    /// </summary>
    void RecordCancellationPoint(TaskId executionId, int point)
    {
    }

    /// <summary>
    /// 阶段 运行 <paramref name="context"/> 的机构从运行
    /// 人体最后的代码 激活,没有写。
    /// </summary>
    /// <remarks>
    /// 答复随后承诺与该机构最终代码更改的日记状态进行联系。
    /// 一旦身体运行时的反应返回它, 则保持一个已经分阶段的响应不变。 运行时间
    /// 无法提前启动响应 来维持默认执行,这毫无用处。
    /// </remarks>
    void StageBodyResponse(GrainDurableTaskContext context, DurableTaskResponse response)
    {
    }

    /// <summary>
    /// 返回 <paramref name="effectId"/> 记录或运行 <paramref name="effect"/> 和记录阶段的结果
    /// 其结果,不以书面形式提出。
    /// </summary>
    DurableTaskResponse RunEffect<TState>(TaskId effectId, TState state, Func<TState, DurableTaskResponse> effect) =>
        throw new NotSupportedException("The durable task runtime does not support recorded effects.");

    /// <summary>
    /// 返回到版本 <paramref name="name"/> 减去 <paramref name="executionId"/> 或其最近的附件
    /// 在同一次执行中记录或分阶段记录 <paramref name="version"/> 下
    /// <paramref name="executionId"/>,不以书面形式返回。
    /// </summary>
    int GetOrRecordVersion(TaskId executionId, string name, int version) =>
        throw new NotSupportedException("The durable task runtime does not support versions.");

    /// <summary>
    /// 取消 <paramref name="context"/> 运行到 <paramref name="reason"/>的尝试,
    /// <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 或
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>,将其任务留待日后启动再放。
    /// </summary>
    void StallAttempt(GrainDurableTaskContext context, DurableTaskAttemptCancellationReason reason, Exception exception) =>
        throw new NotSupportedException("The durable task runtime does not support stalled executions.");

    /// <summary>
    /// 当早期运行记录到 a 时投 <c>DurableTask.RunInline</c> 有效 <paramref name="effectId"/>,而该
    /// <paramref name="operationId"/>,例如一个命名的组合器输入,其组合器具有效果的名称。
    /// </summary>
    /// <param name="operationId">运行请求的操作。</param>
    /// <param name="effectId">手术执行之前的祖先 效果会记录下来</param>
    /// <param name="requested">请求的操作说明,以备错误。</param>
    /// <exception cref="InvalidOperationException">先前的运行记录了 <paramref name="effectId"/>下的影响。</exception>
    void ThrowIfRecordedAsEffect(TaskId operationId, TaskId effectId, string requested)
    {
    }

    /// <summary>
    /// 等待所引用任务中的执行树含有的信号 <paramref name="name"/>
    /// <paramref name="executionId"/>,并返回其承诺值和记录时间。
    /// </summary>
    ValueTask<(DurableTaskResponse Value, DateTimeOffset RecordedAt)> WaitForSignalAsync(
        GrainDurableTaskContext attempt,
        TaskId executionId,
        string name,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The durable task runtime does not support signals.");

    /// <summary>
    /// 向引用的任务发送信号 <paramref name="taskId"/> 日期 时 <paramref name="target"/>下,记录发送
    /// <paramref name="markerId"/>,等待日记写既承诺承诺也承诺承诺的日记。
    /// </summary>
    /// <param name="throwOnRejection">是否因为某项任务而拒绝 grain 而不是被记录和归还。</param>
    ValueTask<DurableSignalResult> SendSignalAsync(
        TaskId markerId,
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        bool throwOnRejection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The durable task runtime does not support signals.");
    //IScheduledTaskHandle OnCreateScheduledTaskHandle(IScheduledTaskHandle 处理);
}
