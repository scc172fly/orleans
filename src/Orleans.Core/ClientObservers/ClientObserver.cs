using Orleans.Runtime;

namespace Orleans.ClientObservers
{
    /// <summary>
    /// 特别客户观察员的基数。
    /// </summary>
    internal abstract class ClientObserver
    {
        /// <summary>
        /// 获取观察者身份
        /// </summary>
        /// <param name="clientId">客户端id。</param>
        /// <returns>观察员id。</returns>
        internal abstract ObserverGrainId GetObserverGrainId(ClientGrainId clientId);
    }
}
