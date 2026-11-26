
using System;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 管理流系统生命周期的备选方案。
    /// </summary>
    public class StreamLifecycleOptions
    {
        /// <summary>
        /// 确定流流系统生命周期中众所周知的要点。
        /// </summary>
        [Serializable]
        public enum RunState
        {
            /// <summary>
            /// 没有运行 。
            /// </summary>
            None,

            /// <summary>
            /// 流动已经开始启动。
            /// </summary>
            Initialized,

            /// <summary>
            /// 特工已经开始行动了
            /// </summary>
            AgentsStarted,

            /// <summary>
            /// 特工已经停止了
            /// </summary>
            AgentsStopped,
        }

        /// <summary>
        /// 如果设置为 <see cref="RunState.AgentsStarted"/>,则在初始化过程中将启动流拉动剂。
        /// </summary>
        public RunState StartupState { get; set; } = DEFAULT_STARTUP_STATE;

        /// <summary>
        /// 默认流运行时间启动状态 。
        /// </summary>
        public const RunState DEFAULT_STARTUP_STATE = RunState.AgentsStarted;

        /// <summary>
        /// 获取或设定启动流运行时间的生命周期阶段。
        /// </summary>
        /// <value>初始化阶段。</value>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 流流运行时间初始化的默认生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <summary>
        /// 获取或设置启动溪流运行时间的生命周期阶段。
        /// </summary>
        /// <value>启动阶段。</value>
        public int StartStage { get; set; } = DEFAULT_START_STAGE;

        /// <summary>
        /// 流运行时间开始的默认生命周期阶段 。
        /// </summary>
        public const int DEFAULT_START_STAGE = ServiceLifecycleStage.Active;
    }

    /// <summary>
    /// 配置流旁/子的选项 。
    /// </summary>
    public class StreamPubSubOptions
    {
        /// <summary>
        /// 获取或设置酒吧的子类型。
        /// </summary>
        /// <value>酒吧子栏的类型。</value>
        public StreamPubSubType PubSubType { get; set; } = DEFAULT_STREAM_PUBSUB_TYPE;

        /// <summary>
        /// 默认流旁/子执行 。
        /// </summary>
        public const StreamPubSubType DEFAULT_STREAM_PUBSUB_TYPE = StreamPubSubType.ExplicitGrainBasedAndImplicit;
    }

    /// <summary>
    /// 流动吸附剂的选择。
    /// </summary>
    public class StreamPullingAgentOptions
    {
        /// <summary>
        /// 获取或设置初始订阅时所使用的位置,这些位置不指定序列符号或明确的起始位置。
        /// </summary>
        /// <remarks>
        /// <see cref="StreamSubscriptionStartPosition.EarliestAvailable"/> 开始于最早的 Q0Q]
        /// 用户订阅流目前保留在拖拉器本地队列缓存中 。
        /// 接收检查点和供应商排队职位保持不变。
        /// 具体序列符号或明确的每次订阅起始位置优先于此设置。
        /// 在滚动部署期间,在 silos 主机持续流拉动后配置此设置
        /// 代理代理已升级为支持它的一个版本。
        /// </remarks>
        public StreamSubscriptionStartPosition InitialSubscriptionStartPosition { get; set; } = DEFAULT_INITIAL_SUBSCRIPTION_START_POSITION;

        /// <summary>
        /// 默认初始订阅启动位置。
        /// </summary>
        public static readonly StreamSubscriptionStartPosition DEFAULT_INITIAL_SUBSCRIPTION_START_POSITION = StreamSubscriptionStartPosition.Latest;

        /// <summary>
        /// 获取或设定每批货集装箱的大小。
        /// </summary>
        /// <value>每一批集装箱的大小。</value>
        public int BatchContainerBatchSize { get; set; } = DEFAULT_BATCH_CONTAINER_BATCH_SIZE;

        /// <summary>
        /// 默认批量集装箱批量大小。
        /// </summary>
        public static readonly int DEFAULT_BATCH_CONTAINER_BATCH_SIZE = 1;

        /// <summary>
        /// 获取或设置队列信息在投票间隔内的时间长度 。
        /// </summary>
        public TimeSpan GetQueueMsgsTimerPeriod { get; set; } = DEFAULT_GET_QUEUE_MESSAGES_TIMER_PERIOD;

        /// <summary>
        /// 排队信件的投票间隔默认时间 。
        /// </summary>
        public static readonly TimeSpan DEFAULT_GET_QUEUE_MESSAGES_TIMER_PERIOD = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// 获取或设置队列初始化超时 。
        /// </summary>
        /// <value>队列初始化超时。</value>
        public TimeSpan InitQueueTimeout { get; set; } = DEFAULT_INIT_QUEUE_TIMEOUT;

        /// <summary>
        /// 默认的队列初始化超时
        /// </summary>
        public static readonly TimeSpan DEFAULT_INIT_QUEUE_TIMEOUT = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设定事件交付时间的最大值 。
        /// </summary>
        /// <value>最大事件交付时间。</value>
        public TimeSpan MaxEventDeliveryTime { get; set; } = DEFAULT_MAX_EVENT_DELIVERY_TIME;

        /// <summary>
        /// 默认事件交付时间上限 。
        /// </summary>
        public static readonly TimeSpan DEFAULT_MAX_EVENT_DELIVERY_TIME = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置流无活动期。
        /// </summary>
        /// <value>流无活动期。</value>
        public TimeSpan StreamInactivityPeriod { get; set; } = DEFAULT_STREAM_INACTIVITY_PERIOD;

        /// <summary>
        /// 默认流无活动期。
        /// </summary>
        public static readonly TimeSpan DEFAULT_STREAM_INACTIVITY_PERIOD = TimeSpan.FromMinutes(30);
    }
}
