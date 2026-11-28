namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供对内置持久化状态契约的便捷访问。
/// </summary>
public static class DurableStateManagerExtensions
{
    /// <summary>获取或创建命名的持久化字典。</summary>
    /// <typeparam name="TKey">字典键的类型。</typeparam>
    /// <typeparam name="TValue">字典值的类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名字典实例。</returns>
    public static IDurableDictionary<TKey, TValue> GetOrAddDictionary<TKey, TValue>(this IDurableStateManager manager, string name)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableDictionary<TKey, TValue>>(name);
    }

    /// <summary>获取或创建命名的持久化列表。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名列表实例。</returns>
    public static IDurableList<T> GetOrAddList<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableList<T>>(name);
    }

    /// <summary>获取或创建命名的持久化队列。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名队列实例。</returns>
    public static IDurableQueue<T> GetOrAddQueue<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableQueue<T>>(name);
    }

    /// <summary>获取或创建命名的持久化集合。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名集合实例。</returns>
    public static IDurableSet<T> GetOrAddSet<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableSet<T>>(name);
    }

    /// <summary>获取或创建命名的持久化日志。</summary>
    /// <typeparam name="T">条目类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名日志实例。</returns>
    public static IDurableLog<T> GetOrAddLog<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableLog<T>>(name);
    }

    /// <summary>获取或创建命名的持久化值。</summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名值实例。</returns>
    public static IDurableValue<T> GetOrAddValue<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableValue<T>>(name);
    }

    /// <summary>获取或创建命名的持久化任务完成源。</summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名任务完成源实例。</returns>
    public static IDurableTaskCompletionSource<T> GetOrAddTaskCompletionSource<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableTaskCompletionSource<T>>(name);
    }

    /// <summary>获取或创建命名的基于日志的持久化状态。</summary>
    /// <typeparam name="T">持久化值的类型。</typeparam>
    /// <param name="manager">所属管理器。</param>
    /// <param name="name">稳定的状态名称。</param>
    /// <returns>命名持久化状态实例。</returns>
    public static IPersistentState<T> GetOrAddPersistentState<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IPersistentState<T>>(name);
    }
}
