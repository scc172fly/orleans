using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Orleans.Connections.Transport.Sockets;

internal sealed class TcpMessageTransportListenerOptions
{
    public IPEndPoint? Endpoint { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 监听 TCP 连接的<see cref="MessageTransportListener"/> 。
/// </summary>
internal sealed class TcpMessageTransportListener : MessageTransportListener
{
    private readonly IOptionsMonitor<TcpMessageTransportOptions> _tcpOptions;
    private readonly IOptionsMonitor<TcpMessageTransportListenerOptions> _listenerOptions;
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _closingCts = new();
    private Socket? _listenSocket;
    private bool _disposed;

    internal TcpMessageTransportListener(string endpointName, IOptionsMonitor<TcpMessageTransportOptions> tcpOptions, IOptionsMonitor<TcpMessageTransportListenerOptions> listenerOptions, ILoggerFactory loggerFactory)
    {
        Debug.Assert(loggerFactory != null);
        _listenerOptions = listenerOptions;
        _tcpOptions = tcpOptions;
        ListenerName = endpointName;
        Logger = loggerFactory.CreateLogger("Orleans.Connections.Transport.Sockets");
    }

    private ILogger Logger { get; }

    /// <inheritdoc/>
    public override FeatureCollection Features { get; } = new FeatureCollection();

    /// <inheritdoc/>
    public override bool IsValid => _listenerOptions.Get(ListenerName).Enabled;

    /// <inheritdoc/>
    public override string ListenerName { get; }

    private Socket CreateListenSocket()
    {
        var options = _tcpOptions.Get(ListenerName);
        var listenerOptions = _listenerOptions.Get(ListenerName);
        var listenSocket = new Socket(listenerOptions.Endpoint!.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            listenSocket.LingerState = options.LingerOption;
            listenSocket.NoDelay = options.NoDelay;
            listenSocket.ConfigureKeepAlive(options, Logger);

            listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            if (options.FastPath)
            {
                listenSocket.EnableFastPath(noDelay: options.NoDelay);
            }

            // IPv6 任何事项都预计将与 IPv6 和 IPv4 同时绑定
            if (listenerOptions.Endpoint is IPEndPoint ip && ip.Address == IPAddress.IPv6Any)
            {
                listenSocket.DualMode = options.DualMode;
            }

            return listenSocket;
        }
        catch
        {
            listenSocket.Dispose();
            throw;
        }
    }

    private void OnAcceptSocket(Socket socket)
    {
        var options = _tcpOptions.Get(ListenerName);
        socket.NoDelay = options.NoDelay;
        socket.ConfigureKeepAlive(options, Logger);
    }

    public override ValueTask BindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_listenSocket != null)
        {
            throw new InvalidOperationException("Transport already bound");
        }

        Socket? listenSocket = CreateListenSocket();

        try
        {
            var listenerOptions = _listenerOptions.Get(ListenerName);
            listenSocket.Bind(listenerOptions.Endpoint!);
            listenSocket.Listen(512);

            _listenSocket = listenSocket;
            listenSocket = null;
            return default;
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new AddressInUseException(e.Message, e);
        }
        finally
        {
            listenSocket?.Dispose();
        }
    }

    public override async ValueTask<MessageTransport?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        using var ct = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closingCts.Token);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var acceptSocket = await _listenSocket!.AcceptAsync(ct.Token).ConfigureAwait(false);
                try
                {
                    OnAcceptSocket(acceptSocket);
                }
                catch
                {
                    acceptSocket.Dispose();
                    throw;
                }

                return await SocketMessageTransport.CreateAndStartAsync(acceptSocket, Logger).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 优雅的终止
                return null;
            }
            catch (ObjectDisposedException)
            {
                // 电话是叫 UnbindAsync/DisposeAsync 退回就完
                return null;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.OperationAborted)
            {
                // 电话是叫 UnbindAsync/DisposeAsync 退回就完
                return null;
            }
            catch (SocketException)
            {
                // 连接在积压时被重置, 所以我们再试一次。
                SocketsLog.ConnectionReset(Logger, connection: "(null)");
            }
        }

        return null;
    }

    private async ValueTask UnbindCoreAsync()
    {
        Socket? listenSocket;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            listenSocket = _listenSocket;
        }

        await _closingCts.CancelAsync().ConfigureAwait(false);
        listenSocket?.Dispose();
    }

    public override ValueTask UnbindAsync(CancellationToken cancellationToken) => UnbindCoreAsync();

    public override async ValueTask DisposeAsync()
    {
        Socket? listenSocket;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            listenSocket = _listenSocket;
        }

        await _closingCts.CancelAsync().ConfigureAwait(false);
        listenSocket?.Dispose();
        _closingCts.Dispose();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
