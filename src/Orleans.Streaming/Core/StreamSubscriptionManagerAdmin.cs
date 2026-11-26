using System.Collections.Generic;

namespace Orleans.Streams.Core
{
    internal class StreamSubscriptionManagerAdmin : IStreamSubscriptionManagerAdmin
    {
        private readonly StreamSubscriptionManager _explicitStreamSubscriptionManager;

        public StreamSubscriptionManagerAdmin(IStreamProviderRuntime providerRuntime)
        {
            // 在此使用 ExplicitGrainBasedAndImplicit 旁端次元, 所以如果 StreamSubscriptionManager.Add(删除)订阅要求默认订阅
            // 消费者 grain,其订阅将由 ImplicitPubsub处理,不会被搅乱成 GrainBasedPubsub 
            _explicitStreamSubscriptionManager = new StreamSubscriptionManager(providerRuntime.PubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit)!, // 此定义的 enum 值总是选择运行时间 。
                StreamSubscriptionManagerType.ExplicitSubscribeOnly);
        }

        public IStreamSubscriptionManager GetStreamSubscriptionManager(string managerType)
        {
            return managerType switch
            {
                StreamSubscriptionManagerType.ExplicitSubscribeOnly => _explicitStreamSubscriptionManager,
                _ => throw new KeyNotFoundException($"Cannot find StreamSubscriptionManager with type {managerType}.")
            };
        }
    }
}
