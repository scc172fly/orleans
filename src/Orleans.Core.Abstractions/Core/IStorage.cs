using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Core
{
    /// <summary>
    /// 提供在 grain 储存上操作的方法。
    /// </summary>
    public interface IStorage
    {
        /// <summary>
        /// 得到ETAG。
        /// </summary>
        /// <remarks>
        /// ETag或实体标签是一种价值,用于防止在其中一个或多个作者没有首先看到最近操作的情况下同时写作。
        /// </remarks>
        string? Etag { get; }

        /// <summary>
        /// 获得一个值,以显示记录是否已经存在。
        /// </summary>
        bool RecordExists { get; }

        /// <summary>
        /// 清除 grain 状态。
        /// </summary>
        /// <remarks>
        /// 这通常意味着国家记录从备份存储处删除, 但具体的行为由为此配置的存储提供者实例来定义 grain。
        /// 如果 Etag 与支持存储中存在的内容不符, 此操作将失败; 设置 <see cref="Etag"/> 到 <see langword="null"/> 以表示“ 总是删除 ”。
        /// </remarks>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task ClearStateAsync();

        /// <summary>
        /// 将 grain 状态写入存储状态。
        /// </summary>
        /// <remarks>
        /// 如果 Etag 与支持存储中存在的内容不符, 此操作将失败; 设置 <see cref="Etag"/> 到 <see langword="null"/> 以表示“ 总是删除 ”。
        /// </remarks>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task WriteStateAsync();

        /// <summary>
        /// 从存储中读取 grain 状态。
        /// </summary>
        /// <remarks>
        /// grain 状态数据的任何先前内容将被覆盖。
        /// </remarks>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task ReadStateAsync();

        /// <summary>
        /// 清除 grain 状态。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <remarks>
        /// 这通常意味着国家记录从备份存储处删除, 但具体的行为由为此配置的存储提供者实例来定义 grain。
        /// 如果 Etag 与支持存储中存在的内容不符, 此操作将失败; 设置 <see cref="Etag"/> 到 <see langword="null"/> 以表示“ 总是删除 ”。
        /// </remarks>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();

        /// <summary>
        /// 将 grain 状态写入存储状态。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <remarks>
        /// 如果 Etag 与支持存储中存在的内容不符, 此操作将失败; 设置 <see cref="Etag"/> 到 <see langword="null"/> 以表示“ 总是删除 ”。
        /// </remarks>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();

        /// <summary>
        /// 从存储中读取 grain 状态。
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <remarks>
        /// grain 状态数据的任何先前内容将被覆盖。
        /// </remarks>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();
    }

    /// <summary>
    /// 提供在 grain 状态上运行的方法。
    /// </summary>
    public interface IStorage<TState> : IStorage
    {
        /// <summary>
        /// 建立州立或控制
        /// </summary>
        TState State { get; set; }
    }
}
