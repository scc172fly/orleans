using Orleans.Durable.Tasks;
using MessagingHierarchicalKey = Orleans.Durable.Messaging.HierarchicalKey;

namespace Orleans;

/// <summary>
/// 在 <see cref="TaskId"/> 和 <see cref="MessagingHierarchicalKey"/>之间转换的扩展方法。
/// </summary>
/// <remarks>
/// 这些扩展使 <see cref="TaskId"/> 类型之间能够互操作性,而从
/// Orleans.Durable.Tasks 和 <see cref="MessagingHierarchicalKey"/> 类型来自 Orleans 通讯
/// 这两种类型都具有相同的弦表示法和等级语义,使转换成为正变
/// 通过字符串解析直接。
/// </remarks>
public static class TaskIdExtensions
{
    /// <summary>
    /// 将 <see cref="TaskId"/> 转换为 <see cref="MessagingHierarchicalKey"/>。
    /// </summary>
    /// <param name="taskId">下 <see cref="TaskId"/> 要转换。</param>
    /// <returns>
    /// A <see cref="MessagingHierarchicalKey"/> 等同于指定 <see cref="TaskId"/>,
    /// 或 <c>null</c> 如果 <see cref="TaskId"/> 默认值。
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
    /// <param name="key">下 <see cref="MessagingHierarchicalKey"/> 要转换。</param>
    /// <returns>
    /// A <see cref="TaskId"/> 等同于指定 <see cref="MessagingHierarchicalKey"/>,
    /// 如果密钥未设定, 则 <see cref="TaskId.None"/> 。
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
    /// 将无效 <see cref="MessagingHierarchicalKey"/> 转换为 <see cref="TaskId"/>。
    /// </summary>
    /// <param name="key">下 <see cref="MessagingHierarchicalKey"/> 要转换。</param>
    /// <returns>
    /// A <see cref="TaskId"/> 等同于指定 <see cref="MessagingHierarchicalKey"/>,
    /// 或 <see cref="TaskId.None"/> 如果密钥是 <c>null</c> 或取消设置。
    /// </returns>
    public static TaskId ToTaskId(this MessagingHierarchicalKey? key) =>
        key is { } value ? value.ToTaskId() : TaskId.None;
}
