using Orleans.Streams;
using Orleans.Runtime;
using Orleans.Streams.PubSub;
using Microsoft.Extensions.DependencyInjection;

namespace UnitTests.Grains.ProgrammaticSubscribe
{
    public interface ISubscribeGrain : IGrainWithGuidKey
    {
        Task<bool> CanGetSubscriptionManager(string providerName);
    }

    public class SubscribeGrain : Grain, ISubscribeGrain
    {
        public Task<bool> CanGetSubscriptionManager(string providerName)
        {
            return Task.FromResult(this.ServiceProvider.GetKeyedService<IStreamProvider>(providerName)!.TryGetStreamSubscriptionManager(out _)); // 测试会在调用此 Grain 之前配置命名提供程序。
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class FullStreamIdentity : IStreamIdentity
    {
        public FullStreamIdentity(Guid streamGuid, string streamNamespace, string providerName)
        {
            Guid = streamGuid;
            Namespace = streamNamespace;
            this.ProviderName = providerName;
        }

        [Id(0)]
        public string ProviderName;

        /// <summary>
        /// 流的 GUID 主键。
        /// </summary>
        [Id(1)]
        public Guid Guid { get; }

        /// <summary>
        /// 流命名空间。
        /// </summary>
        [Id(2)]
        public string Namespace { get; }

        public static implicit operator StreamId(FullStreamIdentity identity) => StreamId.Create(identity);
    }
}
