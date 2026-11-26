using Orleans.Runtime;

namespace Orleans.Streams.Core
{
    /// <summary>
    /// 功能是创建特定流和订阅的流订阅处理器。
    /// </summary>
    public interface IStreamSubscriptionHandleFactory
    {
        /// <summary>
        /// 获取溪流识别码。
        /// </summary>
        /// <value>流体识别符号。</value>
        StreamId StreamId { get; }

        /// <summary>
        /// 获取提供者的名字。
        /// </summary>
        /// <value>提供者的名称。</value>
        string ProviderName { get; }

        /// <summary>
        /// 获取订阅标识符。
        /// </summary>
        /// <value>订阅标识符。</value>
        GuidId SubscriptionId { get; }

        /// <summary>
        /// 创建此实例所指定的流和订阅的流订阅控件。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <returns>新流订阅控点。</returns>
        StreamSubscriptionHandle<T> Create<T>();
    }
}
