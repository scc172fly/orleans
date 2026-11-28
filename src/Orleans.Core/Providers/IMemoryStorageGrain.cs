using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Storage
{
    /// <summary>
    /// Orleans 内存存储提供程序使用的内部内存存储 Grain 接口。
    /// </summary>
    public interface IMemoryStorageGrain : IGrainWithIntegerKey
    {
        /// <summary>从内存存储中获取指定 Grain 状态数据的异步方法。</summary>
        /// <param name="grainStoreKey">此 Grain 的存储键。</param>
        /// <returns>表示指定 Grain 当前已存储状态的异步结果。</returns>
        [Alias("ReadStateAsync")]
        Task<IGrainState<T>?> ReadStateAsync<T>(string grainStoreKey);

        [Alias("45659318")]
        Task<IGrainState<T>?> ReadStateAsync<T>(string grainStoreKey, CancellationToken cancellationToken)
            => ReadStateAsync<T>(grainStoreKey);

        /// <summary>将指定 Grain 的状态数据更新到内存存储中的异步方法。</summary>
        /// <param name="grainStoreKey">Grain ID。</param>
        /// <param name="grainState">要为此 Grain 存储的新状态数据。</param>
        /// <returns>表示指定 Grain 的存储状态更新操作已完成、并返回新 ETag 的异步结果。</returns>
        [Alias("WriteStateAsync")]
        Task<string> WriteStateAsync<T>(string grainStoreKey, IGrainState<T> grainState);

        [Alias("7CC6CA25")]
        Task<string> WriteStateAsync<T>(
            string grainStoreKey,
            IGrainState<T> grainState,
            CancellationToken cancellationToken)
            => WriteStateAsync(grainStoreKey, grainState);

        /// <param name="grainStoreKey">此 Grain 的存储键。</param>
        /// <param name="eTag">上一次读取的 ETag。</param>
        /// <returns>表示指定 Grain 的存储状态更新操作完成的异步结果。</returns>
        [Alias("DeleteStateAsync")]
        Task DeleteStateAsync<T>(string grainStoreKey, string? eTag);

        [Alias("B7CADD03")]
        Task DeleteStateAsync<T>(string grainStoreKey, string? eTag, CancellationToken cancellationToken)
            => DeleteStateAsync<T>(grainStoreKey, eTag);
    }
}
