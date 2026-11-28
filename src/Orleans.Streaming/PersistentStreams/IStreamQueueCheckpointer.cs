using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 用于创建 <see cref="IStreamQueueCheckpointer{TCheckpoint}"/> 实例的工厂。
    /// </summary>
    public interface IStreamQueueCheckpointerFactory
    {
        /// <summary>
        /// 为指定分区创建流检查点管理器。
        /// </summary>
        /// <param name="partition">分区。</param>
        /// <returns>流检查点管理器。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<IStreamQueueCheckpointer<string>> Create(string partition);

        /// <summary>
        /// 为指定分区创建流检查点管理器。
        /// </summary>
        /// <param name="partition">分区。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>流检查点管理器。</returns>
#pragma warning disable CS0618 // 为兼容仅实现旧版重载的提供程序而保留。
        Task<IStreamQueueCheckpointer<string>> Create(string partition, CancellationToken cancellationToken)
            => Create(partition);
#pragma warning restore CS0618
    }

    /// <summary>
    /// 提供为流设置检查点的功能。
    /// </summary>
    /// <typeparam name="TCheckpoint">检查点类型。</typeparam>
    public interface IStreamQueueCheckpointer<TCheckpoint>
    {
        /// <summary>
        /// 获取一个值，指示检查点是否存在。
        /// </summary>
        /// <value>如果检查点存在，则为 <see langword="true" />；否则为 <see langword="false" />。</value>
        bool CheckpointExists { get; }

        /// <summary>
        /// 加载检查点。
        /// </summary>
        /// <returns>检查点。</returns>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        Task<TCheckpoint> Load();

        /// <summary>
        /// 加载检查点。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>检查点。</returns>
#pragma warning disable CS0618 // 为兼容仅实现旧版重载的提供程序而保留。
        Task<TCheckpoint> Load(CancellationToken cancellationToken) => Load();
#pragma warning restore CS0618

        /// <summary>
        /// 更新检查点。
        /// </summary>
        /// <param name="offset">偏移量。</param>
        /// <param name="utcNow">当前 UTC 时间。</param>
        [Obsolete("Use the overload which accepts a CancellationToken.")]
        void Update(TCheckpoint offset, DateTime utcNow);

        /// <summary>
        /// 更新检查点。
        /// </summary>
        /// <param name="offset">偏移量。</param>
        /// <param name="utcNow">当前 UTC 时间。</param>
        /// <param name="cancellationToken">取消令牌。</param>
#pragma warning disable CS0618 // 为兼容仅实现旧版重载的提供程序而保留。
        void Update(TCheckpoint offset, DateTime utcNow, CancellationToken cancellationToken)
            => Update(offset, utcNow);
#pragma warning restore CS0618

        /// <summary>
        /// 将待写入的检查点刷新到持久化存储，确保最新偏移量已持久保存。
        /// 在关闭或再均衡期间调用，以防止重新启动时重放消息。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示刷新操作的 <see cref="Task"/>。</returns>
        Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
