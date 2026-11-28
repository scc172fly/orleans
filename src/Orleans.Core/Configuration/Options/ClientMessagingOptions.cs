using System.Net;
using System.Net.Sockets;

namespace Orleans.Configuration
{
    /// <summary>
    /// 指定与客户端相关的全局消息传递选项。
    /// </summary>
    public class ClientMessagingOptions : MessagingOptions
    {
        /// <summary>
        /// 获取或设置客户端在客户端到网关通信
        /// 协议中使用的 Grain 分桶总数。此协议将 Grain 映射到桶，再将桶映射到网关连接，以实现
        /// Grain 与网关之间的粘滞性（发往同一 Grain 的消息经由同一网关发送，同时将 Grain 均匀分布到各网关）。
        /// 此数值应为预期网关连接数量的约 10 至 100 倍。
        /// 如果未指定此设置，则使用 Math.Pow(2, 13)。
        /// </summary>
        public int ClientSenderBuckets { get; set; } = DEFAULT_CLIENT_SENDER_BUCKETS;

        /// <summary>
        /// <see cref="ClientSenderBuckets"/> 的默认值。
        /// </summary>
        /// <value>8192</value>
        public const int DEFAULT_CLIENT_SENDER_BUCKETS = 8192;

        /// <summary>
        /// 获取或设置确定适当客户端标识时优先使用的 <see cref="AddressFamily"/>。
        /// </summary>
        public AddressFamily PreferredFamily { get; set; } = DEFAULT_PREFERRED_FAMILY;

        /// <summary>
        /// <see cref="PreferredFamily"/> 的默认值。
        /// </summary>
        /// <value><see cref="AddressFamily.InterNetwork"/></value>
        public const AddressFamily DEFAULT_PREFERRED_FAMILY = AddressFamily.InterNetwork;

        /// <summary>
        /// 获取或设置用于确定此计算机 IP 地址的网络接口名称。
        /// </summary>
        public string? NetworkInterfaceName { get; set; }

        /// <summary>
        /// 获取或设置集群客户端使用的 IP 地址。
        /// </summary>
        public IPAddress? LocalAddress { get; set; }
    }
}
