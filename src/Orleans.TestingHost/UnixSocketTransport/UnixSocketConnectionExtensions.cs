using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Connections.Transport;
using Orleans.Hosting;
using Orleans.Runtime.Messaging;

namespace Orleans.TestingHost.UnixSocketTransport;

/// <summary>
/// 用于配置 Orleans Unix 域套接字传输的扩展方法。
/// </summary>
public static class UnixSocketConnectionExtensions
{
    /// <summary>
    /// 配置 Silo 和网关连接使用 Unix 域套接字。
    /// </summary>
    /// <param name="siloBuilder">Silo 构建器。</param>
    /// <returns>提供的 Silo 构建器。</returns>
    public static ISiloBuilder UseUnixSocketConnection(this ISiloBuilder siloBuilder)
    {
        var services = siloBuilder.Services;
        services.RemoveAll<MessageTransportConnector>();
        services.RemoveAll<MessageTransportListener>();
        services.AddSingleton<MessageTransportConnector>(sp => new UnixDomainSocketMessageTransportConnector(
            sp.GetRequiredService<IOptions<UnixSocketConnectionOptions>>(),
            sp.GetRequiredService<ILoggerFactory>()));
        AddListener(services, GatewayConnectionListener.DefaultListenerName, static options => options.GetListeningProxyEndpoint());
        AddListener(services, SiloConnectionListener.DefaultListenerName, static options => options.GetListeningSiloEndpoint());
        return siloBuilder;
    }

    /// <summary>
    /// 配置客户端连接使用 Unix 域套接字。
    /// </summary>
    /// <param name="clientBuilder">客户端构建器。</param>
    /// <returns>提供的客户端构建器。</returns>
    public static IClientBuilder UseUnixSocketConnection(this IClientBuilder clientBuilder)
    {
        clientBuilder.Services.RemoveAll<MessageTransportConnector>();
        clientBuilder.Services.AddSingleton<MessageTransportConnector>(sp => new UnixDomainSocketMessageTransportConnector(
            sp.GetRequiredService<IOptions<UnixSocketConnectionOptions>>(),
            sp.GetRequiredService<ILoggerFactory>()));
        return clientBuilder;
    }

    private static void AddListener(
        IServiceCollection services,
        string listenerName,
        Func<EndpointOptions, System.Net.IPEndPoint?> getEndpoint)
    {
        services.AddSingleton<MessageTransportListener>(sp => new UnixDomainSocketMessageTransportListener(
            listenerName,
            sp.GetRequiredService<IOptionsMonitor<UnixDomainSocketMessageTransportListenerOptions>>(),
            sp.GetRequiredService<ILoggerFactory>()));
        services.AddOptions<UnixDomainSocketMessageTransportListenerOptions>(listenerName)
            .Configure<IOptions<EndpointOptions>, IOptions<UnixSocketConnectionOptions>>((listenerOptions, endpointOptions, connectionOptions) =>
            {
                var endpoint = getEndpoint(endpointOptions.Value);
                listenerOptions.Enabled = endpoint is not null;
                if (endpoint is not null)
                {
                    listenerOptions.Path = connectionOptions.Value.ConvertEndpointToPath(endpoint);
                }
            });
    }
}
