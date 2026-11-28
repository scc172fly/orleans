
using System;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 流处理系统生命周期的管理选项。
    /// </summary>
    public class StreamLifecycleOptions
    {
        /// <summary>
        /// 标识流处理系统生命周期中的已知状态。
        /// </summary>
        [Serializable]
        public enum RunState
        {
            /// <summary>
            /// 未运行。
            /// </summary>
            None,

            /// <summary>
            /// 流处理系统已初始化。
            /// </summary>
            Initialized,

            /// <summary>
            /// 代理已启动。
            /// </summary>
            AgentsStarted,

            /// <summary>
            /// 代理已停止。
            /// </summary>
            AgentsStopped,
        }

        /// <summary>
        /// 若设置为 <see cref="RunState.AgentsStarted"/>，则在初始化期间启动流拉取代理。
        /// </summary>
        public RunState StartupState { get; set; } = DEFAULT_STARTUP_STATE;

        /// <summary>
        /// 流运行时的默认启动状态。
        /// </summary>
        public const RunState DEFAULT_STARTUP_STATE = RunState.AgentsStarted;

        /// <summary>
        /// 获取或设置初始化流运行时的生命周期阶段。
        /// </summary>
        /// <value>初始化阶段。</value>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 初始化流运行时的默认生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <summary>
        /// 获取或设置启动流运行时的生命周期阶段。
        /// </summary>
        /// <value>启动阶段。</value>
        public int StartStage { get; set; } = DEFAULT_START_STAGE;

        /// <summary>
        /// 启动流运行时的默认生命周期阶段。
        /// </summary>
        public const int DEFAULT_START_STAGE = ServiceLifecycleStage.Active;
    }

    /// <summary>
    /// 配置流发布/订阅的选项。
    /// </summary>
    public class StreamPubSubOptions
    {
        /// <summary>
        /// 获取或设置发布/订阅类型。
        /// </summary>
        /// <value>发布/订阅类型。</value>
        public StreamPubSubType PubSubType { get; set; } = DEFAULT_STREAM_PUBSUB_TYPE;

        /// <summary>
        /// 默认流发布/订阅实现。
        /// </summary>
        public const StreamPubSubType DEFAULT_STREAM_PUBSUB_TYPE = StreamPubSubType.ExplicitGrainBasedAndImplicit;
    }

    /// <summary>
    /// 流拉取代理的选项。
    /// </summary>
    public class StreamPullingAgentOptions
    {
        /// <summary>
        /// 获取或设置未指定序列令牌或显式起始位置的初始订阅所使用的位置。
        /// </summary>
        /// <remarks>
        /// <see cref="StreamSubscriptionStartPosition.EarliestAvailable"/> 从拉取代理的本地队列缓存当前为
        /// 已订阅流保留的最早消息开始。
        /// 接收器检查点和提供程序队列位置保持不变。
        /// 具体的序列令牌或单个订阅显式指定的起始位置优先于此设置。
        /// 在滚动部署期间，应在托管持久化流拉取代理的 Silo
        /// 升级到支持此设置的版本后再进行配置。
        /// </remarks>
        public StreamSubscriptionStartPosition InitialSubscriptionStartPosition { get; set; } = DEFAULT_INITIAL_SUBSCRIPTION_START_POSITION;

        /// <summary>
        /// 初始订阅的默认起始位置。
        /// </summary>
        public static readonly StreamSubscriptionStartPosition DEFAULT_INITIAL_SUBSCRIPTION_START_POSITION = StreamSubscriptionStartPosition.Latest;

        /// <summary>
        /// 获取或设置每个批次容器集合的大小。
        /// </summary>
        /// <value>每个批次容器集合的大小。</value>
        public int BatchContainerBatchSize { get; set; } = DEFAULT_BATCH_CONTAINER_BATCH_SIZE;

        /// <summary>
        /// 批次容器集合的默认大小。
        /// </summary>
        public static readonly int DEFAULT_BATCH_CONTAINER_BATCH_SIZE = 1;

        /// <summary>
        /// 获取或设置轮询队列消息的时间间隔。
        /// </summary>
        public TimeSpan GetQueueMsgsTimerPeriod { get; set; } = DEFAULT_GET_QUEUE_MESSAGES_TIMER_PERIOD;

        /// <summary>
        /// 轮询队列消息的默认时间间隔。
        /// </summary>
        public static readonly TimeSpan DEFAULT_GET_QUEUE_MESSAGES_TIMER_PERIOD = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// 获取或设置队列初始化超时时间。
        /// </summary>
        /// <value>队列初始化超时时间。</value>
        public TimeSpan InitQueueTimeout { get; set; } = DEFAULT_INIT_QUEUE_TIMEOUT;

        /// <summary>
        /// 默认队列初始化超时时间。
        /// </summary>
        public static readonly TimeSpan DEFAULT_INIT_QUEUE_TIMEOUT = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设置事件投递的最长时间。
        /// </summary>
        /// <value>事件投递的最长时间。</value>
        public TimeSpan MaxEventDeliveryTime { get; set; } = DEFAULT_MAX_EVENT_DELIVERY_TIME;

        /// <summary>
        /// 默认事件投递最长时间。
        /// </summary>
        public static readonly TimeSpan DEFAULT_MAX_EVENT_DELIVERY_TIME = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置流的非活动时长。
        /// </summary>
        /// <value>流的非活动时长。</value>
        public TimeSpan StreamInactivityPeriod { get; set; } = DEFAULT_STREAM_INACTIVITY_PERIOD;

        /// <summary>
        /// 默认流非活动时长。
        /// </summary>
        public static readonly TimeSpan DEFAULT_STREAM_INACTIVITY_PERIOD = TimeSpan.FromMinutes(30);
    }
}
