using Orleans.Runtime;

namespace Orleans.ClientObservers
{
    /// <summary>
    /// 客户端范围内特殊观察者的基类型。
    /// </summary>
    internal abstract class ClientObserver
    {
        /// <summary>
        /// 获取观察者 ID。
        /// </summary>
        /// <param name="clientId">客户端 ID。</param>
        /// <returns>观察者 ID。</returns>
        internal abstract ObserverGrainId GetObserverGrainId(ClientGrainId clientId);
    }
}
