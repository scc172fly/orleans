using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Storage
{
    /// <summary>
    /// Grain 内部内存存储界面 grain 使用用 Orleans 模拟储存供应商。
    /// </summary>
    public interface IMemoryStorageGrain : IGrainWithIntegerKey
    {
        /// <summary>用于从存储存储处检索指定 grain 状态数据的同步方法。</summary>
        /// <param name="grainStoreKey">存储此密钥 grain。</param>
        /// <returns>目前储存的保证价值 grain 指定状态 grain.</returns>
        [Alias("ReadStateAsync")]
        Task<IGrainState<T>?> ReadStateAsync<T>(string grainStoreKey);

        [Alias("45659318")]
        Task<IGrainState<T>?> ReadStateAsync<T>(string grainStoreKey, CancellationToken cancellationToken)
            => ReadStateAsync<T>(grainStoreKey);

        /// <summary>将指定 grain 状态数据更新到内存存储处的同步方法。</summary>
        /// <param name="grainStoreKey">Grain ID.</param>
        /// <param name="grainState">为此存储新的状态数据 grain.</param>
        /// <returns>附有新的完成承诺 eTag 用于更新存储的操作 grain 指定状态 grain.</returns>
        [Alias("WriteStateAsync")]
        Task<string> WriteStateAsync<T>(string grainStoreKey, IGrainState<T> grainState);

        [Alias("7CC6CA25")]
        Task<string> WriteStateAsync<T>(
            string grainStoreKey,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
            => WriteStateAsync(grainStoreKey, grainState);

        /// <param name="grainStoreKey">存储此密钥 grain。</param>
        /// <param name="eTag">上一个已读取的标签。</param>
        /// <returns>储存的更新作业完成承诺 grain 指定状态 grain.</returns>
        [Alias("DeleteStateAsync")]
        Task DeleteStateAsync<T>(string grainStoreKey, string? eTag);

        [Alias("B7CADD03")]
        Task DeleteStateAsync<T>(string grainStoreKey, string? eTag, CancellationToken cancellationToken)
            => DeleteStateAsync<T>(grainStoreKey, eTag);
    }
}
