using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Diagnostics;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Runtime;
using Orleans.TestingHost.Diagnostics;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>等待 <see cref="DiagnosticEventCollector"/> 捕获的持久化任务运行时状态转换。</summary>
/// <remarks>在引发状态转换的操作之前启用收集器，以便捕获事件。</remarks>
internal static class DurableTaskEventBarriers
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>为持久化任务运行时事件创建收集器。</summary>
    public static DiagnosticEventCollector Collect() => new(DurableTaskEvents.ListenerName);

    /// <summary>等待 <paramref name="grainId"/> 的运行时暂存 <paramref name="taskId"/> 的终态结果。</summary>
    public static async Task<DurableTaskResponse> WaitForCompletedAsync(this DiagnosticEventCollector events, GrainId grainId, TaskId taskId) =>
        ((DurableTaskEvents.TaskCompleted)(await Wait<DurableTaskEvents.TaskCompleted>(events, grainId, taskId)).Payload!).Response;

    /// <summary>等待 <paramref name="grainId"/> 的运行时暂存 <paramref name="taskId"/> 的删除操作。</summary>
    public static Task WaitForDeletedAsync(this DiagnosticEventCollector events, GrainId grainId, TaskId taskId) =>
        Wait<DurableTaskEvents.TaskDeleted>(events, grainId, taskId);

    /// <summary>
    /// 等待 <paramref name="calleeId"/> 的运行时创建为节点
    /// <paramref name="nodeId"/>（由 <paramref name="callerId"/> 调用）存储的任务，并返回该任务的 ID。
    /// </summary>
    /// <remarks>
    /// 被调用方在调用方作用域下按传输 ID 存储节点；该 ID 包含调用方的运行世代标识，
    /// 因此测试查找本次运行所用的 ID，而不是自行派生 ID。
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
    /// 返回被调用方是否将 <paramref name="taskId"/> 作为节点 <paramref name="nodeId"/> 对应的任务存储，该节点由
    /// <paramref name="callerId"/> 调用，范围涵盖该节点所在执行树的任意一次运行。
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
