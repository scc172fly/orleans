using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Core
{
    /// <summary>
    /// 提供操作 Grain 存储的方法。
    /// </summary>
    public interface IStorage
    {
        /// <summary>
        /// 获取 ETag。
        /// </summary>
        /// <remarks>
        /// ETag（实体标记）是用于阻止此类并发写入的值：其中一个或多个写入操作尚未先观察到最新操作。
        /// </remarks>
        string? Etag { get; }

        /// <summary>
        /// 获取一个值，指示记录是否已存在。
        /// </summary>
        bool RecordExists { get; }

        /// <summary>
        /// 清除 Grain 状态。
        /// </summary>
        /// <remarks>
        /// 通常意味着从后端存储删除状态记录，但具体行为由为此 Grain 配置的存储提供程序实例决定。
        /// 如果 ETag 与后端存储中的值不匹配，则操作将失败；将 <see cref="Etag"/> 设置为 <see langword="null"/> 表示“始终删除”。
        /// </remarks>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task ClearStateAsync();

        /// <summary>
        /// 将 Grain 状态写入存储。
        /// </summary>
        /// <remarks>
        /// 如果 ETag 与后端存储中的值不匹配，则操作将失败；将 <see cref="Etag"/> 设置为 <see langword="null"/> 表示“始终删除”。
        /// </remarks>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task WriteStateAsync();

        /// <summary>
        /// 从存储读取 Grain 状态。
        /// </summary>
        /// <remarks>
        /// Grain 状态数据原有的所有内容都将被覆盖。
        /// </remarks>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task ReadStateAsync();

        /// <summary>
        /// 清除 Grain 状态。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <remarks>
        /// 通常意味着从后端存储删除状态记录，但具体行为由为此 Grain 配置的存储提供程序实例决定。
        /// 如果 ETag 与后端存储中的值不匹配，则操作将失败；将 <see cref="Etag"/> 设置为 <see langword="null"/> 表示“始终删除”。
        /// </remarks>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();

        /// <summary>
        /// 将 Grain 状态写入存储。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <remarks>
        /// 如果 ETag 与后端存储中的值不匹配，则操作将失败；将 <see cref="Etag"/> 设置为 <see langword="null"/> 表示“始终删除”。
        /// </remarks>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();

        /// <summary>
        /// 从存储读取 Grain 状态。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <remarks>
        /// Grain 状态数据原有的所有内容都将被覆盖。
        /// </remarks>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();
    }

    /// <summary>
    /// 提供操作 Grain 状态的方法。
    /// </summary>
    public interface IStorage<TState> : IStorage
    {
        /// <summary>
        /// 获取或设置状态。
        /// </summary>
        TState State { get; set; }
    }
}
