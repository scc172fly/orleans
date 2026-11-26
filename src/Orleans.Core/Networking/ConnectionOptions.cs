using System;
using Orleans.Runtime.Messaging;

namespace Orleans.Configuration
{
    /// <summary>
    /// 连接选项。
    /// </summary>
    public class ConnectionOptions
    {
        /// <summary>
        /// 获取或设置网络协议版本以与它谈判。
        /// </summary>
        public NetworkProtocolVersion ProtocolVersion { get; set; } = NetworkProtocolVersion.Version1;

        /// <summary>
        /// 获取或设置每个端点要维持的连接数。
        /// </summary>
        public int ConnectionsPerEndpoint { get; set; } = 1;

        /// <summary>
        /// 获取或设定连接尝试失败后等待的时间,然后重试连接。
        /// </summary>
        public TimeSpan ConnectionRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 在设定连接已失败之前获取或设置超时 。
        /// </summary>
        public TimeSpan OpenConnectionTimeout { get; set; } = DEFAULT_OPENCONNECTION_TIMEOUT;

        /// <summary>
        /// <see cref="OpenConnectionTimeout"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_OPENCONNECTION_TIMEOUT = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设置优雅关闭连接的超时 。
        /// 如果超时, 连接将被强制关闭 。
        /// </summary>
        public TimeSpan CloseConnectionTimeout { get; set; } = DEFAULT_CLOSECONNECTION_TIMEOUT;

        /// <summary>
        /// <see cref="CloseConnectionTimeout"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CLOSECONNECTION_TIMEOUT = TimeSpan.FromSeconds(30);
    }
}
