using System.Net;
using System.Net.Sockets;

namespace Orleans.Configuration
{
    /// <summary>
    /// 指定与客户端相关的全球信息选项。
    /// </summary>
    public class ClientMessagingOptions : MessagingOptions
    {
        /// <summary>
        ///  获取或设定客户在客户到门户通信中使用的 grain 桶总数
        ///  在本议定书中, grains 绘制成桶,将水桶绘制成桶,并绘制成网关连接图,以便粘贴
        ///  联 年 月 日 月 日 月 月 日 grain 网关(同一地址) grain 你们应当平坐着,平坐著, grains 跨网关)
        ///  这个数字应比预期网关连接量多10至100倍。
        ///  如果未指定此属性,则使用 Math.Pow(2、13) 。
        /// </summary>
        public int ClientSenderBuckets { get; set; } = DEFAULT_CLIENT_SENDER_BUCKETS;

        /// <summary>
        /// <see cref="ClientSenderBuckets"/>的默认值。
        /// </summary>
        /// <value>8192</value>
        public const int DEFAULT_CLIENT_SENDER_BUCKETS = 8192;

        /// <summary>
        /// 获取或设置首选的 <see cref="AddressFamily"/> ,用于确定适当的客户身份。
        /// </summary>
        public AddressFamily PreferredFamily { get; set; } = DEFAULT_PREFERRED_FAMILY;

        /// <summary>
        /// <see cref="PreferredFamily"/>的默认值。
        /// </summary>
        /// <value><see cref="AddressFamily.InterNetwork"/></value>
        public const AddressFamily DEFAULT_PREFERRED_FAMILY = AddressFamily.InterNetwork;

        /// <summary>
        /// 获取或设置用于为此机器绘制IP地址的网络界面名称。
        /// </summary>
        public string? NetworkInterfaceName { get; set; }

        /// <summary>
        /// 获取或设置分组客户端所使用的 IP 地址 。
        /// </summary>
        public IPAddress? LocalAddress { get; set; }
    }
}
