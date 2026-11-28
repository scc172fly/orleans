using Orleans.Runtime;

namespace Orleans.Streams.Core
{
    /// <summary>
    /// 提供为特定流和订阅创建流订阅句柄的功能。
    /// </summary>
    public interface IStreamSubscriptionHandleFactory
    {
        /// <summary>
        /// 获取流标识符。
        /// </summary>
        /// <value>流标识符。</value>
        StreamId StreamId { get; }

        /// <summary>
        /// 获取提供程序的名称。
        /// </summary>
        /// <value>提供程序的名称。</value>
        string ProviderName { get; }

        /// <summary>
        /// 获取订阅标识符。
        /// </summary>
        /// <value>订阅标识符。</value>
        GuidId SubscriptionId { get; }

        /// <summary>
        /// 为此实例标识的流和订阅创建流订阅句柄。
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <returns>新的流订阅句柄。</returns>
        StreamSubscriptionHandle<T> Create<T>();
    }
}
