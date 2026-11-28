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
        /// 获取或设置用于协商的网络协议版本。
        /// </summary>
        public NetworkProtocolVersion ProtocolVersion { get; set; } = NetworkProtocolVersion.Version1;

        /// <summary>
        /// 获取或设置为每个终结点维护的连接数量。
        /// </summary>
        public int ConnectionsPerEndpoint { get; set; } = 1;

        /// <summary>
        /// 获取或设置连接尝试失败后、再次尝试连接之前的等待时长。
        /// </summary>
        public TimeSpan ConnectionRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 获取或设置判定连接打开失败之前的超时时长。
        /// </summary>
        public TimeSpan OpenConnectionTimeout { get; set; } = DEFAULT_OPENCONNECTION_TIMEOUT;

        /// <summary>
        /// <see cref="OpenConnectionTimeout"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_OPENCONNECTION_TIMEOUT = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 获取或设置优雅关闭连接的超时时长。
        /// 超时后，将强制关闭连接。
        /// </summary>
        public TimeSpan CloseConnectionTimeout { get; set; } = DEFAULT_CLOSECONNECTION_TIMEOUT;

        /// <summary>
        /// <see cref="CloseConnectionTimeout"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CLOSECONNECTION_TIMEOUT = TimeSpan.FromSeconds(30);
    }
}
