using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streaming.AzureStorage;
using Orleans.Streams;

namespace Orleans.Providers.Streams.PersistentStreams
{
    /// <summary>
    /// 发送失败处理器, 写出无法保存 zure表的处理器。
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    public class AzureTableStorageStreamFailureHandler<TEntity> : IStreamFailureHandler where TEntity : StreamDeliveryFailureEntity, new()
    {
        private static readonly Func<TEntity> DefaultCreateEntity = () => new TEntity();
        private readonly Serializer<StreamSequenceToken> serializer;
        private readonly string clusterId;
        private readonly AzureTableDataManager<TEntity> dataManager;
        private readonly Func<TEntity> createEntity;

        /// <summary>
        /// 发送失败处理器, 写出无法保存 zure表的处理器。
        /// </summary>
        /// <param name="serializer"></param>
        /// <param name="loggerFactory">用于使用</param>的对帐厂
        /// <param name="faultOnFailure"></param>
        /// <param name="clusterId"></param>
        /// <param name="azureStorageOptions"></param>
        /// <param name="createEntity"></param>
        public AzureTableStorageStreamFailureHandler(Serializer<StreamSequenceToken> serializer, ILoggerFactory loggerFactory, bool faultOnFailure, string clusterId, AzureStorageOperationOptions azureStorageOptions, Func<TEntity>? createEntity = null)
        {
            if (string.IsNullOrEmpty(clusterId))
            {
                throw new ArgumentNullException(nameof(clusterId));
            }
            ArgumentNullException.ThrowIfNull(azureStorageOptions);
            if (string.IsNullOrEmpty(azureStorageOptions.TableName))
            {
                throw new ArgumentException("A table name must be configured.", nameof(azureStorageOptions));
            }

            this.serializer = serializer;
            this.clusterId = clusterId;
            ShouldFaultSubsriptionOnError = faultOnFailure;
            this.createEntity = createEntity ?? DefaultCreateEntity;
            dataManager = new AzureTableDataManager<TEntity>(
                azureStorageOptions,
                loggerFactory.CreateLogger<AzureTableDataManager<TEntity>>());
        }

        /// <summary>
        /// 指示在流出故障时订阅是否发生错误状态
        /// </summary>
        public bool ShouldFaultSubsriptionOnError { get; private set; }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        public Task InitAsync()
        {
            return dataManager.InitTableAsync();
        }

        /// <summary>
        /// 在用尽努力再试之后,当一项事件不能交付给消费者时,应叫停。
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <param name="streamProviderName"></param>
        /// <param name="streamId"></param>
        /// <param name="sequenceToken"></param>
        /// <returns></returns>
        public Task OnDeliveryFailure(GuidId subscriptionId, string streamProviderName, StreamId streamId,
            StreamSequenceToken? sequenceToken)
        {
            return OnFailure(subscriptionId, streamProviderName, streamId, sequenceToken);
        }

        /// <summary>
        /// 在消费者要求的订阅在用尽所有手段尝试之后无法安装时,应调用。
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <param name="streamProviderName"></param>
        /// <param name="streamId"></param>
        /// <param name="sequenceToken"></param>
        /// <returns></returns>
        public Task OnSubscriptionFailure(GuidId subscriptionId, string streamProviderName, StreamId streamId,
            StreamSequenceToken? sequenceToken)
        {
            return OnFailure(subscriptionId, streamProviderName, streamId, sequenceToken);
        }

        private async Task OnFailure(GuidId subscriptionId, string streamProviderName, StreamId streamId,
                StreamSequenceToken? sequenceToken)
        {
            if (subscriptionId == null)
            {
                throw new ArgumentNullException(nameof(subscriptionId));
            }
            if (string.IsNullOrWhiteSpace(streamProviderName))
            {
                throw new ArgumentNullException(nameof(streamProviderName));
            }

            var failureEntity = createEntity();
            failureEntity.SubscriptionId = subscriptionId.Guid;
            failureEntity.StreamProviderName = streamProviderName;
            failureEntity.StreamGuid = streamId.GetKeyAsString();
            failureEntity.StreamNamespace = streamId.GetNamespace();
            failureEntity.SetSequenceToken(this.serializer, sequenceToken);
            failureEntity.SetPartitionKey(this.clusterId);
            failureEntity.SetRowkey();
            await dataManager.CreateTableEntryAsync(failureEntity);
        }
    }
}
