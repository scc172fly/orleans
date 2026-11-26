using System;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 处理代表订阅事宜 。
    /// 消费者可以将控柄序列化并储存,以便日后取消订货,例如
    /// 中,此 grain上的另一个激活。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class StreamSubscriptionHandle<T> : IEquatable<StreamSubscriptionHandle<T>?>
    {
        /// <summary>
        /// 获取溪流识别码。
        /// </summary>
        /// <value>流体识别符号。</value>
        public abstract StreamId StreamId { get; }

        /// <summary>
        /// 获取提供者的名字。
        /// </summary>
        /// <value>提供者的名称。</value>
        public abstract string ProviderName { get; }

        /// <summary>
        /// 获取此 StreamSubscriptionHandle的独特标识符
        /// </summary>
        public abstract Guid HandleId { get; }

        /// <summary>
        /// 取消从此可见的源流消费者订阅。
        /// </summary>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        public abstract Task UnsubscribeAsync();

        /// <summary>
        /// 订阅流的重新消费。
        /// </summary>
        /// <param name="observer">观察员反对。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>
        /// 新的流订阅器。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 主动的默认订阅已完成交付, <paramref name="token"/> 并非中标。
        /// </exception>
        public abstract Task<StreamSubscriptionHandle<T>> ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null);

        /// <summary>
        /// 恢复溪流订阅的批量消费。
        /// </summary>
        /// <param name="observer">蝙蝠bserver 对象。</param>
        /// <param name="token">用于抵消启动订阅的流序序列。</param>
        /// <returns>
        /// 新的流订阅器。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 主动的默认订阅已完成交付, <paramref name="token"/> 并非中标。
        /// </exception>
        public abstract Task<StreamSubscriptionHandle<T>> ResumeAsync(IAsyncBatchObserver<T> observer, StreamSequenceToken? token = null);

        /// <inheritdoc/>
        public abstract bool Equals(StreamSubscriptionHandle<T>? other);
    }
}
