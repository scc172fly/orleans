using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;
using Orleans.TestingHost.Diagnostics;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>等待持久任务运行时的过渡 <see cref="DiagnosticEventCollector"/> 捕获。</summary>
/// <remarks>在引起转换动作之前武装收藏家, 从而将事件记录下来 。</remarks>
internal static class DurableTaskEventBarriers
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>为耐久任务运行时间事件创建收藏家。</summary>
    public static DiagnosticEventCollector Collect() => new(DurableTaskEvents.ListenerName);

    /// <summary>等待 <paramref name="grainId"/>运行时间完成 <paramref name="taskId"/>的终端结果。</summary>
    public static async Task<DurableTaskResponse> WaitForCompletedAsync(this DiagnosticEventCollector events, GrainId grainId, TaskId taskId) =>
        ((DurableTaskEvents.TaskCompleted)(await Wait<DurableTaskEvents.TaskCompleted>(events, grainId, taskId)).Payload!).Response;

    /// <summary>等待 <paramref name="grainId"/>的运行时间阶段删除 <paramref name="taskId"/>。</summary>
    public static Task WaitForDeletedAsync(this DiagnosticEventCollector events, GrainId grainId, TaskId taskId) =>
        Wait<DurableTaskEvents.TaskDeleted>(events, grainId, taskId);

    /// <summary>
    /// 等待 <paramref name="calleeId"/>运行时创建它存储用于节点的任务
    /// <paramref name="nodeId"/> 减去 <paramref name="callerId"/> 引用,并返回该任务的身份。
    /// </summary>
    /// <remarks>
    /// 被调用者通过电线识别码将接头的节点存放在来电者的视野之下,该识别码覆盖了来电者的化身。
    /// 运行,所以测试找到运行使用的代号而不是衍生的代号。
    /// </remarks>
    public static async Task<TaskId> WaitForCallerChildAsync(this DiagnosticEventCollector events, GrainId calleeId, GrainId callerId, TaskId nodeId)
    {
        var created = await events.WaitForEventAsync(
            nameof(DurableTaskEvents.TaskCreated),
            evt => evt.Payload is DurableTaskEvents.TaskCreated payload
                && payload.GrainContext.GrainId == calleeId
                && IsCallerChild(callerId, nodeId, payload.TaskId),
            Timeout,
            TestContext.Current.CancellationToken);
        return ((DurableTaskEvents.TaskCreated)created.Payload!).TaskId;
    }

    /// <summary>
    /// 返回是否有一个调用商店 <paramref name="taskId"/> 节点 <paramref name="nodeId"/> 减去
    /// 在节点树的任何一根管子上都引用了<paramref name="callerId"/> 。
    /// </summary>
    public static bool IsCallerChild(GrainId callerId, TaskId nodeId, TaskId taskId) =>
        CallerScope.Root.Child(callerId.ToString()).IsAncestorOf(taskId)
        && CallerScope.TryParseWireId(CallerScope.GetCallerTaskId(callerId, taskId), out _, out var callerNodeId)
        && callerNodeId == nodeId;

    private static Task<DiagnosticEvent> Wait<TEvent>(DiagnosticEventCollector events, GrainId grainId, TaskId taskId)
        where TEvent : DurableTaskEvents.DurableTaskEvent =>
        events.WaitForEventAsync(
            typeof(TEvent).Name,
            evt => evt.Payload is TEvent payload && payload.GrainContext.GrainId == grainId && payload.TaskId == taskId,
            Timeout,
            TestContext.Current.CancellationToken);
}
