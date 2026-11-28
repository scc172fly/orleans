using System;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 表示此订阅的句柄。
    /// 消费者可以序列化并保存该句柄，以便日后取消订阅，例如
    /// 在此 Grain 的另一个激活实例中取消订阅。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public abstract class StreamSubscriptionHandle<T> : IEquatable<StreamSubscriptionHandle<T>?>
    {
        /// <summary>
        /// 获取流标识符。
        /// </summary>
        /// <value>流标识符。</value>
        public abstract StreamId StreamId { get; }

        /// <summary>
        /// 获取提供程序的名称。
        /// </summary>
        /// <value>提供程序的名称。</value>
        public abstract string ProviderName { get; }

        /// <summary>
        /// 获取此 StreamSubscriptionHandle 的唯一标识符。
        /// </summary>
        public abstract Guid HandleId { get; }

        /// <summary>
        /// 取消流消费者对此可观察对象的订阅。
        /// </summary>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        public abstract Task UnsubscribeAsync();

        /// <summary>
        /// 恢复从流订阅消费数据。
        /// </summary>
        /// <param name="observer">观察者对象。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>
        /// 新的流订阅句柄。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 活动的隐式订阅已经完成过一次投递，且 <paramref name="token"/> 不为 null。
        /// </exception>
        public abstract Task<StreamSubscriptionHandle<T>> ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null);

        /// <summary>
        /// 恢复从流订阅批量消费数据。
        /// </summary>
        /// <param name="observer">批量观察者对象。</param>
        /// <param name="token">用作订阅起始偏移位置的流序列令牌。</param>
        /// <returns>
        /// 新的流订阅句柄。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 活动的隐式订阅已经完成过一次投递，且 <paramref name="token"/> 不为 null。
        /// </exception>
        public abstract Task<StreamSubscriptionHandle<T>> ResumeAsync(IAsyncBatchObserver<T> observer, StreamSequenceToken? token = null);

        /// <inheritdoc/>
        public abstract bool Equals(StreamSubscriptionHandle<T>? other);
    }
}
