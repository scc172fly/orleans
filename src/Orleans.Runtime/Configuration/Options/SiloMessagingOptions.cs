using System;
using System.Diagnostics;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 指定与 silo 相关的全球消息选项。
    /// </summary>
    public class SiloMessagingOptions : MessagingOptions
    {
        /// <summary>
        /// <see cref="SystemResponseTimeout"/>.
        /// </summary>
        private TimeSpan systemResponseTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置平行队列和随身线条的数量, silo 用来发送外送
        /// 向其他 silos.
        /// 如果未指定此属性, 则使用 System.Environment.ProcessorCount 。
        /// </summary>
        public int SiloSenderQueues { get; set; }

        /// <summary>
        /// 获取或设置 silo 网关用来发送外向的平行队列和随行线索的数量
        ///  向与之有关的客户发出信息(请求、答复和通知)。
        ///  如果未指定此属性, 则使用 System.Environment.ProcessorCount 。
        /// </summary>
        public int GatewaySenderQueues { get; set; }

        /// <summary>
        /// 获取或设置从一个信件中转发消息的最大次数 silo 对另一个。
        /// 当 silos 失败且成员不稳定时,在运行时间内部将转发用作回收机制。
        /// 在这种时候,电文可能无法正确传递到目的地,在拒绝之前,要多次尝试转发这类电文。
        /// </summary>
        public int MaxForwardCount { get; set; } = 2;

        /// <summary>
        /// 获取或设定网关在丢弃断开的客户端前要等待的时间段 。
        /// </summary>
        public TimeSpan ClientDropTimeout { get; set; } = Constants.DEFAULT_CLIENT_DROP_TIMEOUT;

        /// <summary>
        /// 获取或设置更新连接客户端列表的间隔 。
        /// </summary>
        public TimeSpan ClientRegistrationRefresh { get; set; } = DEFAULT_CLIENT_REGISTRATION_REFRESH;

        /// <summary>
        /// <see cref="ClientRegistrationRefresh"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CLIENT_REGISTRATION_REFRESH = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 获取或设定连接网关在通知相关客户端后再继续使用
        /// 关闭进程
        /// </summary>
        public TimeSpan ClientGatewayShutdownNotificationTimeout { get; set; } = DEFAULT_CLIENT_GW_NOTIFICATION_TIMEOUT;

        /// <summary>
        /// <see cref="ClientGatewayShutdownNotificationTimeout"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CLIENT_GW_NOTIFICATION_TIMEOUT = TimeSpan.Zero;

        /// <summary>
        /// 获取或设定待决请求的每 grain 阈值。超过此阈值时生成警告 。
        /// </summary>
        public int MaxEnqueuedRequestsSoftLimit { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_SOFT_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsSoftLimit"/>的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_SOFT_LIMIT = 0;

        /// <summary>
        /// 获取或设定待决请求的 grain 阈值。超过此限时,请求被拒绝。
        /// </summary>
        public int MaxEnqueuedRequestsHardLimit { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_HARD_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsHardLimit"/>的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_HARD_LIMIT = 0;

        /// <summary>
        /// 获取或设定待决无国籍工人申请的 grain 阈值。超过警告后生成警告 。
        /// </summary>
        public int MaxEnqueuedRequestsSoftLimit_StatelessWorker { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_SOFT_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsSoftLimit_StatelessWorker"/>的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_SOFT_LIMIT = 0;

        /// <summary>
        /// 获得或设定对无国籍工人的待决申请的 grain 阈值,超过要求时被拒绝。
        /// </summary>
        public int MaxEnqueuedRequestsHardLimit_StatelessWorker { get; set; } = DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_HARD_LIMIT;

        /// <summary>
        /// <see cref="MaxEnqueuedRequestsHardLimit_StatelessWorker"/>的默认值。
        /// </summary>
        public const int DEFAULT_MAX_ENQUEUED_REQUESTS_STATELESS_WORKER_HARD_LIMIT = 0;

        /// <summary>
        /// 获取或设定请求在被“封堵”的激活报告前可以接受的最大时间
        /// </summary>
        public TimeSpan MaxRequestProcessingTime { get; set; } = DEFAULT_MAX_REQUEST_PROCESSING_TIME;

        /// <summary>
        /// <see cref="MaxRequestProcessingTime"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_MAX_REQUEST_PROCESSING_TIME = TimeSpan.FromHours(2);

        /// <summary>
        /// 获取或设定一个值,以显示是否假设所有主机在它们支持的 grain 接口和类别方面都相同。
        /// </summary>
        /// <remarks>
        /// 仅用于测试。
        /// </remarks>
        public bool AssumeHomogenousSilosForTesting { get; set; } = false;

        /// <summary>
        /// 获取或设置 silo 在继续关闭前要等待更改队列信件的路径的时间段 。 
        /// </summary>
        [Obsolete("Unused, will be removed in a future version.")]
        public TimeSpan ShutdownRerouteTimeout { get; set; } = DEFAULT_SHUTDOWN_REROUTE_TIMEOUT;

        /// <summary>
        /// <see cref="ShutdownRerouteTimeout"/>的默认值。
        /// </summary>
        [Obsolete("Unused, will be removed in a future version.")]
        public static readonly TimeSpan DEFAULT_SHUTDOWN_REROUTE_TIMEOUT = TimeSpan.Zero;

        /// <summary>
        /// 在内部系统请求被假定为失败之前,获取或设置默认超时 。
        /// </summary>
        public TimeSpan SystemResponseTimeout
        {
            get { return Debugger.IsAttached ? ResponseTimeoutWithDebugger : this.systemResponseTimeout; }
            set { this.systemResponseTimeout = value; }
        }

        /// <summary>
        /// 分析当前执行启动工作量之间的时间间隔或设定时间。
        /// </summary>
        public TimeSpan GrainWorkloadAnalysisPeriod { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设定当前执行请求被认为缓慢的时间间隔。
        /// </summary>
        /// <remarks>
        /// 这一点至少应设定为值 <see cref="GrainWorkloadAnalysisPeriod"/> 短于 <see cref="MessagingOptions.ResponseTimeout"/>
        /// 以便有足够的时间发现长期请求,并在客户时间结束前向客户发送状况信息。
        /// </remarks>
        public TimeSpan RequestProcessingWarningTime { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// 获取或设定被排队的请求被认为延迟的时间。
        /// </summary>
        /// <remarks>
        /// 这一点至少应设定为值 <see cref="GrainWorkloadAnalysisPeriod"/> 短于 <see cref="MessagingOptions.ResponseTimeout"/>
        /// 以便有足够的时间发现延迟的请求,并在客户时间结束之前向客户发送状况信息。
        /// </remarks>
        public TimeSpan RequestQueueDelayWarningTime { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// 获取或设置等待所有队列信件的时间 OutboundMessageQueue 之前 MessageCenter 停止和 OutboundMessageQueue 停下来。
        /// </summary>
        public TimeSpan WaitForMessageToBeQueuedForOutboundTime { get; set; } = DEFAULT_WAIT_FOR_MESSAGE_TO_BE_QUEUED_FOR_OUTBOUND_TIME;

        /// <summary>
        /// <see cref="WaitForMessageToBeQueuedForOutboundTime"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_WAIT_FOR_MESSAGE_TO_BE_QUEUED_FOR_OUTBOUND_TIME = TimeSpan.Zero;

        /// <summary>
        /// 获取或设定 grain 安置作业完成的最长时间。
        /// </summary>
        /// <remarks>
        /// 这一超时适用于整个安置行动,包括再处理。
        /// 如果超时,安置操作将因一个 <see cref="TimeoutException"/>而失败。
        /// </remarks>
        public TimeSpan PlacementTimeout { get; set; } = DEFAULT_PLACEMENT_TIMEOUT;

        /// <summary>
        /// <see cref="PlacementTimeout"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_PLACEMENT_TIMEOUT = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设定 grain 安置作业的尝试重试的最大次数。
        /// </summary>
        /// <remarks>
        /// 值 <c>0</c> 将禁用重试。
        /// </remarks>
        public int PlacementMaxRetries { get; set; } = DEFAULT_PLACEMENT_MAX_RETRIES;

        /// <summary>
        /// <see cref="PlacementMaxRetries"/>的默认值。
        /// </summary>
        public const int DEFAULT_PLACEMENT_MAX_RETRIES = 3;

        /// <summary>
        /// 获取或设置在重新定位尝试之间的初始延迟 。
        /// </summary>
        /// <remarks>
        /// 其后的重试使用指数反向后退作为基底延迟。
        /// </remarks>
        public TimeSpan PlacementRetryBaseDelay { get; set; } = DEFAULT_PLACEMENT_RETRY_BASE_DELAY;

        /// <summary>
        /// <see cref="PlacementRetryBaseDelay"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_PLACEMENT_RETRY_BASE_DELAY = TimeSpan.FromMilliseconds(100);
    }
}
