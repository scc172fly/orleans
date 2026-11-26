using Orleans.Streams;
using System;
using System.Threading.Tasks;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// 设立检查站,不设固定的检查站。
    /// </summary>
    public class NoOpCheckpointerFactory : IStreamQueueCheckpointerFactory
    {
        /// <summary>
        /// 分享工厂的事例
        /// </summary>
        public static NoOpCheckpointerFactory Instance = new NoOpCheckpointerFactory();

        /// <inheritdoc />
        public Task<IStreamQueueCheckpointer<string>> Create(string partition)
        {
            return Task.FromResult<IStreamQueueCheckpointer<string>>(NoOpCheckpointer.Instance);
        }
    }
    /// <summary>
    /// NoOpCheckpointer 用于 EventDataGeneratorStreamProvider 生态系统,以替换需要后端存储的默认检查器。在 EventHubDataGeneratorStreamProvider中,
    /// 接收器从队列中提取信件时, 它会生成 EventData , 这意味着它不支持可回收流, 因此检查点不会在其中带来多大价值 。 
    /// 所以没有行动检查的检查站就足够了
    /// </summary>
    public class NoOpCheckpointer : IStreamQueueCheckpointer<string>
    {
        /// <summary>
        /// 获得共享的检查站实例。
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
        /// 忽略检查站更新,因为生成的活动流不会持续设置检查站。
        /// </summary>
        /// <param name="offset">忽略了的偏差。</param>
        /// <param name="utcNow">忽略的更新时间。</param>
        public void Update(string offset, DateTime utcNow)
        {
        }
    }
}
