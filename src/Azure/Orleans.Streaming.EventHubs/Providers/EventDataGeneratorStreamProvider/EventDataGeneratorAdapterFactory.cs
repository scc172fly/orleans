using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Statistics;
using Orleans.Streams;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// 这是一个持久化流提供程序适配器，它自行生成事件，而不是从 Event Hub 读取事件。
    /// 主要用于测试。
    ///  </summary>
    public partial class EventDataGeneratorAdapterFactory : EventHubAdapterFactory, IControllable
    {
        private readonly EventDataGeneratorStreamOptions ehGeneratorOptions;

        /// <summary>
        /// 初始化 <see cref="EventDataGeneratorAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="options">事件数据生成器选项。</param>
        /// <param name="ehOptions">Event Hub 连接选项。</param>
        /// <param name="receiverOptions">Event Hub 接收器选项。</param>
        /// <param name="cacheOptions">Event Hub 缓存压力选项。</param>
        /// <param name="evictionOptions">流缓存淘汰选项。</param>
        /// <param name="statisticOptions">流统计信息选项。</param>
        /// <param name="dataAdapter">用于在 Event Hubs 数据和 Orleans 流数据之间转换的适配器。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        /// <param name="environmentStatisticsProvider">环境统计信息提供程序。</param>
        public EventDataGeneratorAdapterFactory(
            string name,
            EventDataGeneratorStreamOptions options,
            EventHubOptions ehOptions,
            EventHubReceiverOptions receiverOptions,
            EventHubStreamCachePressureOptions cacheOptions,
            StreamCacheEvictionOptions evictionOptions,
            StreamStatisticOptions statisticOptions,
            IEventHubDataAdapter dataAdapter,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            IEnvironmentStatisticsProvider environmentStatisticsProvider)
            : base(name, ehOptions, receiverOptions, cacheOptions, evictionOptions, statisticOptions, dataAdapter, serviceProvider, loggerFactory, environmentStatisticsProvider)
        {
            this.ehGeneratorOptions = options;
        }

        /// <summary>
        /// 配置生成事件的接收器工厂，并初始化公共适配器状态。
        /// </summary>
        /// <remarks>此提供程序在本地生成事件，不创建 Event Hubs 客户端。</remarks>
        public override void Init()
        {
            this.EventHubReceiverFactory = this.EHGeneratorReceiverFactory;
            base.Init();
        }

        /// <summary>
        /// 不执行初始化，因为生成式事件流不连接 Event Hubs。
        /// </summary>
        protected override void InitEventHubClient()
        {
            // 不做任何操作，EventDataGeneratorStreamProvider 不需要与 EventHubClient 建立连接。
        }

        /// <summary>
        /// 生成模拟的 Event Hub 分区标识符。
        /// </summary>
        /// <returns>完成时返回模拟分区标识符的任务。</returns>
        protected override Task<string[]> GetPartitionIdsAsync()
        {
            return Task.FromResult(GenerateEventHubPartitions(this.ehGeneratorOptions.EventHubPartitionCount));
        }

        private IEventHubReceiver EHGeneratorReceiverFactory(EventHubPartitionSettings settings, string offset, ILogger logger)
        {
            var streamGeneratorFactory = this.serviceProvider.GetKeyedService<Func<StreamId, IStreamDataGenerator<EventData>>>(this.Name)
                ?? SimpleStreamEventDataGenerator.CreateFactory(this.serviceProvider);
            var generator = new EventHubPartitionDataGenerator(this.ehGeneratorOptions, streamGeneratorFactory, logger);
            return new EventHubPartitionGeneratorReceiver(generator);
        }

        private void RandomlyPlaceStreamToQueue(StreamRandomPlacementArg args)
        {
            if (args == null)
                return;

            var allQueueInTheCluster = EventHubQueueMapper?.GetAllQueues();
            if (allQueueInTheCluster != null)
            {
                var allQueues = allQueueInTheCluster as QueueId[] ?? allQueueInTheCluster.ToArray();
                // 每个代理收到相同的随机数，对队列数量取模，获得同一个随机队列，用于分配流。
                var queueToAssign = allQueues[args.RandomNumber % allQueues.Length];
                if (EventHubReceivers.TryGetValue(queueToAssign, out var receiverToAssign))
                {
                    receiverToAssign.ConfigureDataGeneratorForStream(args.StreamId);
                    LogInfoStreamAssignedToQueue(logger, args.StreamId, queueToAssign);
                }
            }
            else
            {
                LogInfoCannotGetQueues(logger);
            }
        }

        private void StopProducingOnStream(StreamId streamId)
        {
            foreach (var ehReceiver in this.EventHubReceivers)
            {
                // 如果流被分配给此接收器或队列，则请求数据生成器停止生成数据。
                ehReceiver.Value.StopProducingOnStream(streamId);
            }
        }

        /// <summary>
        /// 生成模拟 Event Hub 的分区标识符。
        /// </summary>
        /// <param name="partitionCount">要生成的分区标识符数量。</param>
        /// <returns>生成的分区标识符。</returns>
        public static string[] GenerateEventHubPartitions(int partitionCount)
        {
            var partitions = new string[partitionCount];
            for (int i = 0; i < partitions.Length; i++)
                partitions[i] = $"partition-{i}";
            return partitions;
        }

        /// <summary>
        /// IControllable 的命令。
        /// </summary>
        public enum Commands
        {
            /// <summary>
            /// Randomly_Place_Stream_To_Queue 命令。
            /// </summary>
            Randomly_Place_Stream_To_Queue = (int)PersistentStreamProviderCommand.AdapterFactoryCommandStartRange + 4,
            /// <summary>
            /// Stop_Producing_On_Stream 命令。
            /// </summary>
            Stop_Producing_On_Stream = (int)PersistentStreamProviderCommand.AdapterFactoryCommandStartRange + 5
        }

        /// <summary>
        /// RandomlyPlaceStreamToQueue 方法的参数。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public class StreamRandomPlacementArg
        {
            /// <summary>
            /// StreamId
            /// </summary>
            [Id(0)]
            public StreamId StreamId { get; set; }

            /// <summary>
            /// 随机数。
            /// </summary>
            [Id(1)]
            public int RandomNumber { get; set; }

            /// <summary>
            /// 构造函数。
            /// </summary>
            /// <param name="streamId"></param>
            /// <param name="randomNumber"></param>
            public StreamRandomPlacementArg(StreamId streamId, int randomNumber)
            {
                this.StreamId = streamId;
                this.RandomNumber = randomNumber;
            }
        }

        /// <summary>
        /// 执行命令。
        /// </summary>
        /// <param name="command"></param>
        /// <param name="arg"></param>
        /// <returns></returns>
        public virtual Task<object?> ExecuteCommand(int command, object? arg)
        {
            switch (command)
            {
                case (int)Commands.Randomly_Place_Stream_To_Queue:
                    this.RandomlyPlaceStreamToQueue((arg as StreamRandomPlacementArg)!);
                    break;
                case (int)Commands.Stop_Producing_On_Stream:
                    // 发出此命令时始终提供 StreamId 参数。
                    this.StopProducingOnStream((StreamId)arg!);
                    break;
                default: break;

            }
            return Task.FromResult<object?>(true);
        }

        /// <summary>
        /// 创建并初始化事件数据生成器适配器工厂。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <returns>已初始化的适配器工厂。</returns>
        public new static EventDataGeneratorAdapterFactory Create(IServiceProvider services, string name)
        {
            var generatorOptions = services.GetOptionsByName<EventDataGeneratorStreamOptions>(name);
            var ehOptions = services.GetOptionsByName<EventHubOptions>(name);
            var receiverOptions = services.GetOptionsByName<EventHubReceiverOptions>(name);
            var cacheOptions = services.GetOptionsByName<EventHubStreamCachePressureOptions>(name);
            var statisticOptions = services.GetOptionsByName<StreamStatisticOptions>(name);
            var evictionOptions = services.GetOptionsByName<StreamCacheEvictionOptions>(name);
            IEventHubDataAdapter dataAdapter = services.GetKeyedService<IEventHubDataAdapter>(name)
                ?? services.GetService<IEventHubDataAdapter>()
                ?? ActivatorUtilities.CreateInstance<EventHubDataAdapter>(services);
            var factory = ActivatorUtilities.CreateInstance<EventDataGeneratorAdapterFactory>(services, name, generatorOptions, ehOptions, receiverOptions, cacheOptions,
                evictionOptions, statisticOptions, dataAdapter);
            factory.Init();
            return factory;
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Stream {StreamId} is assigned to queue {QueueId}"
        )]
        private static partial void LogInfoStreamAssignedToQueue(ILogger logger, StreamId streamId, QueueId queueId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Cannot get queues in the cluster, current streamQueueMapper is not EventHubQueueMapper"
        )]
        private static partial void LogInfoCannotGetQueues(ILogger logger);
    }
}
