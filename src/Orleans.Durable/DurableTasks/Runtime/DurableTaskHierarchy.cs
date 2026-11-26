using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 将存储任务的身份识别特征按等级顺序索引,这样就发现任务中最接近的存储子孙
/// 扫描每个任务
/// </summary>
/// <remarks>
/// 储存的或存有后代的所有识别资料都与父母的子女组合联系在一起。
/// 任务与最接近存储的子孙之间,例如组合器操作识别符号之间,有链接
/// 增加或删除任务成本,其时间与深度成比例,指数有一个孩子
/// 与相关儿童一起设定每个标识符。
/// </remarks>
internal sealed class DurableTaskHierarchy
{
    private readonly Dictionary<TaskId, HashSet<TaskId>> _children = [];

    /// <summary>创建 <paramref name="taskIds"/>的索引。</summary>
    public static DurableTaskHierarchy Create(IEnumerable<TaskId> taskIds)
    {
        var result = new DurableTaskHierarchy();
        foreach (var taskId in taskIds)
        {
            result.Add(taskId);
        }

        return result;
    }

    /// <summary>链接新存储的任务。</summary>
    public void Add(TaskId taskId)
    {
        for (var child = taskId; ;)
        {
            var parent = child.Parent();
            if (parent.IsDefault)
            {
                return;
            }

            // 与相关儿童的身份识别符本身就相连,所以步行结束于第一个。
            ref var children = ref CollectionsMarshal.GetValueRefOrAddDefault(_children, parent, out var parentLinked);
            children ??= [];
            if (!children.Add(child) || parentLinked)
            {
                return;
            }

            child = parent;
        }
    }

    /// <summary>取消不再储存的任务的链接,在它存留后代时保持其连接。</summary>
    /// <param name="taskId">任务已删除。</param>
    /// <param name="isStored">返回是否存储了标识符。</param>
    public void Remove(TaskId taskId, Func<TaskId, bool> isStored)
    {
        for (var child = taskId; !_children.ContainsKey(child);)
        {
            var parent = child.Parent();
            if (parent.IsDefault)
            {
                return;
            }

            var found = _children.TryGetValue(parent, out var children);
            Debug.Assert(found && children!.Contains(child), "Every stored task is linked into its parent's child set.");
            children!.Remove(child);
            if (children.Count > 0)
            {
                return;
            }

            _children.Remove(parent);
            if (isStored(parent))
            {
                return;
            }

            child = parent;
        }
    }

    /// <summary>删除每一项任务。</summary>
    public void Clear() => _children.Clear();

    /// <summary>获取储藏最接近的祖先 <paramref name="taskId"/>的存储任务。</summary>
    /// <param name="taskId">祖先的传说</param>
    /// <param name="isStored">返回是否存储了标识符。</param>
    public IEnumerable<TaskId> GetNearestStoredDescendants(TaskId taskId, Func<TaskId, bool> isStored)
    {
        if (!_children.TryGetValue(taskId, out var children))
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (isStored(child))
            {
                yield return child;
                continue;
            }

            Debug.Assert(_children.ContainsKey(child), "An identifier which is not stored is linked only while it has stored descendants.");
            foreach (var descendant in GetNearestStoredDescendants(child, isStored))
            {
                yield return descendant;
            }
        }
    }
}
