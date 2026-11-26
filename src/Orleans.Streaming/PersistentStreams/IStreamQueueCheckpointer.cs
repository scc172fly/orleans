using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 创建 <see cref="IStreamQueueCheckpointer{TCheckpoint}"/> 实例的工厂。
    /// </summary>
    public interface IStreamQueueCheckpointerFactory
    {
        /// <summary>
        /// 为指定的分区创建流格。
        /// </summary>
        /// <param name="partition">分区。</param>
        /// <returns>流体检查站。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IStreamQueueCheckpointer<string>> Create(string partition);

        /// <summary>
        /// 为指定的分区创建流格。
        /// </summary>
        /// <param name="partition">分区。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>流体检查站。</returns>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        Task<IStreamQueueCheckpointer<string>> Create(string partition, CancellationToken cancellationToken)
            => Create(partition);
#pragma warning restore CS0618
    }

    /// <summary>
    /// 检查一条溪流的功能。
    /// </summary>
    /// <typeparam name="TCheckpoint">检查站类型。</typeparam>
    public interface IStreamQueueCheckpointer<TCheckpoint>
    {
        /// <summary>
        /// 获得一个值,显示是否存在检查站。
        /// </summary>
        /// <value><see langword="true" /> 如果存在检查站;否则, <see langword="false" />。</value>
        bool CheckpointExists { get; }

        /// <summary>
        /// 装满了检查站
        /// </summary>
        /// <returns>检查站。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<TCheckpoint> Load();

        /// <summary>
        /// 装满了检查站
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>检查站。</returns>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        Task<TCheckpoint> Load(CancellationToken cancellationToken) => Load();
#pragma warning restore CS0618

        /// <summary>
        /// 更新检查站
        /// </summary>
        /// <param name="offset">转自</param>
        /// <param name="utcNow">当前UTC 时间。</param>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        void Update(TCheckpoint offset, DateTime utcNow);

        /// <summary>
        /// 更新检查站
        /// </summary>
        /// <param name="offset">转自</param>
        /// <param name="utcNow">当前UTC 时间。</param>
        /// <param name="cancellationToken">取消牌。</param>
#pragma warning disable CS0618 // Required for compatibility with providers which only implement the legacy overload.
        void Update(TCheckpoint offset, DateTime utcNow, CancellationToken cancellationToken)
            => Update(offset, utcNow);
#pragma warning restore CS0618

        /// <summary>
        /// 任何有待设立的持久性储存检查站都可确保最新抵消得到持久保存。
        /// 停止运行或重新平衡时被调用,以防止在重新启动时重播信件。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表冲洗操作。</returns>
        Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
