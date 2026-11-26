using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Docs.Snippets.Persistence;

// <grain_storage_interface>
/// <summary>
/// 用于存储能够读写 Orleans grain 状态数据的存储,将实施接口。
/// </summary>
public interface ICustomGrainStorage
{
    /// <summary>读取此存储实例的数据函数。</summary>
    /// <param name="stateName">国家名称 grain</param>
    /// <param name="grainId">Grain ID 身份</param>
    /// <param name="grainState">为此要输入的州数据对象 grain.</param>
    /// <typeparam name="T"> grain 状态类型。</typeparam>
    /// <returns>特定 grain的读化操作的完成承诺。</returns>
    Task ReadStateAsync<T>(
        string stateName, GrainId grainId, IGrainState<T> grainState);

    /// <summary>为此存储实例写入数据函数 。</summary>
    /// <param name="stateName">国家名称 grain</param>
    /// <param name="grainId">Grain ID 身份</param>
    /// <param name="grainState">为此要写入的状态数据对象 grain.</param>
    /// <typeparam name="T"> grain 状态类型。</typeparam>
    /// <returns>指定写入操作的完成承诺 grain.</returns>
    Task WriteStateAsync<T>(
        string stateName, GrainId grainId, IGrainState<T> grainState);

    /// <summary>删除/清除此存储实例的数据函数。</summary>
    /// <param name="stateName">国家名称 grain</param>
    /// <param name="grainId">Grain ID 身份</param>
    /// <param name="grainState">本 grain的最后已知的状态数据对象副本。</param>
    /// <typeparam name="T"> grain 状态类型。</typeparam>
    /// <returns>特定 grain的删除操作的完成承诺。</returns>
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

    /// <summary>目前持续储存中的滞值。</summary>
    public string StoredEtag { get; }

    /// <summary>目前存储在记忆中的牌值, 并试图更新。</summary>
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
