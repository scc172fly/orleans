using System;
using System.Diagnostics;

namespace Orleans.Configuration
{
    /// <summary>
    /// 指定客户端和 silo通用的全球信息消息选项。
    /// </summary>
    public abstract class MessagingOptions
    {
        /// <summary>
        /// <see cref="ResponseTimeout"/> 值。
        /// </summary>
        private TimeSpan _responseTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 在假定请求失败之前获取或设置默认超时 。
        /// </summary>
        /// <seealso cref="ResponseTimeoutWithDebugger"/>
        /// <value>请求默认将在30秒后暂停执行。</value>
        public TimeSpan ResponseTimeout
        {
            get { return Debugger.IsAttached ? ResponseTimeoutWithDebugger : _responseTimeout; }
            set { this._responseTimeout = value; }
        }

        /// <summary>
        /// 获取或设置连接调试器时使用的有效的 <see cref="ResponseTimeout"/> 值。
        /// </summary>
        /// <value>请求将在30分钟后暂停,但如默认,调试器被附在后面。</value>
        public TimeSpan ResponseTimeoutWithDebugger { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 获取或设定一个值,显示信件过期后是否即未发送时是否应删除
        /// 发送者超时前到达目的地。
        /// </summary>
        /// <value>信件一旦过期,默认会被丢弃。</value>
        public bool DropExpiredMessages { get; set; } = true;

        /// <summary>
        /// 信件发送尝试的最大次数将被重新搜索 。
        /// </summary>
        internal const short DEFAULT_MAX_MESSAGE_SEND_RETRIES = 1;

        /// <summary>
        /// 信件信头的最大大小, 以字节为单位。 运行时间将强制关闭连接
        /// 如果信头大小大于此值,则该信头的大小大于此值。
        /// </summary>
        /// <value>最大信头大小默认为 10 MB。</value>
        public int MaxMessageHeaderSize { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// 信件的最大体积, 以字节为单位。 运行时间将强制关闭连接
        /// 如果身体大小大于此值,则该数值为“体积”。
        /// </summary>
        /// <value>最大信息体积大小默认为 100 MB。</value>
        public int MaxMessageBodySize { get; set; } = 100 * 1024 * 1024;

        /// <summary>
        /// 获取 <see cref="ResponseTimeout"/> 属性的响应超时,不进行调试器检查。
        /// </summary>
        internal TimeSpan ConfiguredResponseTimeout => _responseTimeout;

        /// <summary>
        /// 请求时间过后是否尝试取消请求 。
        /// </summary>
        /// <remarks>
        /// 取消请求可能涉及向 silo 目标所在的 grain.
        /// 默认为 <see langword="false"/>。
        /// </remarks>
        public bool CancelRequestOnTimeout { get; set; }

        /// <summary>
        /// 当信号发出取消时是否立即取消本地电话 (<see langword="false"/>)
        /// 而不是等待被传唤者在电话被考虑取消之前确认取消(<see langword="true"/>)。
        /// </summary>
        /// <remarks>
        /// 默认为 <see langword="false"/>。
        /// </remarks>
        public bool WaitForCancellationAcknowledgement { get; set; }

        /// <summary>
        /// 在收到关于未知请求的状态更新时,是否应当尝试取消请求。
        /// </summary>
        /// <remarks>
        /// 一项未知请求的最新情况很可能表明,该请求先前被暂停,后来被撤销。
        /// 取消请求可能涉及向 silo 目标所在的 grain.
        /// 如果远程被调用者不支持取消, 此设置无效 。
        /// 默认为 <see langword="false"/>。
        /// </remarks>
        public bool CancelUnknownRequestOnStatusUpdate { get; set; }
    }
}
