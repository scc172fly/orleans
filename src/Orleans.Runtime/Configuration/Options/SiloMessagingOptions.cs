using System;
using System.Diagnostics;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 指定与 Silo 相关的全局消息传递选项。
    /// </summary>
    public class SiloMessagingOptions : MessagingOptions
    {
        /// <summary>
        /// <see cref="SystemResponseTimeout"/>.
        /// </summary>
        private TimeSpan systemResponseTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置 Silo 用于向其他 Silo 发送出站
        /// 消息（请求、响应及通知）的并行队列数量及相应线程数。
        /// 如果未指定此属性，则使用 System.Environment.ProcessorCount。
        /// </summary>
        public int SiloSenderQueues { get; set; }

        /// <summary>
        /// 获取或设置 Silo 网关用于向已连接的客户端发送出站
        /// 消息（请求、响应及通知）的并行队列数量及相应线程数。
        /// 如果未指定此属性，则使用 System.Environment.ProcessorCount。
        /// </summary>
        public int GatewaySenderQueues { get; set; }

        /// <summary>
        /// 获取或设置消息在 Silo 之间转发的最大次数。
        /// 当 Silo 发生故障且成员资格不稳定时，运行时内部将转发用作恢复机制。
        /// 在此期间，消息可能无法正确路由到目标；运行时会尝试转发一定次数，再拒绝这些消息。
        /// </summary>
        public int MaxForwardCount { get; set; } = 2;

        /// <summary>
        /// 获取或设置网关在丢弃已断开连接的客户端之前等待的时间。
        /// </summary>
        public TimeSpan ClientDropTimeout { get; set; } = Constants.DEFAULT_CLIENT_DROP_TIMEOUT;

        /// <summary>
        /// 获取或设置刷新已连接客户端列表的间隔。
        /// </summary>
        public TimeSpan ClientRegistrationRefresh { get; set; } = DEFAULT_CLIENT_REGISTRATION_REFRESH;

        /// <summary>
        /// <see cref="ClientRegistrationRefresh"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CLIENT_REGISTRATION_REFRESH = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 获取或设置网关在通知已连接客户端后、继续
        /// 关闭流程之前等待的时间。
        /// </summary>
        public TimeSpan ClientGatewayShutdownNotificationTimeout { get; set; } = DEFAULT_CLIENT_GW_NOTIFICATION_TIMEOUT;

        /// <summary>
        /// <see cref="ClientGatewayShutdownNotificationTimeout"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CLIENT_GW_NOTIFICATION_TIMEOUT = TimeSpan.Zero;

        /// <summary>
        /// 获取或设置每个 Grain 的待处理请求数阈值。超过该阈值时生成警告。
        /// </summary>
        public int MaxEnqueuedRequestsSoftLimit { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_SOFT_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsSoftLimit"/> 的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_SOFT_LIMIT = 0;

        /// <summary>
        /// 获取或设置每个 Grain 的待处理请求数阈值。超过该阈值时拒绝请求。
        /// </summary>
        public int MaxEnqueuedRequestsHardLimit { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_HARD_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsHardLimit"/> 的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_HARD_LIMIT = 0;

        /// <summary>
        /// 获取或设置无状态工作者的每个 Grain 待处理请求数阈值。超过该阈值时生成警告。
        /// </summary>
        public int MaxEnqueuedRequestsSoftLimit_StatelessWorker { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_SOFT_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsSoftLimit_StatelessWorker"/> 的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_SOFT_LIMIT = 0;

        /// <summary>
        /// 获取或设置无状态工作者的每个 Grain 待处理请求数阈值。超过该阈值时拒绝请求。
        /// </summary>
        public int MaxEnqueuedRequestsHardLimit_StatelessWorker { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_HARD_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsHardLimit_StatelessWorker"/> 的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_HARD_LIMIT = 0;

        /// <summary>
        /// 获取或设置请求可以持续的最长时间，超过该时间后将报告激活实例“已阻塞”。
        /// </summary>
        public TimeSpan MaxRequestProcessingTime { get; set; } = DEFAULT_MAX_REQUEST_PROCESSING_TIME;

        /// <summary>
        /// <see cref="MaxRequestProcessingTime"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_MAX_REQUEST_PROCESSING_TIME = TimeSpan.FromHours(2);

        /// <summary>
        /// 获取或设置一个值，指示是否假定所有主机支持完全相同的 Grain 接口和类。
        /// </summary>
        /// <remarks>
        /// 仅用于测试。
        /// </remarks>
        public bool AssumeHomogenousSilosForTesting { get; set; } = false;

        /// <summary>
        /// 获取或设置 Silo 继续关闭之前等待重新路由排队消息的时间。
        /// </summary>
        [Obsolete("Unused, will be removed in a future version.")]
        public TimeSpan ShutdownRerouteTimeout { get; set; } = DEFAULT_SHUTDOWN_REROUTE_TIMEOUT;

        /// <summary>
        /// <see cref="ShutdownRerouteTimeout"/> 的默认值。
        /// </summary>
        [Obsolete("Unused, will be removed in a future version.")]
        public static readonly TimeSpan DEFAULT_SHUTDOWN_REROUTE_TIMEOUT = TimeSpan.Zero;

        /// <summary>
        /// 获取或设置将内部系统请求视为失败之前的默认超时时间。
        /// </summary>
        public TimeSpan SystemResponseTimeout
        {
            get { return Debugger.IsAttached ? ResponseTimeoutWithDebugger : this.systemResponseTimeout; }
            set { this.systemResponseTimeout = value; }
        }

        /// <summary>
        /// 获取或设置分析当前正在执行的激活实例工作负载的时间间隔。
        /// </summary>
        public TimeSpan GrainWorkloadAnalysisPeriod { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设置正在执行的请求被视为慢请求之前的时间。
        /// </summary>
        /// <remarks>
        /// 此值应比响应超时时间至少短 <see cref="GrainWorkloadAnalysisPeriod"/>，也就是说，应在 <see cref="MessagingOptions.ResponseTimeout"/> 之前留出至少这么长的时间，
        /// 从而在客户端超时之前，有足够的时间检测长时间运行的请求并向客户端发送状态消息。
        /// </remarks>
        public TimeSpan RequestProcessingWarningTime { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// 获取或设置已入队请求被视为延迟请求之前的时间。
        /// </summary>
        /// <remarks>
        /// 此值应比响应超时时间至少短 <see cref="GrainWorkloadAnalysisPeriod"/>，也就是说，应在 <see cref="MessagingOptions.ResponseTimeout"/> 之前留出至少这么长的时间，
        /// 从而在客户端超时之前，有足够的时间检测延迟请求并向客户端发送状态消息。
        /// </remarks>
        public TimeSpan RequestQueueDelayWarningTime { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// 获取或设置在 MessageCenter 和 OutboundMessageQueue 停止之前，等待所有排队消息发送到 OutboundMessageQueue 的时长。
        /// </summary>
        public TimeSpan WaitForMessageToBeQueuedForOutboundTime { get; set; } = DEFAULT_WAIT_FOR_MESSAGE_TO_BE_QUEUED_FOR_OUTBOUND_TIME;

        /// <summary>
        /// <see cref="WaitForMessageToBeQueuedForOutboundTime"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_WAIT_FOR_MESSAGE_TO_BE_QUEUED_FOR_OUTBOUND_TIME = TimeSpan.Zero;

        /// <summary>
        /// 获取或设置 Grain 放置操作允许完成的最长时间。
        /// </summary>
        /// <remarks>
        /// 此超时适用于包括重试在内的整个放置操作。
        /// 如果超过该超时时间，放置操作将失败并抛出 <see cref="TimeoutException"/>。
        /// </remarks>
        public TimeSpan PlacementTimeout { get; set; } = DEFAULT_PLACEMENT_TIMEOUT;

        /// <summary>
        /// <see cref="PlacementTimeout"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_PLACEMENT_TIMEOUT = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置 Grain 放置操作的最大重试次数。
        /// </summary>
        /// <remarks>
        /// 值为 <c>0</c> 时禁用重试。
        /// </remarks>
        public int PlacementMaxRetries { get; set; } = DEFAULT_PLACEMENT_MAX_RETRIES;

        /// <summary>
        /// <see cref="PlacementMaxRetries"/> 的默认值。
        /// </summary>
        public const int DEFAULT_PLACEMENT_MAX_RETRIES = 3;

        /// <summary>
        /// 获取或设置放置重试之间的初始延迟。
        /// </summary>
        /// <remarks>
        /// 后续重试采用指数退避，此值为基础延迟。
        /// </remarks>
        public TimeSpan PlacementRetryBaseDelay { get; set; } = DEFAULT_PLACEMENT_RETRY_BASE_DELAY;

        /// <summary>
        /// <see cref="PlacementRetryBaseDelay"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_PLACEMENT_RETRY_BASE_DELAY = TimeSpan.FromMilliseconds(100);
    }
}
