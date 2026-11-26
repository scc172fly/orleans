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
    /// 这是一个持续流源的供应商适配器 产生它自己的事件 而不是从Eventhubt读到它们
    /// 这主要用于试验目的。
    ///  </summary>
    public partial class EventDataGeneratorAdapterFactory : EventHubAdapterFactory, IControllable
    {
        private readonly EventDataGeneratorStreamOptions ehGeneratorOptions;

        /// <summary>
        /// 初始化 <see cref="EventDataGeneratorAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="options">事件数据生成器选项。</param>
        /// <param name="ehOptions">活动枢纽连接选项。</param>
        /// <param name="receiverOptions">事件枢纽接收器选项。</param>
        /// <param name="cacheOptions">事件枢纽缓存压力选项。</param>
        /// <param name="evictionOptions">溪流缓冲驱逐选项。</param>
        /// <param name="statisticOptions">流统计选项。</param>
        /// <param name="dataAdapter">用于在事件枢纽数据与 Orleans 数据。</param>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="loggerFactory">伐木厂</param>
        /// <param name="environmentStatisticsProvider">环境统计提供者。</param>
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
        /// 配置生成活动接收器厂,并启动通用适配器状态。
        /// </summary>
        /// <remarks>此提供者生成本地事件, 但不创建事件枢纽客户端 。</remarks>
        public override void Init()
        {
            this.EventHubReceiverFactory = this.EHGeneratorReceiverFactory;
            base.Init();
        }

        /// <summary>
        /// 执行不初始化, 因为生成的活动流无法连接到事件枢纽 。
        /// </summary>
        protected override void InitEventHubClient()
        {
            //什么都不做 EventDataGeneratorStreamProvider 不需要连接 EventHubClient
        }

        /// <summary>
        /// 生成模拟事件枢纽分区识别符。
        /// </summary>
        /// <returns>一项任务,它解决模拟分区标识符的问题。</returns>
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
                //每个代理器都收到相同的随机数, 以队列计数时做一个模式, 获得同样的随机队列来分配流 。
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
                //如果将流流指定给此接收器/ 队列, 那么它会要求数据生成器停止生产
                ehReceiver.Value.StopProducingOnStream(streamId);
            }
        }

        /// <summary>
        /// 为模拟事件枢纽生成分区识别符。
        /// </summary>
        /// <param name="partitionCount">要生成的分区识别符数量。</param>
        /// <returns>生成的分区识别符号。</returns>
        public static string[] GenerateEventHubPartitions(int partitionCount)
        {
            var partitions = new string[partitionCount];
            for (int i = 0; i < partitions.Length; i++)
                partitions[i] = $"partition-{i}";
            return partitions;
        }

        /// <summary>
        /// 不可控的无法控制命令
        /// </summary>
        public enum Commands
        {
            /// <summary>
            /// Randomly_Place_Stream_To_Queue 命令
            /// </summary>
            Randomly_Place_Stream_To_Queue = (int)PersistentStreamProviderCommand.AdapterFactoryCommandStartRange + 4,
            /// <summary>
            /// 停止生产命令
            /// </summary>
            Stop_Producing_On_Stream = (int)PersistentStreamProviderCommand.AdapterFactoryCommandStartRange + 5
        }

        /// <summary>
        /// RandomlyPlaceStreamToQueue 方法的参数
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
            /// 随机数
            /// </summary>
            [Id(1)]
            public int RandomNumber { get; set; }

            /// <summary>
            /// 构造建构器
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
        /// 执行命令
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
                    // 此命令总是使用 StreamId 参数来发布。
                    this.StopProducingOnStream((StreamId)arg!);
                    break;
                default: break;

            }
            return Task.FromResult<object?>(true);
        }

        /// <summary>
        /// 创建并启动事件数据生成器适配厂。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <returns>初始化适配器厂。</returns>
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
