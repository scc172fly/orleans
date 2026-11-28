using System;
using System.Net;
using System.Net.Sockets;
using Orleans.Configuration;
using Orleans.Runtime.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 <see cref="EndpointOptions"/> 的扩展方法。
    /// </summary>
    public static class EndpointOptionsExtensions
    {
        /// <summary>
        /// 配置 Silo 的终结点。
        /// </summary>
        /// <param name="builder">要配置的宿主构建器。</param>
        /// <param name="advertisedIP">要在成员资格表中公布的 IP 地址。</param>
        /// <param name="siloPort">此 Silo 用于 Silo 间通信的端口。</param>
        /// <param name="gatewayPort">此 Silo 用于客户端到 Silo（网关）通信的端口。指定 0 可禁用网关功能。</param>
        /// <param name="listenOnAnyHostAddress">设为 true 时，监听主机的所有 IP 地址，而不仅仅是 advertiseIP。</param>
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
        /// 配置 Silo 的终结点。
        /// </summary>
        /// <param name="builder">要配置的宿主构建器。</param>
        /// <param name="hostname">运行 Silo 的主机名称。</param>
        /// <param name="siloPort">此 Silo 用于 Silo 间通信的端口。</param>
        /// <param name="gatewayPort">此 Silo 用于客户端到 Silo（网关）通信的端口。指定 0 可禁用网关功能。</param>
        /// <param name="addressFamily">要监听的地址族。默认为 IPv4 地址族。</param>
        /// <param name="listenOnAnyHostAddress">设为 true 时，监听主机的所有 IP 地址，而不仅仅是 advertiseIP。</param>
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
        /// 配置 Silo 的终结点。
        /// </summary>
        /// <param name="builder">要配置的宿主构建器。</param>
        /// <param name="siloPort">此 Silo 用于 Silo 间通信的端口。</param>
        /// <param name="gatewayPort">此 Silo 用于客户端到 Silo（网关）通信的端口。指定 0 可禁用网关功能。</param>
        /// <param name="addressFamily">要监听的地址族。默认为 IPv4 地址族。</param>
        /// <param name="listenOnAnyHostAddress">设为 true 时，监听主机的所有 IP 地址，而不仅仅是 advertiseIP。</param>
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
