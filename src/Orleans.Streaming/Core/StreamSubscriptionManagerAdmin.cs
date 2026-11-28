using System.Collections.Generic;

namespace Orleans.Streams.Core
{
    internal class StreamSubscriptionManagerAdmin : IStreamSubscriptionManagerAdmin
    {
        private readonly StreamSubscriptionManager _explicitStreamSubscriptionManager;

        public StreamSubscriptionManagerAdmin(IStreamProviderRuntime providerRuntime)
        {
            // 此处使用 ExplicitGrainBasedAndImplicit 发布/订阅模式，因此对隐式订阅的
            // 消费者 Grain 调用 StreamSubscriptionManager.Add(Remove)Subscription 时，其订阅由 ImplicitPubsub 处理，而不会混入 GrainBasedPubsub。
            _explicitStreamSubscriptionManager = new StreamSubscriptionManager(providerRuntime.PubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit)!, // 此已定义的枚举值始终会选定一个运行时。
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
