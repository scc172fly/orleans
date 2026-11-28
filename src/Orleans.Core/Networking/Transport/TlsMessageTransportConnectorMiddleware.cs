using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Connections.Transport.Security;

namespace Orleans.Connections.Transport;

/// <summary>
/// 为所有 <see cref="MessageTransport"/> 实例添加 TLS 的中间件，这些实例由 <see cref="MessageTransportConnector"/> 创建。
/// </summary>
internal sealed class TlsMessageTransportConnectorMiddleware : IMessageTransportConnectorMiddleware
{
    private readonly IOptionsMonitor<TlsOptions> _tlsOptions;
    private readonly ILoggerFactory _loggerFactory;

    public TlsMessageTransportConnectorMiddleware(IOptionsMonitor<TlsOptions> tlsOptions, ILoggerFactory loggerFactory)
    {
        _tlsOptions = tlsOptions;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc/>
    public MessageTransportConnector Apply(MessageTransportConnector transport) => new TlsMessageTransportConnector(transport, _tlsOptions, _loggerFactory);
}

/// <summary>
/// 为所有 <see cref="MessageTransport"/> 实例添加 TLS 的中间件，这些实例由 <see cref="MessageTransportListener"/> 创建。
/// </summary>
internal sealed class TlsMessageTransportListenerMiddleware : IMessageTransportListenerMiddleware
{
    private readonly IOptionsMonitor<TlsOptions> _tlsOptions;
    private readonly ILoggerFactory _loggerFactory;

    public TlsMessageTransportListenerMiddleware(IOptionsMonitor<TlsOptions> tlsOptions, ILoggerFactory loggerFactory)
    {
        _tlsOptions = tlsOptions;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc/>
    public MessageTransportListener Apply(MessageTransportListener input) => new TlsMessageTransportListener(input, _tlsOptions, _loggerFactory);
}
