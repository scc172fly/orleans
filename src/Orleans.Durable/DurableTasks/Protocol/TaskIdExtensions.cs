using Orleans.Durable.Tasks;
using MessagingHierarchicalKey = Orleans.Durable.Messaging.HierarchicalKey;

namespace Orleans;

/// <summary>
/// 在 <see cref="TaskId"/> 与 <see cref="MessagingHierarchicalKey"/> 之间转换的扩展方法。
/// </summary>
/// <remarks>
/// 这些扩展方法使 Orleans.Durable.Tasks 中的 <see cref="TaskId"/> 类型与
/// Orleans Messaging 中的 <see cref="MessagingHierarchicalKey"/> 类型能够互操作。
/// 两种类型具有相同的字符串表示形式和层次语义，因此可以
/// 通过字符串解析直接转换。
/// </remarks>
public static class TaskIdExtensions
{
    /// <summary>
    /// 将 <see cref="TaskId"/> 转换为 <see cref="MessagingHierarchicalKey"/>。
    /// </summary>
    /// <param name="taskId">要转换的 <see cref="TaskId"/>。</param>
    /// <returns>
    /// 一个 <see cref="MessagingHierarchicalKey"/>，与指定的 <see cref="TaskId"/> 等价，
    /// 或者返回 <c>null</c>（当 <see cref="TaskId"/> 为默认值时）。
    /// </returns>
    /// <example>
    /// <code>
    /// TaskId taskId = TaskId.Parse("workflow/task-123");
    /// Orleans.Durable.Messaging.HierarchicalKey? key = taskId.ToHierarchicalKey();
    /// // key.ToString() == "workflow/task-123"
    /// </code>
    /// </example>
    public static MessagingHierarchicalKey? ToHierarchicalKey(this TaskId taskId) =>
        taskId.IsDefault ? null : MessagingHierarchicalKey.Parse(taskId.ToString(), null);

    /// <summary>
    /// 将 <see cref="MessagingHierarchicalKey"/> 转换为 <see cref="TaskId"/>。
    /// </summary>
    /// <param name="key">要转换的 <see cref="MessagingHierarchicalKey"/>。</param>
    /// <returns>
    /// 一个 <see cref="TaskId"/>，与指定的 <see cref="MessagingHierarchicalKey"/> 等价，
    /// 或在键未设置时返回 <see cref="TaskId.None"/>。
    /// </returns>
    /// <example>
    /// <code>
    /// Orleans.Durable.Messaging.HierarchicalKey key =
    ///     Orleans.Durable.Messaging.HierarchicalKey.Parse("workflow/task-123", null);
    /// TaskId taskId = key.ToTaskId();
    /// // taskId.ToString() == "workflow/task-123"
    /// </code>
    /// </example>
    public static TaskId ToTaskId(this MessagingHierarchicalKey key) =>
        key.IsDefault ? TaskId.None : TaskId.Parse(key.ToString(), null);

    /// <summary>
    /// 将可为空的 <see cref="MessagingHierarchicalKey"/> 转换为 <see cref="TaskId"/>。
    /// </summary>
    /// <param name="key">要转换的 <see cref="MessagingHierarchicalKey"/>。</param>
    /// <returns>
    /// 一个 <see cref="TaskId"/>，与指定的 <see cref="MessagingHierarchicalKey"/> 等价，
    /// 或者返回 <see cref="TaskId.None"/>（键为 <c>null</c> 或未设置时）。
    /// </returns>
    public static TaskId ToTaskId(this MessagingHierarchicalKey? key) =>
        key is { } value ? value.ToTaskId() : TaskId.None;
}
