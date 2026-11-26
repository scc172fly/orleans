using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Connections.Transport;
using Orleans.Connections.Transport.Sockets;

namespace Orleans.TestingHost.UnixSocketTransport;

/// <summary>
/// 配置 Unix 域套接字信件传输听众。
/// </summary>
public class UnixDomainSocketMessageTransportListenerOptions
{
    /// <summary>
    /// 获取或设置套接字路径。默认是系统临时目录中唯一的路径。
    /// </summary>
    public string Path { get; set; } = CreateDefaultPath();

    /// <summary>
    /// 获取或设置一个值,以显示是否启用了听众。默认为 <see langword="true"/>。
    /// </summary>
    public bool Enabled { get; set; } = true;
    private static string CreateDefaultPath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"silo_{Guid.NewGuid():N}");
}

internal class UnixDomainSocketMessageTransportListener : MessageTransportListener
{
    private readonly CancellationTokenSource _closingCts = new();
    private Socket? _listenSocket;
    private string? _boundPath;
    private int _disposeStarted;
    private readonly IOptionsMonitor<UnixDomainSocketMessageTransportListenerOptions> _listenerOptions;

    internal UnixDomainSocketMessageTransportListener(
        string endpointName,
        IOptionsMonitor<UnixDomainSocketMessageTransportListenerOptions> listenerOptions,
        ILoggerFactory loggerFactory)
    {
        ListenerName = endpointName;
        _listenerOptions = listenerOptions;
        Logger = loggerFactory.CreateLogger("Orleans.Connections.Transport.Sockets");
    }

    protected ILogger Logger { get; }

    /// <inheritdoc/>
    public override FeatureCollection Features { get; } = new FeatureCollection();

    /// <inheritdoc/>
    public override bool IsValid => Socket.OSSupportsUnixDomainSockets && _listenerOptions.Get(ListenerName).Enabled;

    /// <inheritdoc/>
    public override string ListenerName { get; }

    protected virtual Socket CreateListenSocket()
    {
        var listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        return listenSocket;
    }

    public override ValueTask BindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_listenSocket != null)
        {
            throw new InvalidOperationException("Transport already bound");
        }

        var listenSocket = CreateListenSocket();

        var options = _listenerOptions.Get(ListenerName);
        var path = options.Path;
        var bound = false;
        try
        {
            listenSocket.Bind(new UnixDomainSocketEndPoint(path));
            bound = true;
            listenSocket.Listen(512);
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            listenSocket.Dispose();
            throw new AddressInUseException(e.Message, e);
        }
        catch
        {
            listenSocket.Dispose();
            if (bound && !string.IsNullOrEmpty(path) && path[0] != '\0')
            {
                File.Delete(path);
            }

            throw;
        }

        _boundPath = path;
        _listenSocket = listenSocket;
        return default;
    }

    public override async ValueTask<MessageTransport?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        var listenSocket = _listenSocket;
        if (_closingCts.IsCancellationRequested)
        {
            return null;
        }

        if (listenSocket is null)
        {
            throw new InvalidOperationException("Transport is not bound");
        }

        using var ct = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closingCts.Token);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var acceptSocket = await listenSocket.AcceptAsync(ct.Token).ConfigureAwait(false);
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

    private void DisposeCore()
    {
        _closingCts.Cancel();
        _listenSocket?.Dispose();
        _listenSocket = null;

        var path = _boundPath;
        _boundPath = null;
        if (!string.IsNullOrEmpty(path) && path[0] != '\0')
        {
            File.Delete(path);
        }
    }

    public override ValueTask UnbindAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeStarted) == 0)
        {
            DisposeCore();
        }

        return default;
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        DisposeCore();
        GC.SuppressFinalize(this);
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            _closingCts.Dispose();
        }
    }
}
