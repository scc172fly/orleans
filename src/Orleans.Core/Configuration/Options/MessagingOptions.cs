using System;
using System.Diagnostics;

namespace Orleans.Configuration
{
    /// <summary>
    /// 指定客户端与 Silo 通用的全局消息传递选项。
    /// </summary>
    public abstract class MessagingOptions
    {
        /// <summary>
        /// <see cref="ResponseTimeout"/> 的值。
        /// </summary>
        private TimeSpan _responseTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置认定请求失败之前的默认超时时间。
        /// </summary>
        /// <seealso cref="ResponseTimeoutWithDebugger"/>
        /// <value>默认情况下，请求在 30 秒后超时。</value>
        public TimeSpan ResponseTimeout
        {
            get { return Debugger.IsAttached ? ResponseTimeoutWithDebugger : _responseTimeout; }
            set { this._responseTimeout = value; }
        }

        /// <summary>
        /// 获取或设置附加调试器时实际使用的 <see cref="ResponseTimeout"/> 值。
        /// </summary>
        /// <value>附加调试器时，请求默认在 30 分钟后超时。</value>
        public TimeSpan ResponseTimeoutWithDebugger { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 获取或设置指示消息是否应在过期后丢弃的值，即消息在发送方超时之前
        /// 未能送达目标的情况。
        /// </summary>
        /// <value>默认情况下，消息过期后将被丢弃。</value>
        public bool DropExpiredMessages { get; set; } = true;

        /// <summary>
        /// 消息发送尝试的最大重试次数。
        /// </summary>
        internal const short DEFAULT_MAX_MESSAGE_SEND_RETRIES = 1;

        /// <summary>
        /// 消息头的最大大小（以字节为单位）。运行时将强制关闭连接，
        /// 如果消息头的大小超过此值。
        /// </summary>
        /// <value>消息头的默认最大大小为 10 MB。</value>
        public int MaxMessageHeaderSize { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// 消息体的最大大小（以字节为单位）。运行时将强制关闭连接，
        /// 如果消息体的大小超过此值。
        /// </summary>
        /// <value>消息体的默认最大大小为 100 MB。</value>
        public int MaxMessageBodySize { get; set; } = 100 * 1024 * 1024;

        /// <summary>
        /// 获取 <see cref="ResponseTimeout"/> 属性的底层响应超时时间，不考虑调试器检查。
        /// </summary>
        internal TimeSpan ConfiguredResponseTimeout => _responseTimeout;

        /// <summary>
        /// 是否应在请求超时时尝试取消该请求。
        /// </summary>
        /// <remarks>
        /// 取消请求可能涉及向承载目标 Grain 的 Silo 发送取消消息。
        /// 默认为 <see langword="false"/>。
        /// </remarks>
        public bool CancelRequestOnTimeout { get; set; }

        /// <summary>
        /// 是否在发出取消信号时立即取消本地调用（<see langword="false"/>），
        /// 而不是等待被调用方确认取消后才认定调用已取消（<see langword="true"/>）。
        /// </summary>
        /// <remarks>
        /// 默认为 <see langword="false"/>。
        /// </remarks>
        public bool WaitForCancellationAcknowledgement { get; set; }

        /// <summary>
        /// 是否应在收到未知请求的状态更新时尝试取消该请求。
        /// </summary>
        /// <remarks>
        /// 未知请求的状态更新通常意味着该请求此前已超时，随后被丢弃。
        /// 取消请求可能涉及向承载目标 Grain 的 Silo 发送取消消息。
        /// 如果远程被调用方不支持取消，则此设置不起作用。
        /// 默认为 <see langword="false"/>。
        /// </remarks>
        public bool CancelUnknownRequestOnStatusUpdate { get; set; }
    }
}
