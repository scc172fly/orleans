using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Connections.Transport.Security;

namespace Orleans.Connections.Transport;

/// <summary>
/// 将 TLS 添加到全部的中元软件 <see cref="MessageTransport"/> 设置 <see cref="MessageTransportConnector"/>.
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
/// 将 TLS 添加到全部的中元软件 <see cref="MessageTransport"/> 设置 <see cref="MessageTransportListener"/>.
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
