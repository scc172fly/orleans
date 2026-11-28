using Orleans.Streams;
using System;
using System.Threading.Tasks;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// 创建不保留持久化检查点状态的检查点管理器。
    /// </summary>
    public class NoOpCheckpointerFactory : IStreamQueueCheckpointerFactory
    {
        /// <summary>
        /// 获取共享工厂实例。
        /// </summary>
        public static NoOpCheckpointerFactory Instance = new NoOpCheckpointerFactory();

        /// <inheritdoc />
        public Task<IStreamQueueCheckpointer<string>> Create(string partition)
        {
            return Task.FromResult<IStreamQueueCheckpointer<string>>(NoOpCheckpointer.Instance);
        }
    }
    /// <summary>
    /// NoOpCheckpointer 在 EventDataGeneratorStreamProvider 体系中用于替代需要后端存储的默认 Checkpointer。在 EventHubDataGeneratorStreamProvider 中，
    /// 接收器从队列拉取消息时即时生成 EventData，这意味着它不支持可恢复流，因此检查点机制在此处没有太大价值。
    /// 所以使用不执行任何操作的检查点管理器即可。
    /// </summary>
    public class NoOpCheckpointer : IStreamQueueCheckpointer<string>
    {
        /// <summary>
        /// 获取共享检查点管理器实例。
        /// </summary>
        public static NoOpCheckpointer Instance = new NoOpCheckpointer();

        /// <inheritdoc />
        public bool CheckpointExists => true;

        /// <inheritdoc />
        public Task<string> Load()
        {
            return Task.FromResult(EventHubConstants.StartOfStream);
        }
        /// <summary>
        /// 忽略检查点更新，因为生成式事件流不持久化检查点。
        /// </summary>
        /// <param name="offset">被忽略的偏移量。</param>
        /// <param name="utcNow">被忽略的更新时间。</param>
        public void Update(string offset, DateTime utcNow)
        {
        }
    }
}
