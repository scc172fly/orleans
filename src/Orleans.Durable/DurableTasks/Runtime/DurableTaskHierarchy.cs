using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 按层次结构对已存储任务的标识符建立索引，以便查找任务最近的已存储后代而无需
/// 扫描所有任务。
/// </summary>
/// <remarks>
/// 每个已存储或具有已存储后代的标识都链接到其父节点的子节点集合。位于任务
/// 与最近的已存储后代之间的标识（例如组合器的操作标识）会建立链接，
/// 但不会被存储。添加或删除任务的耗时与其深度成正比，索引为每个具有已链接子节点的
/// 标识保留一个子节点集合。
/// </remarks>
internal sealed class DurableTaskHierarchy
{
    private readonly Dictionary<TaskId, HashSet<TaskId>> _children = [];

    /// <summary>创建 <paramref name="taskIds"/> 的索引。</summary>
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

            // 拥有已链接子节点的标识符本身已链接，因此遍历在遇到第一个此类标识符时停止。
            ref var children = ref CollectionsMarshal.GetValueRefOrAddDefault(_children, parent, out var parentLinked);
            children ??= [];
            if (!children.Add(child) || parentLinked)
            {
                return;
            }

            child = parent;
        }
    }

    /// <summary>解除不再存储的任务的链接；若它仍有已存储的后代，则保持链接。</summary>
    /// <param name="taskId">被移除的任务。</param>
    /// <param name="isStored">返回标识符是否已存储。</param>
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

    /// <summary>移除所有任务。</summary>
    public void Clear() => _children.Clear();

    /// <summary>获取最近的已存储祖先为 <paramref name="taskId"/> 的已存储任务。</summary>
    /// <param name="taskId">祖先任务。</param>
    /// <param name="isStored">返回标识符是否已存储。</param>
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
