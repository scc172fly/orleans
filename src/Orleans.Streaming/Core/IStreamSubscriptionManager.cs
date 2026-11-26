using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams.Core
{
    /// <summary>
    /// 管理流用户功能。
    /// </summary>
    public interface IStreamSubscriptionManager
    {
        /// <summary>
        /// 将指定的 grain 订阅到流中。
        /// </summary>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="grainRef">下 grain 参考。</param>
        /// <returns>流状订阅。</returns>
        Task<StreamSubscription> AddSubscription(string streamProviderName, StreamId streamId, GrainReference grainRef);

        /// <summary>
        /// 从流中取消订阅一个 grain 。
        /// </summary>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task RemoveSubscription(string streamProviderName, StreamId streamId, Guid subscriptionId);

        /// <summary>
        /// 获得一条流的订阅。
        /// </summary>
        /// <param name="streamProviderName">流体提供者的名称。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns>订阅</returns>
        Task<IEnumerable<StreamSubscription>> GetSubscriptions(string streamProviderName, StreamId streamId);
    }
}
