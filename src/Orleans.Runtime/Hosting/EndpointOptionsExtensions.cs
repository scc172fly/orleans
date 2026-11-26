using System;
using System.Net;
using System.Net.Sockets;
using Orleans.Configuration;
using Orleans.Runtime.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置的扩展方法 <see cref="EndpointOptions"/>。
    /// </summary>
    public static class EndpointOptionsExtensions
    {
        /// <summary>
        /// 配置 silo的端点 。
        /// </summary>
        /// <param name="builder">要配置的主机构建器 。</param>
        /// <param name="advertisedIP">成员表格中公布的IPIP地址</param>
        /// <param name="siloPort">此处端口 silo 用途的用途 silo- 来...silo 通信。</param>
        /// <param name="gatewayPort">此处端口 silo 客户端到-silo 指定 0 来禁用网关功能 。</param>
        /// <param name="listenOnAnyHostAddress">设置为真实性, 以收听主机的所有 IP 地址, 而不是仅收听 advertiseIP。</param>
        /// <returns></returns>
        public static ISiloBuilder ConfigureEndpoints(
            this ISiloBuilder builder,
            IPAddress advertisedIP,
            int siloPort,
            int gatewayPort,
            bool listenOnAnyHostAddress = false)
        {
            builder.Configure<EndpointOptions>(options =>
            {
                options.AdvertisedIPAddress = advertisedIP;
                options.GatewayPort = gatewayPort;
                options.SiloPort = siloPort;

                if (listenOnAnyHostAddress)
                {
                    options.SiloListeningEndpoint = new IPEndPoint(IPAddress.Any, siloPort);
                    options.GatewayListeningEndpoint = new IPEndPoint(IPAddress.Any, gatewayPort);
                }
            });
            return builder;
        }

        /// <summary>
        /// 配置 silo的端点 。
        /// </summary>
        /// <param name="builder">要配置的主机构建器 。</param>
        /// <param name="hostname">主机名称 silo 正在运行。</param>
        /// <param name="siloPort">此处端口 silo 用途的用途 silo- 来...silo 通信。</param>
        /// <param name="gatewayPort">此处端口 silo 客户端到-silo 指定 0 来禁用网关功能 。</param>
        /// <param name="addressFamily">地址是要收听的家属。默认 IPv4 地址是家庭。</param>
        /// <param name="listenOnAnyHostAddress">设置为真实性, 以收听主机的所有 IP 地址, 而不是仅收听 advertiseIP。</param>
        /// <returns></returns>
        public static ISiloBuilder ConfigureEndpoints(
            this ISiloBuilder builder,
            string? hostname,
            int siloPort,
            int gatewayPort,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            bool listenOnAnyHostAddress = false)
        {
            var ip = ConfigUtilities.ResolveIPAddressOrDefault(hostname, null, addressFamily);

            if (ip == null)
            {
                throw new ArgumentException("Hostname '" + hostname + "' with subnet [] and family " + addressFamily + " is not a valid IP address or DNS name");
            }

            return builder.ConfigureEndpoints(ip, siloPort, gatewayPort, listenOnAnyHostAddress);
        }

        /// <summary>
        /// 配置 silo的端点 。
        /// </summary>
        /// <param name="builder">要配置的主机构建器 。</param>
        /// <param name="siloPort">此处端口 silo 用途的用途 silo- 来...silo 通信。</param>
        /// <param name="gatewayPort">此处端口 silo 客户端到-silo 指定 0 来禁用网关功能 。</param>
        /// <param name="addressFamily">地址是要收听的家属。默认 IPv4 地址是家庭。</param>
        /// <param name="listenOnAnyHostAddress">设置为真实性, 以收听主机的所有 IP 地址, 而不是仅收听 advertiseIP。</param>
        /// <returns></returns>
        public static ISiloBuilder ConfigureEndpoints(
            this ISiloBuilder builder,
            int siloPort,
            int gatewayPort,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            bool listenOnAnyHostAddress = false)
        {
            return builder.ConfigureEndpoints(null, siloPort, gatewayPort, addressFamily, listenOnAnyHostAddress);
        }
    }
}
