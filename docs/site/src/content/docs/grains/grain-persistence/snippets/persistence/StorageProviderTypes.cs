using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Docs.Snippets.Persistence;

// <grain_storage_interface>
/// <summary>
/// 能够读写 Orleans Grain 状态数据的存储必须实现此接口。
/// </summary>
public interface ICustomGrainStorage
{
    /// <summary>此存储实例的数据读取方法。</summary>
    /// <param name="stateName">此 Grain 的状态名称。</param>
    /// <param name="grainId">Grain ID。</param>
    /// <param name="grainState">用于接收读取到的此 Grain 状态数据的对象。</param>
    /// <typeparam name="T">Grain 状态类型。</typeparam>
    /// <returns>表示指定 Grain 读取操作完成的 异步任务。</returns>
    Task ReadStateAsync<T>(
        string stateName, GrainId grainId, IGrainState<T> grainState);

    /// <summary>此存储实例的数据写入方法。</summary>
    /// <param name="stateName">此 Grain 的状态名称。</param>
    /// <param name="grainId">Grain ID。</param>
    /// <param name="grainState">要为此 Grain 写入的状态数据对象。</param>
    /// <typeparam name="T">Grain 状态类型。</typeparam>
    /// <returns>表示指定 Grain 写入操作完成的 异步任务。</returns>
    Task WriteStateAsync<T>(
        string stateName, GrainId grainId, IGrainState<T> grainState);

    /// <summary>此存储实例的数据删除或清除方法。</summary>
    /// <param name="stateName">此 Grain 的状态名称。</param>
    /// <param name="grainId">Grain ID。</param>
    /// <param name="grainState">此 Grain 最后一次已知的状态数据对象的副本。</param>
    /// <typeparam name="T">Grain 状态类型。</typeparam>
    /// <returns>表示指定 Grain 删除操作完成的 异步任务。</returns>
    Task ClearStateAsync<T>(
        string stateName, GrainId grainId, IGrainState<T> grainState);
}
// </grain_storage_interface>

// <inconsistent_state_exception>
public class InconsistentStateException : OrleansException
{
    public InconsistentStateException(
    string message,
    string storedEtag,
    string currentEtag,
    Exception storageException)
        : base(message, storageException)
    {
        StoredEtag = storedEtag;
        CurrentEtag = currentEtag;
    }

    public InconsistentStateException(
        string storedEtag,
        string currentEtag,
        Exception storageException)
        : this(storageException.Message, storedEtag, currentEtag, storageException)
    {
    }

    /// <summary>当前保存在持久化存储中的 Etag 值。</summary>
    public string StoredEtag { get; }

    /// <summary>当前保存在内存中并尝试更新的 Etag 值。</summary>
    public string CurrentEtag { get; }
}
// </inconsistent_state_exception>

// <grain_base_methods>
public abstract class GrainBaseExample<TState>
{
    protected virtual Task ReadStateAsync() { return Task.CompletedTask; }
    protected virtual Task WriteStateAsync() { return Task.CompletedTask; }
    protected virtual Task ClearStateAsync() { return Task.CompletedTask; }
}
// </grain_base_methods>
