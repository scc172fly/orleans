using System;
using System.Diagnostics;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 映射 grains 在其他 grains 上援引的、其目标储存这些识别符号的任务,以及
/// 将 grain的处决树与其它 grains 地址的线条识别符相连接。
/// </summary>
/// <remarks>
/// <para>
/// 根任务标识符在删除早期运行后每次 grain 保存其引用时都指定新运行,并且
/// 每个运行的每个运行都有自己的化身, 是一个非。 一条信息, 命名执行树节点, 比如呼叫
/// 另外一个 grain 或订阅,用其有线识别码命名, <c>{incarnation:N}/{nodeId}</c>: the incarnation of
/// 结点树,以32十六进制数字格式格式,作为根段根段,然后是节点本身的段,例如
/// <c>9f3c…e1/place-order/reserve</c>。因此,每个运行者都发送自己的信息,而通信的重复则保持了这些信息
/// 除了前运行点之外, 命名前运行节点的回复会被确认为已老化。 线标识符
/// 是运行和节点的纯函数, 在一个运行中进行重播和再放映, 以达到相同的任务。
/// </para>
/// <para>
/// A grain 援引了另一个 grain的任务,即作为自己处决的儿童。根识别码是按 grain测算的,
/// 目标商店
/// 呼叫者的范围 <c>$caller/{callerGrainId}/{wireId}</c>,所以两个来电人, 两个跑的打一个, 从来没有分享
/// 状态,并绘制在边界的调用者识别特征地图:调用者使用的援引、取消和中止
/// 目标发送的完成时 将线识别号命名为
/// 用户。 由独立启动发送的根标识符本身会处理目标的根任务 。
/// </para>
/// <para>
/// 范围根 <see cref="Root"/>被保留:只有从其它 grains 中引用来创建任务, 所以没有
/// 与范围任务共享一个标识符。
/// 每项执行操作都保留其识别资料。
/// </para>
/// </remarks>
internal static class CallerScope
{
    // 化身的“ N” 格式: 32 个十六进制数字 。
    private const int IncarnationLength = 32;

    /// <summary>a 用于 grain 保存其他任务 grains 援引。</summary>
    public static TaskId Root { get; } = TaskId.CreateRoot("$caller");

    /// <summary>
    /// 返回运行的行树的节点 <paramref name="nodeId"/> 的线条识别符
    /// <paramref name="incarnation"/>: <c>{incarnation:N}/{nodeId}</c>.
    /// </summary>
    public static TaskId GetWireId(Guid incarnation, TaskId nodeId)
    {
        Debug.Assert(incarnation != Guid.Empty, "Every run of an execution tree has an incarnation.");
        Debug.Assert(!nodeId.IsDefault, "A wire identifier names a node.");
        return TaskId.Parse($"{incarnation:N}/{nodeId}");
    }

    /// <summary>
    /// 分割一个 <see cref="GetWireId"/> 格式化成其运行的化身和节点的线条识别符。
    /// </summary>
    /// <returns><see langword="false"/> 何时 <paramref name="wireId"/> 并不是一个线条识别器。</returns>
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
    /// 返回任务 <paramref name="taskId"/> 的目标标识符,该任务是来自
    /// <paramref name="caller"/> 地址:目标的根任务本身,或打电话者的孩子,其线路识别码
    /// <paramref name="taskId"/> 属于呼叫者的范围。
    /// </summary>
    public static TaskId Resolve(GrainId caller, TaskId taskId) =>
        taskId.Parent().IsDefault ? taskId : GetTaskId(caller, taskId);

    /// <summary>
    /// 返回一个目标存储孩子的识别符,该目标存储的孩子被 <paramref name="caller"/> 引用,其电线
    /// 身份识别符号是 <paramref name="callerTaskId"/>。
    /// </summary>
    public static TaskId GetTaskId(GrainId caller, TaskId callerTaskId)
    {
        Debug.Assert(!caller.IsDefault, "A scoped task has a calling grain.");
        Debug.Assert(!callerTaskId.Parent().IsDefault, "A grain invokes another grain's task as a node of one of its execution trees.");
        return TaskId.Parse($"{GetPrefix(caller)}{callerTaskId}");
    }

    /// <summary>
    /// 返回哪个身份识别符 <paramref name="caller"/> 任务所在地址 <paramref name="taskId"/> 中,该列所
    /// 儿童被援引:儿童的电线识别特征,或 <paramref name="taskId"/> 何时 <paramref name="caller"/> 并没有援引它
    /// 范围范围内的。
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
    /// 当 <paramref name="taskId"/> 是调用者范围或下方的预留根时投出,而该根仅是
    /// 其它 grains 创建的引用 。
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
