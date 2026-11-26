using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Tasks.Protocol;

internal interface IDurableTaskGrainStorage
{
    /// <summary>获取储藏最接近的祖先 <paramref name="task"/>的存储任务。</summary>
    /// <remarks>
    /// 储存的任务可以通过自己不存储的识别符号从 <paramref name="task"/> 降下来,这些识别标志本身不储存。
    /// 存储存储索引等级, 因此成本与
    /// 返回任务而不是每个存储的任务。
    /// </remarks>
    IEnumerable<(TaskId Id, IDurableTaskState State)> GetChildren(TaskId task);

    IEnumerable<(TaskId Id, IDurableTaskState State)> Tasks { get; }

    /// <summary>
    /// 返回 <paramref name="taskId"/>状态,或存储 <paramref name="kind"/> 的新记录
    /// <paramref name="request"/> 当任务不储存的时候
    /// </summary>
    /// <remarks>储存的任务保留了同类和要求。</remarks>
    IDurableTaskState GetOrCreateTask(TaskId taskId, DurableTaskRecordKind kind, IDurableTaskRequest? request);
    void SetRequest(TaskId taskId, IDurableTaskState state, IDurableTaskRequest request);

    /// <summary>
    /// 记录任务 <see cref="IDurableTaskState.RequestFingerprint"/> ,并记录援引某一方法的记录,
    /// 其 其 其 年 其 其 其 其 其 其 <see cref="IDurableTaskState.Method"/>.
    /// </summary>
    void SetRequestFingerprint(TaskId taskId, IDurableTaskState state, string fingerprint, string? method = null);

    /// <summary>
    /// 记录任务对 <paramref name="target"/>的呼唤:援引的 <paramref name="method"/>和诊断
    /// 电话要求的<paramref name="fingerprint"/>
    /// </summary>
    void SetRemoteRequest(TaskId taskId, IDurableTaskState state, GrainId target, string method, string fingerprint);
    void SetCallerId(TaskId taskId, IDurableTaskState state, GrainId callerId);

    /// <summary>
    /// 记录 <see cref="DurableTaskRecordKind.EarlyCancellation"/> 记录 <paramref name="taskId"/> 是
    /// <see cref="DurableTaskRecordKind.Invocation"/> 在那日,他们将被送到后方。
    /// </summary>
    void SetInvocation(TaskId taskId, IDurableTaskState state);

    /// <summary>记录被援引任务运行过程的化身</summary>
    void SetIncarnation(TaskId taskId, IDurableTaskState state, System.Guid incarnation);

    /// <summary>
    /// 存储任务终端的仓库 <paramref name="response"/>,标记其完成时间和下一个
    /// <see cref="IDurableTaskState.CompletionSequence"/>.
    /// </summary>
    void SetResponse(TaskId taskId, IDurableTaskState state, DurableTaskResponse response);

    /// <summary>
    /// 储存新的终端记录 <paramref name="taskId"/> 执行中执行的, <paramref name="kind"/>,例如
    /// 以 <paramref name="fingerprint"/>完成的版本决定
    /// <paramref name="response"/>.
    /// </summary>
    /// <remarks>
    /// 记录存储于 <see cref="GetOrCreateTask"/>, <see cref="SetRequestFingerprint"/>,和
    /// <see cref="SetResponse"/> 存储它。 一个存储器, 它在一次更新中将完整记录进行分级, 而不是此 。
    /// </remarks>
    /// <exception cref="Exception">反应无法存储, 例如因为结果没有连环化; 没有储存任何信息。</exception>
    IDurableTaskState AddRecord(TaskId taskId, DurableTaskRecordKind kind, string fingerprint, DurableTaskResponse response)
    {
        var state = GetOrCreateTask(taskId, kind, request: null);
        SetRequestFingerprint(taskId, state, fingerprint);
        SetResponse(taskId, state, response);
        return state;
    }

    /// <summary>如有记录表明请求取消一项非终止性任务,如有说明理由的话。</summary>
    /// <remarks>第一项请求确定原因:取消或终止的任务没有变化。</remarks>
    void RequestCancellation(TaskId taskId, IDurableTaskState state, string? reason = null);

    /// <summary>任务被保护的记录: 其祖先的持久取消没有达到。</summary>
    void Shield(TaskId taskId, IDurableTaskState state);

    /// <summary>
    /// 记录本记录开始的任务所在主机是否接受起始还是拒绝。
    /// </summary>
    void SetAccepted(TaskId taskId, IDurableTaskState state, bool accepted);
    void SetDelay(TaskId taskId, IDurableTaskState state, DateTimeOffset dueTime, long generation);

    void AddCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination);
    void RemoveCompletionDestination(TaskId taskId, IDurableTaskState state, DurableTaskSubscriber destination);

    /// <summary>恢复终端结果的留存期,现在。</summary>
    void RetainResult(TaskId taskId, IDurableTaskState state);

    bool TryGetTask(TaskId taskId, [NotNullWhen(true)] out IDurableTaskState? state);

    /// <summary>删除任务及其状态。</summary>
    /// <remarks>这项任务没有完成目的地:每个待完成的任务都保留了任务结果。</remarks>
    bool RemoveTask(TaskId taskId);
    void Clear();

    ValueTask WriteAsync(CancellationToken cancellationToken);
    ValueTask ReadAsync(CancellationToken cancellationToken);
}
