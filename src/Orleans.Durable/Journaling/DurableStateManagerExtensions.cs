namespace Orleans.Durable.Journaling;

/// <summary>
/// 方便地获取内在的耐久国家合同。
/// </summary>
public static class DurableStateManagerExtensions
{
    /// <summary>获取或创建名为耐久字典。</summary>
    /// <typeparam name="TKey">字典键类型。</typeparam>
    /// <typeparam name="TValue">字典值类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>名词典实例。</returns>
    public static IDurableDictionary<TKey, TValue> GetOrAddDictionary<TKey, TValue>(this IDurableStateManager manager, string name)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableDictionary<TKey, TValue>>(name);
    }

    /// <summary>获取或创建一个名为耐久列表。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>命名列表实例。</returns>
    public static IDurableList<T> GetOrAddList<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableList<T>>(name);
    }

    /// <summary>获取或创建名为耐久性队列。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>命名的队列实例。</returns>
    public static IDurableQueue<T> GetOrAddQueue<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableQueue<T>>(name);
    }

    /// <summary>获取或创建一个命名的耐久性套件。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>命名的设置实例。</returns>
    public static IDurableSet<T> GetOrAddSet<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableSet<T>>(name);
    }

    /// <summary>获取或创建一个命名的耐久日志。</summary>
    /// <typeparam name="T">条目类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>命名的日志实例。</returns>
    public static IDurableLog<T> GetOrAddLog<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableLog<T>>(name);
    }

    /// <summary>获取或创建一个命名的耐久值。</summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>命名值实例。</returns>
    public static IDurableValue<T> GetOrAddValue<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableValue<T>>(name);
    }

    /// <summary>获取或创建一个命名的持久任务完成源。</summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>指定的任务完成源实例。</returns>
    public static IDurableTaskCompletionSource<T> GetOrAddTaskCompletionSource<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IDurableTaskCompletionSource<T>>(name);
    }

    /// <summary>获取或创建名为日记支持的持久性状态。</summary>
    /// <typeparam name="T">持续值类型。</typeparam>
    /// <param name="manager">拥有权管理器。</param>
    /// <param name="name">稳定状态名称。</param>
    /// <returns>被命名为持续州立法院</returns>
    public static IPersistentState<T> GetOrAddPersistentState<T>(this IDurableStateManager manager, string name)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetOrAddState<IPersistentState<T>>(name);
    }
}
