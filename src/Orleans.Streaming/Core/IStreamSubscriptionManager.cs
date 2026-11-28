using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streams.Core
{
    /// <summary>
    /// 提供管理流订阅的功能。
    /// </summary>
    public interface IStreamSubscriptionManager
    {
        /// <summary>
        /// 将指定 Grain 订阅到流。
        /// </summary>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="grainRef">Grain 引用。</param>
        /// <returns>流订阅。</returns>
        Task<StreamSubscription> AddSubscription(string streamProviderName, StreamId streamId, GrainReference grainRef);

        /// <summary>
        /// 取消 Grain 对流的订阅。
        /// </summary>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="streamId">流标识符。</param>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task RemoveSubscription(string streamProviderName, StreamId streamId, Guid subscriptionId);

        /// <summary>
        /// 获取流的订阅。
        /// </summary>
        /// <param name="streamProviderName">流提供程序名称。</param>
        /// <param name="streamId">流标识符。</param>
        /// <returns>订阅集合。</returns>
        Task<IEnumerable<StreamSubscription>> GetSubscriptions(string streamProviderName, StreamId streamId);
    }
}
