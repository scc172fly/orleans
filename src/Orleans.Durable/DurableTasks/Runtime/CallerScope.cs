using System;
using System.Diagnostics;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 将 Grain 在其他 Grain 上调用的任务映射为目标存储这些任务时使用的标识符，并
/// 将 Grain 执行树中的节点映射为其他 Grain 用于寻址的传输标识符。
/// </summary>
/// <remarks>
/// <para>
/// 每当 Grain 删除先前的一次运行后重新存储同一根任务的调用时，根任务标识符都会标识一次新的运行，而且
/// 每次运行都有独立的运行世代标识，即一次性标识。标识执行树节点的消息，例如对
/// 其他 Grain 的调用或订阅，以其传输标识符 <c>{incarnation:N}/{nodeId}</c> 标识节点：其中
/// 运行世代标识作为根段，格式为 32 个十六进制数字，之后跟随节点本身的各段，例如
/// <c>9f3c…e1/place-order/reserve</c>。因此，每次运行发送各自的消息，Messaging 的去重机制将它们与
/// 先前运行的消息区分开来，指定先前运行节点的回复则被识别为过时消息。传输标识符
/// 是运行和节点的纯函数，因此同一次运行中的重放与重新投递会访问同一个任务。
/// </para>
/// <para>
/// Grain 将其他 Grain 的任务作为自身某一执行实例的子任务调用。根标识按 Grain 划分作用域，
/// 因此两个调用方可以在同一目标上调用具有相同标识的子任务。目标将各子任务存储在其
/// 调用方作用域 <c>$caller/{callerGrainId}/{wireId}</c> 下，因此两个调用方或同一调用方的两次运行绝不共享
/// 状态。目标在边界处映射调用方标识：调用方发送的调用、取消及中止消息
/// 使用子任务的传输标识寻址，目标发送的完成消息以该传输标识作为
/// 订阅者标识。分离启动发送的根标识直接寻址目标的根任务。
/// </para>
/// <para>
/// 作用域的根 <see cref="Root"/> 是保留名称：只有来自其他 Grain 的调用才能在其下创建任务，因此
/// 其他任务不会与作用域内的任务使用相同的标识符。由客户端和分离启动创建的根任务，以及
/// 每个执行实例的操作均保留自身的标识符。
/// </para>
/// </remarks>
internal static class CallerScope
{
    // 运行世代标识的 "N" 格式：32 个十六进制数字。
    private const int IncarnationLength = 32;

    /// <summary>Grain 用于存储其他 Grain 所调用任务的保留根。</summary>
    public static TaskId Root { get; } = TaskId.CreateRoot("$caller");

    /// <summary>
    /// 返回执行树的节点 <paramref name="nodeId"/> 的传输标识符，该执行树的运行世代标识为
    /// <paramref name="incarnation"/>：<c>{incarnation:N}/{nodeId}</c>。
    /// </summary>
    public static TaskId GetWireId(Guid incarnation, TaskId nodeId)
    {
        Debug.Assert(incarnation != Guid.Empty, "Every run of an execution tree has an incarnation.");
        Debug.Assert(!nodeId.IsDefault, "A wire identifier names a node.");
        return TaskId.Parse($"{incarnation:N}/{nodeId}");
    }

    /// <summary>
    /// 将 <see cref="GetWireId"/> 格式化的传输标识符解析为其运行世代标识和节点。
    /// </summary>
    /// <returns><see langword="false"/> 表示 <paramref name="wireId"/> 不是传输标识符。</returns>
    public static bool TryParseWireId(TaskId wireId, out Guid incarnation, out TaskId nodeId)
    {
        incarnation = default;
        nodeId = default;
        var id = wireId.ToString();
        return id.Length > IncarnationLength + 1
            && id[IncarnationLength] == '/'
            && Guid.TryParseExact(id.AsSpan(0, IncarnationLength), "N", out incarnation)
            && TaskId.TryParse(id.AsSpan(IncarnationLength + 1), out nodeId);
    }

    /// <summary>
    /// 返回任务 <paramref name="taskId"/> 在目标上的标识符，该任务由
    /// <paramref name="caller"/> 发出的消息寻址：可能是目标自身的根任务，也可能是调用方的子任务，后者的传输标识符为
    /// <paramref name="taskId"/>，并位于调用方的作用域下。
    /// </summary>
    public static TaskId Resolve(GrainId caller, TaskId taskId) =>
        taskId.Parent().IsDefault ? taskId : GetTaskId(caller, taskId);

    /// <summary>
    /// 返回目标用于存储 <paramref name="caller"/> 所调用子任务的标识符，该子任务的传输
    /// 标识符为 <paramref name="callerTaskId"/>。
    /// </summary>
    public static TaskId GetTaskId(GrainId caller, TaskId callerTaskId)
    {
        Debug.Assert(!caller.IsDefault, "A scoped task has a calling grain.");
        Debug.Assert(!callerTaskId.Parent().IsDefault, "A grain invokes another grain's task as a node of one of its execution trees.");
        return TaskId.Parse($"{GetPrefix(caller)}{callerTaskId}");
    }

    /// <summary>
    /// 返回 <paramref name="caller"/> 寻址其已调用的任务 <paramref name="taskId"/> 时使用的标识符：
    /// 即其子任务的传输标识符；如果 <paramref name="taskId"/> 并非由 <paramref name="caller"/> 在
    /// 其作用域下调用的任务，则使用任务自身的标识符。
    /// </summary>
    public static TaskId GetCallerTaskId(GrainId caller, TaskId taskId)
    {
        if (caller.IsDefault)
        {
            return taskId;
        }

        var prefix = GetPrefix(caller);
        var id = taskId.ToString();
        return id.StartsWith(prefix, StringComparison.Ordinal) ? TaskId.Parse(id.AsSpan(prefix.Length)) : taskId;
    }

    /// <summary>
    /// 如果 <paramref name="taskId"/> 是调用方作用域的保留根或位于其下，则抛出异常；这些任务只能由
    /// 来自其他 Grain 的调用创建。
    /// </summary>
    public static void ThrowIfReserved(TaskId taskId)
    {
        if (Root.IsAncestorOf(taskId))
        {
            throw new ArgumentException(
                $"Task ID '{taskId}' is reserved: the root task ID '{Root}' holds the tasks which other grains invoke.",
                nameof(taskId));
        }
    }

    private static string GetPrefix(GrainId caller) => $"{Root.Child(caller.ToString())}/";
}
