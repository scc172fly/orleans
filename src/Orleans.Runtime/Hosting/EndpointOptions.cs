using System.Net;
using Microsoft.Extensions.Configuration;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 Silo 终结点选项。
    /// </summary>
    public class EndpointOptions
    {
        private IPAddress advertisedIPAddress = null!;
        private int siloPort = DEFAULT_SILO_PORT;

        /// <summary>
        /// 用于集群通信的 IP 地址。
        /// </summary>
        public IPAddress AdvertisedIPAddress
        {
            get => advertisedIPAddress;
            set
            {
                if (value is null)
                {
                    throw new OrleansConfigurationException(
                        $"No listening address specified. Use {nameof(Hosting.ISiloBuilder)}.{nameof(Hosting.EndpointOptionsExtensions.ConfigureEndpoints)}(...) "
                        + $"to configure endpoints and ensure that {nameof(AdvertisedIPAddress)} is set.");
                }

                if (Equals(value, IPAddress.Any)
                    || Equals(value, IPAddress.IPv6Any)
                    || Equals(value, IPAddress.None)
                    || Equals(value, IPAddress.IPv6None))
                {
                    throw new OrleansConfigurationException(
                        $"Invalid value specified for {nameof(AdvertisedIPAddress)}. The value was {value}");
                }

                advertisedIPAddress = value;
            }
        }

        /// <summary>
        /// 获取或设置此 Silo 用于 Silo 间通信的端口。
        /// </summary>
        public int SiloPort
        {
            get => siloPort;
            set
            {
                if (value == 0)
                {
                    throw new OrleansConfigurationException(
                        $"No listening port specified. Use {nameof(Hosting.ISiloBuilder)}.{nameof(Hosting.EndpointOptionsExtensions.ConfigureEndpoints)}(...) "
                        + $"to configure endpoints and ensure that {nameof(SiloPort)} is set.");
                }

                siloPort = value;
            }
        }

        /// <summary>
        /// <see cref="SiloPort"/> 的默认值。
        /// </summary>
        public const int DEFAULT_SILO_PORT = 11111;

        /// <summary>
        /// 获取或设置此 Silo 用于 Silo 与客户端（网关）通信的端口。指定 0 可禁用网关功能。
        /// </summary>
        public int GatewayPort { get; set; } = DEFAULT_GATEWAY_PORT;

        /// <summary>
        /// <see cref="GatewayPort"/> 的默认值。
        /// </summary>
        public const int DEFAULT_GATEWAY_PORT = 30000;

        /// <summary>
        /// 获取或设置用于监听 Silo 间通信的终结点。
        /// 如果未设置，则默认为 <see cref="AdvertisedIPAddress"/> + <see cref="SiloPort"/>。
        /// </summary>
        public IPEndPoint? SiloListeningEndpoint { get; set; }

        /// <summary>
        /// 获取或设置用于监听客户端到 Silo 通信的终结点。
        /// 如果未设置，则默认为 <see cref="AdvertisedIPAddress"/> + <see cref="GatewayPort"/>。
        /// </summary>
        public IPEndPoint? GatewayListeningEndpoint { get; set; }

        internal IPEndPoint GetPublicSiloEndpoint() => new(AdvertisedIPAddress, SiloPort);

        internal IPEndPoint? GetPublicProxyEndpoint()
        {
            var gatewayPort = GatewayPort != 0 ? GatewayPort : GatewayListeningEndpoint?.Port ?? 0;
            return gatewayPort != 0 ? new(AdvertisedIPAddress, gatewayPort) : null;
        }

        internal IPEndPoint GetListeningSiloEndpoint() => SiloListeningEndpoint ?? GetPublicSiloEndpoint();

        internal IPEndPoint? GetListeningProxyEndpoint() => GatewayListeningEndpoint ?? GetPublicProxyEndpoint();

        internal void Bind(IConfiguration cfg)
        {
            if (ConfigurationValueParser.TryParseValue<int>(cfg, nameof(GatewayPort), out var gatewayPort))
            {
                GatewayPort = gatewayPort;
            }

            if (ConfigurationValueParser.TryParseValue<int>(cfg, nameof(SiloPort), out var siloPort))
            {
                SiloPort = siloPort;
            }

            if (ConfigurationValueParser.TryParseValue<IPAddress>(cfg, nameof(AdvertisedIPAddress), out var aip))
            {
                AdvertisedIPAddress = aip;
            }

            if (ConfigurationValueParser.TryParseIPEndPoint(cfg, nameof(SiloListeningEndpoint), out var sle))
            {
                SiloListeningEndpoint = sle;
            }

            if (ConfigurationValueParser.TryParseIPEndPoint(cfg, nameof(GatewayListeningEndpoint), out var gle))
            {
                GatewayListeningEndpoint = gle;
            }
        }
    }
}
