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
/// 服务器的操作暂存任务状态并提交，而不运行 Grain 代码，因此它们与 Grain 的
/// 请求（包括其持久化执行实例的各个执行片段）交错执行：即使某个执行实例的片段正在等待
/// 其他工作，取消请求也能到达该执行实例，而调度操作不会等待 Grain 正在运行的执行实例。
/// </remarks>
[Alias("IDurableTaskServerGrainExtension")]
internal interface IDurableTaskServer : IGrainExtension
{
    // 由 DurableTaskRequest.Invoke 调用，确保任务已被调度。
    [Alias("ScheduleAsync"), AlwaysInterleave]
    ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, IDurableTaskRequest request, CancellationToken cancellationToken = default);

    // ScheduledTask/<T> 用于检查任务结果的 API。
    // ScheduledTask 无法访问原始请求，因此无法提交有意义的 IDurableTaskRequest。
    // 轮询等待结果而不改变状态，所以可以交错执行：Grain 继续处理使任务完成的请求和
    // 消息。
    [Alias("SubscribeOrPollAsync"), AlwaysInterleave]
    ValueTask<DurableTaskResponse> SubscribeOrPollAsync(TaskId taskId, SubscribeOrPollOptions options, CancellationToken cancellationToken = default);

    [Alias("CancelAsync"), AlwaysInterleave]
    ValueTask CancelAsync(TaskId taskId, CancellationToken cancellationToken = default);

    /// <summary>持久化地请求取消此 Grain 上的任务，并携带 <paramref name="reason"/>。</summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">
    /// 任务的 <see cref="DurableTaskContext.TaskCancellationReason"/> 所报告的原因；若无原因，
    /// 则为 <see langword="null"/>。
    /// </param>
    /// <param name="cancellationToken">用于放弃等待请求提交的令牌。</param>
    /// <remarks>
    /// 第一次取消请求决定原因。针对该 Grain 未保留的任务的请求会抛出
    /// <see cref="DurableTaskNotFoundException"/>，且不存储任何内容。
    /// </remarks>
    /// <exception cref="DurableTaskNotFoundException">此 Grain 未保留该任务。</exception>
    [Alias("CancelWithReasonAsync"), AlwaysInterleave]
    ValueTask CancelAsync(TaskId taskId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对此 Grain 上的任务执行管理性中止：在一次写入中将任务及其执行树以
    /// <see cref="DurableTaskAbortedException"/> 作为失败结果提交，并停止它们正在运行的执行尝试，不再运行任何
    /// 任务代码。
    /// </summary>
    /// <param name="taskId">任务标识符。</param>
    /// <param name="reason">作为异常消息记录的中止原因。</param>
    /// <param name="cancellationToken">用于放弃等待中止操作提交的令牌。</param>
    /// <remarks>已处于终态的任务，或者此 Grain 未保留的任务，保持不变。</remarks>
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

    /// <summary>向此 Grain 上被调用的任务 <paramref name="taskId"/> 投递信号。</summary>
    /// <param name="taskId">被调用任务的标识符。</param>
    /// <param name="name">信号名称。</param>
    /// <param name="value">包含信号值的成功响应。</param>
    /// <param name="cancellationToken">用于放弃等待信号提交的令牌。</param>
    /// <returns>在任务持久化保存信号值后完成的操作。</returns>
    /// <exception cref="DurableTaskNotFoundException">Grain 未保留被调用任务 <paramref name="taskId"/>。</exception>
    /// <exception cref="InvalidOperationException">
    /// 任务已处于终态且没有该信号的值，或者已保存不同的信号值。
    /// </exception>
    [Alias("SignalAsync"), AlwaysInterleave]
    ValueTask SignalAsync(TaskId taskId, string name, DurableTaskResponse value, CancellationToken cancellationToken = default);

    /// <summary>向此 Grain 上被调用的任务 <paramref name="taskId"/> 投递信号，并返回任务的处理结果。</summary>
    /// <param name="taskId">被调用任务的标识符。</param>
    /// <param name="name">信号名称。</param>
    /// <param name="value">包含信号值的成功响应。</param>
    /// <param name="cancellationToken">用于放弃等待信号提交的令牌。</param>
    /// <returns>
    /// 当任务持久化保存该值时返回 <see cref="DurableSignalResult.Accepted"/> 或 <see cref="DurableSignalResult.Duplicate"/>，
    /// 否则返回任务拒绝该值的原因，而不产生任何变更。
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
    /// 获取或设置诊断状态；值为 <see langword="null"/> 的情况包括默认初始化，
    /// 或从不提供此成员的版本接收到该值。
    /// </summary>
    /// <remarks>
    /// 已存储结果的任务为 <c>Completed</c>、<c>Faulted</c>、<c>Canceled</c>，或 <c>Unreadable</c>（结果
    /// 无法再解码时）。未存储结果的任务为 <c>Canceling</c>、<c>Delayed</c> 或 <c>Pending</c>。
    /// </remarks>
    [Id(2)]
    public string? Status { get; set; }

    /// <summary>获取或设置本地请求，其格式为方法调用。</summary>
    [Id(3)]
    public string? Request { get; set; }

    /// <summary>
    /// 获取或设置已存储结果的摘要：其值、异常或解码失败信息。
    /// <c>Canceling</c> 或 <c>Canceled</c> 状态的任务会报告取消原因（若有）。
    /// </summary>
    [Id(4)]
    public string? Response { get; set; }

    /// <summary>
    /// 获取或设置诊断用的完成等待者；值为 <see langword="null"/> 的情况包括
    /// 默认初始化，或从不提供此成员的版本接收到该值。
    /// </summary>
    [Id(5)]
    public List<string>? Waiters { get; set; }

    /// <summary>获取或设置待处理延迟的到期时间。</summary>
    [Id(6)]
    public DateTimeOffset? DueTime { get; set; }

    /// <summary>获取或设置执行远程子任务的 Grain。</summary>
    [Id(7)]
    public string? RemoteTarget { get; set; }

    public override readonly string ToString() => $"[{Status}, Created: {CreatedAt}, Completed: {CompletedAt}, Due: {DueTime}, Request: {Request}, Remote: {RemoteTarget}, Response: {Response}, Waiters: {string.Join(", ", Waiters ?? [])}]";
}

// 在客户端或 Grain 与 DurableTasks 之间中转请求。
// 既支持以 Orleans 为后端的 DurableTasks，也支持其他持久化任务。
// 此设计的部分目的是让 Orleans 能够拦截请求，并确保结果在本地存储。
// 有两种实现：
// 1. Grain 代理：在本地存储结果，支持订阅。仅在 Grain 的上下文中可用。
// 2. 客户端代理：不在本地存储结果，不支持订阅。在 Grain 的上下文之外可用。
internal interface IDurableTaskGrainRuntime
{
    DateTimeOffset UtcNow { get; }

    /// <summary>获取其执行实例由运行时运行的 Grain。</summary>
    GrainId GrainId { get; }

    /// <summary>
    /// 启动或重新附接到子任务 <paramref name="taskId"/>，等待提交它的日志写入，并且
    /// 设置 <paramref name="waitForAcceptance"/> 时，等待子任务的宿主接受该子任务。
    /// </summary>
    ValueTask<IScheduledTaskHandle> ScheduleChildAsync(
        TaskId taskId,
        DurableTask taskDefinition,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken);

    /// <summary>暂存在激活实例上启动子任务 <paramref name="taskId"/> 的操作，不进行写入。</summary>
    IScheduledTaskHandle PostChild(TaskId taskId, DurableTask taskDefinition, bool shielded);

    /// <summary>
    /// 将 <paramref name="request"/> 作为分离根任务 <paramref name="rootId"/> 启动，在
    /// <paramref name="recordId"/> 下记录并订阅，等待提交启动操作的日志写入，并在
    /// <paramref name="waitForAcceptance"/> 已设置时，等待任务所属 Grain 接受该任务。
    /// </summary>
    ValueTask<IScheduledTaskHandle> StartDetachedAsync(
        TaskId recordId,
        TaskId rootId,
        IDurableTaskRequest request,
        bool shielded,
        bool waitForAcceptance,
        CancellationToken cancellationToken);

    /// <summary>
    /// 在激活实例上暂存分离根任务 <paramref name="rootId"/> 的启动操作，在
    /// <paramref name="recordId"/> 下记录并订阅，但不执行写入。
    /// </summary>
    IScheduledTaskHandle PostDetached(TaskId recordId, TaskId rootId, IDurableTaskRequest request, bool shielded);

    /// <summary>
    /// 在激活实例上暂存从持久化执行之外运行的代码启动根任务 <paramref name="rootId"/> 的操作，而不
    /// 执行写入。启动操作随 Grain 的下一次日志写入提交。
    /// </summary>
    void PostRoot(TaskId rootId, IDurableTaskRequest request);

    ValueTask<DurableTaskResponse> ScheduleDelayAsync(TaskId taskId, DateTimeOffset dueTime, CancellationToken cancellationToken);
    ValueTask CancelRemoteAsync(TaskId taskId, GrainId target, string? reason, CancellationToken cancellationToken);
    /// <summary>
    /// 返回最先完成的候选任务，或将最先完成的候选任务记录在
    /// <paramref name="decisionId"/> 下，并在等待期间挂起 <paramref name="attempt"/>。
    /// </summary>
    ValueTask<TaskId> SelectCompletionAsync(
        GrainDurableTaskContext attempt,
        TaskId decisionId,
        IReadOnlyList<TaskId> candidates,
        CancellationToken cancellationToken);
    IScheduledTaskHandle GetScheduledTaskHandle(TaskId taskId);

    /// <summary>
    /// 返回执行实例中的操作 <paramref name="taskId"/> 所启动或观察的任务在其宿主上的存储标识符，以及
    /// 在其他参与方寻址该任务时存储它的 Grain。
    /// </summary>
    /// <param name="taskId">操作标识符。</param>
    /// <param name="addressedHost">
    /// 存储分离任务或被订阅任务的 Grain；如果值为 <see langword="default"/>，则任务由
    /// 执行实例的执行树拥有。
    /// </param>
    TaskId GetTargetTaskId(TaskId taskId, out GrainId addressedHost);

    /// <summary>
    /// 返回被调用任务的运行世代标识，该任务的执行树包含 <paramref name="executionId"/>。
    /// </summary>
    Guid GetIncarnation(TaskId executionId);

    /// <summary>返回任务的终态结果记录时间；若无结果，则返回 <see langword="null"/>。</summary>
    DateTimeOffset? GetCompletedAt(TaskId taskId);

    /// <summary>
    /// 返回 <paramref name="executionId"/> 先前执行尝试记录且会被重放
    /// 到达的操作，例如其子任务、标记和已记录的效果；没有这些操作时返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>返回的集合归调用方所有。</remarks>
    HashSet<TaskId>? GetRecordedOperations(TaskId executionId) => null;

    /// <summary>标记 <paramref name="executionId"/> 的执行尝试在重放已记录的操作后开始实时运行。</summary>
    void OnReplayEnded(TaskId executionId)
    {
    }

    /// <summary>
    /// 返回 <paramref name="executionId"/> 先前的执行尝试首次观察到其任务取消的取消点，
    /// 若未记录，则返回 <see langword="null"/>。
    /// </summary>
    int? GetCancellationPoint(TaskId executionId) => null;

    /// <summary>
    /// 暂存 <paramref name="executionId"/> 首次观察到其任务取消的取消点的记录，
    /// 但不执行写入。
    /// </summary>
    void RecordCancellationPoint(TaskId executionId, int point)
    {
    }

    /// <summary>
    /// 在激活实例上运行主体最后一段代码的工作项中，暂存 <paramref name="context"/> 所运行主体的终态响应，
    /// 但不执行写入。
    /// </summary>
    /// <remarks>
    /// 之后，响应会与主体最后一段代码修改的日志化状态一同提交。当主体运行返回响应后，执行尝试仍会提交
    /// 此响应，此时已暂存的响应保持不变。不提前暂存响应的运行时
    /// 保留此默认实现，其不执行任何操作。
    /// </remarks>
    void StageBodyResponse(GrainDurableTaskContext context, DurableTaskResponse response)
    {
    }

    /// <summary>
    /// 返回 <paramref name="effectId"/> 记录的结果，或者运行 <paramref name="effect"/> 并暂存其结果的
    /// 记录，但不执行写入。
    /// </summary>
    DurableTaskResponse RunEffect<TState>(TaskId effectId, TState state, Func<TState, DurableTaskResponse> effect) =>
        throw new NotSupportedException("The durable task runtime does not support recorded effects.");

    /// <summary>
    /// 返回 <paramref name="name"/> 的版本，该版本由 <paramref name="executionId"/> 或同一被调用任务内最近的外层
    /// 执行实例记录；如果未记录，则暂存 <paramref name="version"/> 的记录，位于
    /// <paramref name="executionId"/> 下，而不执行写入，并返回该版本。
    /// </summary>
    int GetOrRecordVersion(TaskId executionId, string name, int version) =>
        throw new NotSupportedException("The durable task runtime does not support versions.");

    /// <summary>
    /// 取消 <paramref name="context"/> 正在运行的执行尝试，取消原因为 <paramref name="reason"/>，即
    /// <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 或
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>，使任务保持待处理状态，以供之后的激活实例重放。
    /// </summary>
    void StallAttempt(GrainDurableTaskContext context, DurableTaskAttemptCancellationReason reason, Exception exception) =>
        throw new NotSupportedException("The durable task runtime does not support stalled executions.");

    /// <summary>
    /// 如果先前一次运行记录了 <c>DurableTask.RunInline</c> 效果，其标识符为 <paramref name="effectId"/>，而该标识符包含
    /// <paramref name="operationId"/>（例如命名组合器的一个输入，且该组合器名称与效果名称相同），则抛出异常。
    /// </summary>
    /// <param name="operationId">本次运行请求的操作。</param>
    /// <param name="effectId">操作在其执行实例下的祖先（若为效果，则本应记录该祖先）。</param>
    /// <param name="requested">所请求操作的描述，用于错误信息。</param>
    /// <exception cref="InvalidOperationException">先前的运行在 <paramref name="effectId"/> 下记录了效果。</exception>
    void ThrowIfRecordedAsEffect(TaskId operationId, TaskId effectId, string requested)
    {
    }

    /// <summary>
    /// 等待被调用任务的信号 <paramref name="name"/>，该任务的执行树包含
    /// <paramref name="executionId"/>，并返回已提交的信号值及记录时间。
    /// </summary>
    ValueTask<(DurableTaskResponse Value, DateTimeOffset RecordedAt)> WaitForSignalAsync(
        GrainDurableTaskContext attempt,
        TaskId executionId,
        string name,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The durable task runtime does not support signals.");

    /// <summary>
    /// 向 <paramref name="taskId"/> 所标识、位于 <paramref name="target"/> 上的被调用任务发送信号，将发送操作记录在
    /// <paramref name="markerId"/> 下，并等待提交两者的日志写入。
    /// </summary>
    /// <param name="throwOnRejection">此 Grain 的任务拒绝信号时，是否抛出异常，而不是记录并返回拒绝结果。</param>
    ValueTask<DurableSignalResult> SendSignalAsync(
        TaskId markerId,
        GrainId target,
        TaskId taskId,
        string name,
        DurableTaskResponse value,
        bool throwOnRejection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The durable task runtime does not support signals.");
    //IScheduledTaskHandle OnCreateScheduledTaskHandle(IScheduledTaskHandle handle);
}
