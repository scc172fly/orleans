using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Tasks.Protocol;

internal interface IDurableTaskGrainStorage
{
    /// <summary>获取最近的已存储祖先为 <paramref name="task"/> 的已存储任务。</summary>
    /// <remarks>
    /// 已存储的任务可能通过本身未存储的标识符成为 <paramref name="task"/> 的后代，例如
    /// 通过组合器的操作标识符。存储对层次结构建立索引，因此操作开销与
    /// 返回的任务数成正比，而不是与所有已存储的任务数成正比。
    /// </remarks>
    IEnumerable<(TaskId Id, IDurableTaskState State)> GetChildren(TaskId task);

    IEnumerable<(TaskId Id, IDurableTaskState State)> Tasks { get; }

    /// <summary>
    /// 返回 <paramref name="taskId"/> 的状态；若任务尚未存储，则存储类型为 <paramref name="kind"/>、
    /// 携带 <paramref name="request"/> 的新记录。
    /// </summary>
    /// <remarks>已存储的任务保留其类型及请求。</remarks>
    IDurableTaskState GetOrCreateTask(TaskId taskId, DurableTaskRecordKind kind, IDurableTaskRequest? request);
    void SetRequest(TaskId taskId, IDurableTaskState state, IDurableTaskRequest request);

    /// <summary>
    /// 记录任务的 <see cref="IDurableTaskState.RequestFingerprint"/>；如果记录调用了方法，则还记录
    /// 它的 <see cref="IDurableTaskState.Method"/>。
    /// </summary>
    void SetRequestFingerprint(TaskId taskId, IDurableTaskState state, string fingerprint, string? method = null);

    /// <summary>
    /// 记录任务对 <paramref name="target"/> 的调用：被调用的 <paramref name="method"/> 及调用请求的诊断
    /// <paramref name="fingerprint"/>。
    /// </summary>
    void SetRemoteRequest(TaskId taskId, IDurableTaskState state, GrainId target, string method, string fingerprint);
    void SetCallerId(TaskId taskId, IDurableTaskState state, GrainId callerId);

    /// <summary>
    /// 记录 <see cref="DurableTaskRecordKind.EarlyCancellation"/> 记录 <paramref name="taskId"/> 对应于
    /// 其后到达的 <see cref="DurableTaskRecordKind.Invocation"/>。
    /// </summary>
    void SetInvocation(TaskId taskId, IDurableTaskState state);

    /// <summary>记录被调用任务本次运行的世代标识。</summary>
    void SetIncarnation(TaskId taskId, IDurableTaskState state, System.Guid incarnation);

    /// <summary>
    /// 存储任务的终态 <paramref name="response"/>，标记其完成时间及下一个
    /// <see cref="IDurableTaskState.CompletionSequence"/>.
    /// </summary>
    void SetResponse(TaskId taskId, IDurableTaskState state, DurableTaskResponse response);

    /// <summary>
    /// 存储执行实例的新终态记录 <paramref name="taskId"/>，其类型为 <paramref name="kind"/>（例如
    /// 已记录的效果或版本决策），携带 <paramref name="fingerprint"/>，其完成结果为
    /// <paramref name="response"/>.
    /// </summary>
    /// <remarks>
    /// 该记录按 <see cref="GetOrCreateTask"/>、<see cref="SetRequestFingerprint"/> 和
    /// <see cref="SetResponse"/> 的方式存储。能够在一次更新中暂存完整记录的存储应重写此方法。
    /// </remarks>
    /// <exception cref="Exception">无法存储响应（例如结果无法序列化）；不存储任何内容。</exception>
    IDurableTaskState AddRecord(TaskId taskId, DurableTaskRecordKind kind, string fingerprint, DurableTaskResponse response)
    {
        var state = GetOrCreateTask(taskId, kind, request: null);
        SetRequestFingerprint(taskId, state, fingerprint);
        SetResponse(taskId, state, response);
        return state;
    }

    /// <summary>记录对非终态任务的取消请求，并记录请求的原因（若有）。</summary>
    /// <remarks>第一次请求决定原因；已记录取消请求或已处于终态的任务保持不变。</remarks>
    void RequestCancellation(TaskId taskId, IDurableTaskState state, string? reason = null);

    /// <summary>记录任务受到屏蔽保护：其祖先的持久化取消不会传播到它。</summary>
    void Shield(TaskId taskId, IDurableTaskState state);

    /// <summary>
    /// 记录此记录所启动的任务的宿主是否接受了启动请求，或拒绝了该请求。
    /// </summary>
    void SetAccepted(TaskId taskId, IDurableTaskState state, bool accepted);
    void SetDelay(TaskId taskId, IDurableTaskState state, DateTimeOffset dueTime, long generation);

    void AddCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination);
    void RemoveCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination);

    /// <summary>从现在起重新计算终态结果的保留期。</summary>
    void RetainResult(TaskId taskId, IDurableTaskState state);

    bool TryGetTask(TaskId taskId, [NotNullWhen(true)] out IDurableTaskState? state);

    /// <summary>删除任务及其状态。</summary>
    /// <remarks>任务没有完成通知目的地：每个待处理的完成通知都会使其任务的结果得到保留。</remarks>
    bool RemoveTask(TaskId taskId);
    void Clear();

    ValueTask WriteAsync(CancellationToken cancellationToken);
    ValueTask ReadAsync(CancellationToken cancellationToken);
}
