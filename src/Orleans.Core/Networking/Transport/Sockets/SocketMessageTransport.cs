using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Orleans.Runtime.Internal;
using System.Net;
using Orleans.Runtime;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Connections.Transport;
using Orleans.Runtime.Utilities;

namespace Orleans.Connections.Transport.Sockets;

internal sealed partial class SocketMessageTransport : MessageTransportBase
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private readonly SocketSender _socketSender = new();
    private readonly SocketReceiver _socketReceiver = new();
    private readonly Socket _socket;
    private readonly Queue<ReadRequest> _readRequests = new();
    private readonly SingleWaiterAutoResetEvent _readSignal = new() { RunContinuationsAsynchronously = false };
    private readonly SingleWaiterAutoResetEvent _writeSignal = new() { RunContinuationsAsynchronously = false };
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _connectionClosingCts = new();
    private readonly CancellationTokenSource _connectionClosedCts = new();
    private readonly object _shutdownLock = new();
    private readonly object _readsLock = new();
    private readonly string _remoteEndpointString; // 仅用于诊断。
    private readonly string _localEndpointString; // 仅用于诊断。
    private readonly StripedMpscBuffer<WriteRequest> _writeRequests = new(
        stripeCount: 16,
        bufferSize: 256);
    private bool _readsCompleted;
    private int _pendingWriteCount;
    private int _writeEnqueuers;
    private int _writesCompleted;
    private Task? _processingTask;
    private volatile bool _socketDisposed;
    private volatile bool _socketShutdown;
    private volatile Exception? _shutdownReason;
    private int _disposeStarted;

    public SocketMessageTransport(Socket socket, ILogger logger)
    {
        _socket = socket;
        _logger = logger;

        var remoteEndPoint = NormalizeEndpoint(_socket.RemoteEndPoint);
        var localEndPoint = NormalizeEndpoint(_socket.LocalEndPoint);

        Features.Set<IConnectionEndPointFeature>(new ConnectionEndPointFeature
        {
            RemoteEndPoint = remoteEndPoint,
            LocalEndPoint = localEndPoint,
        });

        _remoteEndpointString = remoteEndPoint?.ToString() ?? "null";
        _localEndpointString = localEndPoint?.ToString() ?? "null";
    }

    internal static async ValueTask<SocketMessageTransport> CreateAndStartAsync(Socket socket, ILogger logger)
    {
        SocketMessageTransport? transport = null;
        try
        {
            transport = new SocketMessageTransport(socket, logger);
            transport.Start();
            return transport;
        }
        catch
        {
            if (transport is not null)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                socket.Dispose();
            }

            throw;
        }
    }

    public override CancellationToken Closed => _connectionClosedCts.Token;

    public void Start()
    {
        using var _ = new ExecutionContextSuppressor();
        _processingTask = ProcessConnectionAsync();
    }

    private async Task ProcessConnectionAsync()
    {
        // 立即返回给同步调用方。
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        try
        {
            // 启动发送和接收逻辑。
            var receiveTask = ProcessReads();
            var sendTask = ProcessWrites();

            // 等待两者完成。
            try
            {
                await receiveTask;
            }
            catch (Exception ex)
            {
                LogProcessReadsFailure(_logger, ex);
            }

            try
            {
                await sendTask;
            }
            catch (Exception ex)
            {
                LogProcessWritesFailure(_logger, ex);
            }

            _socketReceiver.Dispose();
            _socketSender.Dispose();
        }
        catch (Exception ex)
        {
            _shutdownReason ??= ex;
            LogProcessConnectionFailure(_logger, ex);
        }
        finally
        {
            Shutdown();

            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
            await _connectionClosedCts.CancelAsync().ConfigureAwait(false);
        }
    }

    private void Shutdown()
    {
        if (_socketDisposed)
        {
            return;
        }

        lock (_shutdownLock)
        {
            try
            {
                if (_socketDisposed)
                {
                    return;
                }

                _socketDisposed = true;

                // 只有输出正常完成时 shutdownReason 才应为 null，因此除了
                // 连接中间件尝试半关闭连接之外（目前不支持此操作），不应有任何代码
                // 观察到不包含具体说明的 ConnectionAbortedException。
                _shutdownReason ??= new ConnectionAbortedException("The Socket transport's send loop completed gracefully.");
                SocketsLog.ConnectionWriteFin(_logger, this, _shutdownReason.Message);

                // 仅在尚未调用 Shutdown 时调用它。
                if (!_socketShutdown)
                {
                    _socketShutdown = true;
                    try
                    {
                        _socket.Shutdown(SocketShutdown.Both);
                    }
                    catch
                    {
                        // 忽略 Socket.Shutdown() 的任何错误，因为无论如何都在拆除连接。
                    }
                }

                _socket.Dispose();
            }
            catch (Exception exception)
            {
                SocketsLog.ConnectionShutdownError(_logger, this, exception);
            }
        }
    }

    /// <summary>
    /// 发起套接字的优雅关闭，以中断待完成的 I/O 操作。
    /// 这不会释放套接字；释放操作在 <see cref="Shutdown"/> 中发生。
    /// </summary>
    private void ShutdownSocket()
    {
        if (_socketShutdown || _socketDisposed)
        {
            return;
        }

        lock (_shutdownLock)
        {
            if (_socketShutdown || _socketDisposed)
            {
                return;
            }

            _socketShutdown = true;

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // 忽略错误：套接字可能已处于错误状态或未连接。
            }
        }
    }

    public override bool EnqueueRead(ReadRequest request)
    {
        if (_connectionClosingCts.IsCancellationRequested)
        {
            return false;
        }

        lock (_readsLock)
        {
            if (_readsCompleted)
            {
                return false;
            }

            _readRequests.Enqueue(request);
        }

        _readSignal.Signal();
        return true;
    }

    public override bool EnqueueWrite(WriteRequest request)
    {
        if (_connectionClosingCts.IsCancellationRequested || Volatile.Read(ref _writesCompleted) != 0)
        {
            return false;
        }

        Interlocked.Increment(ref _writeEnqueuers);
        if (_connectionClosingCts.IsCancellationRequested || Volatile.Read(ref _writesCompleted) != 0)
        {
            Interlocked.Decrement(ref _writeEnqueuers);
            return false;
        }

        Interlocked.Increment(ref _pendingWriteCount);
        var spinner = new SpinWait();
        while (_writeRequests.TryAdd(request) is not BufferStatus.Success)
        {
            if (_connectionClosingCts.IsCancellationRequested || Volatile.Read(ref _writesCompleted) != 0)
            {
                Interlocked.Decrement(ref _pendingWriteCount);
                Interlocked.Decrement(ref _writeEnqueuers);
                return false;
            }

            _writeSignal.Signal();
            spinner.SpinOnce();
        }

        Interlocked.Decrement(ref _writeEnqueuers);
        _writeSignal.Signal();
        return true;
    }

    public override async ValueTask CloseAsync(Exception? closeReason, CancellationToken cancellationToken = default)
    {
        _shutdownReason ??= closeReason;

        // 如果已关闭，则提前退出。
        if (_connectionClosedCts.IsCancellationRequested)
        {
            return;
        }

        // 通知各循环停止。
        await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
        _readSignal.Signal();
        _writeSignal.Signal();

        // 如果没有处理任务，则直接释放套接字。
        if (_processingTask is null)
        {
            Shutdown();
            await _connectionClosedCts.CancelAsync().ConfigureAwait(false);
            return;
        }

        // 关闭套接字，以中断任何待完成的 I/O 操作。
        // 这将使 ReceiveAsync/SendAsync 以错误结束，
        // 从而让处理循环能够正常退出。
        ShutdownSocket();

        try
        {
            await _processingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Shutdown();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            // 即使未调用 CloseAsync 或调用超时，也确保套接字已关闭并释放。
            Shutdown();

            // 如果尚未发出关闭信号，则通知正在关闭。
            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
            _readSignal.Signal();
            _writeSignal.Signal();

            if (_processingTask is { } processingTask)
            {
                await processingTask.ConfigureAwait(false);
            }
            else
            {
                _socketReceiver.Dispose();
                _socketSender.Dispose();
                await _connectionClosedCts.CancelAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _connectionClosingCts.Dispose();
            _connectionClosedCts.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private async Task ProcessReads()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var isGracefulTermination = false;
        Exception? error = null;
        ReadRequest? request = null;
        using ArcBufferWriter bufferWriter = new();
        var reader = new ArcBufferReader(bufferWriter);

        // 注意：套接字 API 通常只能接受有限数量的缓冲区。
        // 例如，在 Linux 上，最大数量由 <limits.h> 中的 IOV_MAX 定义，通常为 16。
        // 参阅 https://www.man7.org/linux/man-pages/man0/limits.h.0p.html
        // 此处选择 8 作为 CoreCLR 在类 Unix 系统上使用 stackalloc 分配的缓冲区数量上限，
        // 参阅 https://github.com/dotnet/runtime/blob/0cf461b302f58c7add3f6dc405873fb2212b513f/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketPal.Unix.cs#L24
        List<ArraySegment<byte>> networkBuffers = new(capacity: 8);

        try
        {
            // 循环直到终止。
            while (!_connectionClosingCts.IsCancellationRequested)
            {
                // 处理每个请求。
                while (TryDequeue(out request))
                {
                    // 处理请求直至完成。
                    while (true)
                    {
                        if (request.OnRead(reader))
                        {
                            // 此请求已完成，转到下一个请求。
                            break;
                        }

                        bufferWriter.ReplenishBuffers(networkBuffers);
                        Debug.Assert(networkBuffers.Count == networkBuffers.Capacity);
                        await _socketReceiver.ReceiveAsync(_socket, networkBuffers).ConfigureAwait(false);

                        if (_socketReceiver.HasError)
                        {
                            error = _socketReceiver.Error;
                            isGracefulTermination = HandleReadError(ref error);
                            goto exit;
                        }

                        var transferred = _socketReceiver.BytesTransferred;

                        AdvanceBufferList(networkBuffers, transferred);
                        bufferWriter.AdvanceWriter(transferred);

                        if (transferred == 0)
                        {
                            // FIN
                            SocketsLog.ConnectionReadFin(_logger, this);
                            isGracefulTermination = true;
                            goto exit;
                        }
                    }

                    request = null;
                }

                await _readSignal.WaitAsync().ConfigureAwait(false);
            }

            isGracefulTermination = true;
exit:
/* no op */
            ;
        }
        catch (Exception exception)
        {
            if (_connectionClosingCts.IsCancellationRequested)
            {
                isGracefulTermination = true;
            }
            else
            {
                error = exception;
                isGracefulTermination = HandleReadError(ref error);
            }
        }
        finally
        {
            _shutdownReason ??= error;
            Shutdown();
            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);

            if (isGracefulTermination)
            {
                request?.OnCanceled();
            }
            else
            {
                Debug.Assert(error is not null);
                request?.OnError(error);
            }

            _writeSignal.Signal();

            lock (_readsLock)
            {
                _readsCompleted = true;
                while (_readRequests.TryDequeue(out request))
                {
                    if (isGracefulTermination)
                    {
                        request.OnCanceled();
                    }
                    else
                    {
                        Debug.Assert(error is not null);
                        request.OnError(error);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TryDequeue([NotNullWhen(true)] out ReadRequest? request)
        {
            lock (_readsLock)
            {
                return _readRequests.TryDequeue(out request);
            }
        }

    }

    private bool HandleReadError(ref Exception? error)
    {
        if (_socketReceiver.HasError)
        {
            error = _socketReceiver.Error;
        }

        // 如果由我们发起关闭，则将任何错误视为正常终止。
        if (_socketDisposed || _socketShutdown)
        {
            error = null;
            return true;
        }

        if (error is ObjectDisposedException)
        {
            // 如果套接字尚未释放，则这是意外情况。
            SocketsLog.ConnectionError(_logger, this, error);
        }
        else if (IsConnectionResetError(_socketReceiver.SocketError))
        {
            // 如果已设置 _shutdownReason，则可忽略此情况。
            error = null;

            // DoReceive() 和 DoSend() 仍有很小的概率会记录同一次连接重置。
            // 两条日志的 ConnectionId 相同。仅为避免此情况而加锁并不值得。
            SocketsLog.ConnectionReset(_logger, this);
        }
        else if (IsConnectionAbortError(_socketReceiver.SocketError))
        {
            // 此异常应始终忽略，因为此时应已设置 _shutdownReason。
            error = null;
        }
        else if (error is { })
        {
            // 这是意外情况。
            SocketsLog.ConnectionError(_logger, this, error);
        }
        else
        {
            error = null;
        }

        return error is null;
    }

    private async Task ProcessWrites()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        const int SmallRequestBufferLimit = 32;
        const int LargeRequestBufferLimit = 64;
        const int CoalescingBufferSize = 64 * 1024;
        Exception? error = null;
        Queue<WriteRequest> requests = new();
        WriteRequest[] requestBuffer = new WriteRequest[256];
        List<ArraySegment<byte>> buffers = new(capacity: LargeRequestBufferLimit);
        List<(WriteRequest, ArcBuffer)> processingRequests = new(capacity: LargeRequestBufferLimit);
        var coalescingBuffer = ArrayPool<byte>.Shared.Rent(CoalescingBufferSize);
        ArcBuffer.ArraySegmentEnumerator enumerator = default;
        var bufferLimit = SmallRequestBufferLimit;

        try
        {
            // 循环直到终止。
            while (!_connectionClosingCts.IsCancellationRequested)
            {
                while (buffers.Count < bufferLimit)
                {
                    // 尝试从当前枚举器消耗一个缓冲区。
                    if (enumerator.MoveNext())
                    {
                        Debug.Assert(enumerator.Current.Count > 0);
                        buffers.Add(enumerator.Current);
                    }
                    else
                    {
DequeueRequest:
// 尝试获取下一个请求并消耗其数据。
                        if (requests.TryDequeue(out var request))
                        {
                            if (request.Buffers.Length == 0)
                            {
                                request.SetResult();
                                goto DequeueRequest;
                            }

                            if (request.HasLargeMessages)
                            {
                                bufferLimit = LargeRequestBufferLimit;
                            }

                            // 开始枚举下一个请求。
                            var slice = request.Buffers.ConsumeSlice(request.Buffers.Length);
                            processingRequests.Add((request, slice));
                            enumerator = slice.ArraySegments;
                        }
                        else if (buffers.Count == 0)
                        {
RefreshRequestQueue:
                            if (_connectionClosingCts.IsCancellationRequested)
                            {
                                break;
                            }

                            // 在等待之前检查是否有待处理的消息。
                            RefreshRequestQueue(requests, requestBuffer);

                            // 等待更多请求。
                            if (requests.Count == 0)
                            {
                                await _writeSignal.WaitAsync().ConfigureAwait(false);
                                goto RefreshRequestQueue;
                            }

                            goto DequeueRequest;
                        }
                        else
                        {
                            // 发送当前缓冲区中的数据。
                            enumerator = default;
                            break;
                        }
                    }
                }

                // 如果没有要发送的缓冲区，则继续检查是否有更多请求，或者退出。
                if (buffers.Count == 0)
                {
                    continue;
                }

                var totalBytes = 0;
                foreach (var buffer in buffers)
                {
                    totalBytes += buffer.Count;
                }

                if (buffers.Count == 1
                    && processingRequests.Count == 1
                    && requests.Count == 0
                    && Volatile.Read(ref _pendingWriteCount) == 0)
                {
                    var buffer = buffers[0];
                    var remaining = new ReadOnlyMemory<byte>(buffer.Array!, buffer.Offset, buffer.Count);
                    while (!remaining.IsEmpty)
                    {
                        await _socketSender.SendAsync(_socket, remaining).ConfigureAwait(false);
                        if (_socketSender.HasError)
                        {
                            error = GetSendAsyncError();
                            break;
                        }

                        var transferred = _socketSender.BytesTransferred;
                        if (transferred == 0)
                        {
                            error = new ConnectionClosedException("The socket closed before all queued data was sent.");
                            break;
                        }

                        remaining = remaining[transferred..];
                    }

                    if (error is null)
                    {
                        buffers.Clear();
                    }
                }
                else if (bufferLimit == SmallRequestBufferLimit
                    && buffers.Count > 1
                    && totalBytes <= coalescingBuffer.Length)
                {
                    var offset = 0;
                    foreach (var buffer in buffers)
                    {
                        buffer.AsSpan().CopyTo(coalescingBuffer.AsSpan(offset));
                        offset += buffer.Count;
                    }

                    var remaining = new ReadOnlyMemory<byte>(coalescingBuffer, 0, totalBytes);
                    while (!remaining.IsEmpty)
                    {
                        await _socketSender.SendAsync(_socket, remaining).ConfigureAwait(false);
                        if (_socketSender.HasError)
                        {
                            error = GetSendAsyncError();
                            break;
                        }

                        var transferred = _socketSender.BytesTransferred;
                        if (transferred == 0)
                        {
                            error = new ConnectionClosedException("The socket closed before all queued data was sent.");
                            break;
                        }

                        remaining = remaining[transferred..];
                    }

                    if (error is null)
                    {
                        buffers.Clear();
                    }
                }
                else
                {
                    while (buffers.Count > 0)
                    {
                        await _socketSender.SendAsync(_socket, buffers).ConfigureAwait(false);
                        if (_socketSender.HasError)
                        {
                            error = GetSendAsyncError();
                            break;
                        }

                        var transferred = _socketSender.BytesTransferred;
                        if (transferred == 0)
                        {
                            error = new ConnectionClosedException("The socket closed before all queued data was sent.");
                            break;
                        }

                        AdvanceBufferList(buffers, transferred);
                    }
                }

                if (error is not null)
                {
                    break;
                }

                bufferLimit = SmallRequestBufferLimit;

                // 发出请求已完成的信号。
                for (var i = 0; i < processingRequests.Count - 1; i++)
                {
                    var (request, slice) = processingRequests[i];
                    request.SetResult();
                    slice.Dispose();
                }

                var last = processingRequests[^1];
                processingRequests.Clear();

                // 除非枚举已完成，否则避免释放最后一个条目。
                if (enumerator.IsCompleted)
                {
                    var (request, slice) = last;
                    request.SetResult();
                    slice.Dispose();
                }
                else
                {
                    processingRequests.Add(last);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
            if (!_socketDisposed)
            {
                SocketsLog.ConnectionError(_logger, this, error);
            }
        }
        finally
        {
            _shutdownReason ??= error;
            Shutdown();
            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
            _readSignal.Signal();

            var requestError = _shutdownReason ?? new ConnectionClosedException();
            foreach (var (request, slice) in processingRequests)
            {
                request.SetException(requestError);
                slice.Dispose();
            }

            Volatile.Write(ref _writesCompleted, 1);
            var spinner = new SpinWait();
            while (Volatile.Read(ref _writeEnqueuers) != 0)
            {
                spinner.SpinOnce();
            }

            // 排空请求。
            int drained;
            do
            {
                drained = RefreshRequestQueue(requests, requestBuffer);
                while (requests.TryDequeue(out var request))
                {
                    request.SetException(requestError);
                }
            }
            while (drained > 0);

            ArrayPool<byte>.Shared.Return(coalescingBuffer);
        }

        int RefreshRequestQueue(Queue<WriteRequest> queue, WriteRequest[] buffer)
        {
            var count = _writeRequests.DrainTo(buffer);
            Interlocked.Add(ref _pendingWriteCount, -count);
            for (var i = 0; i < count; i++)
            {
                queue.Enqueue(buffer[i]);
                buffer[i] = null!;
            }

            return count;
        }

    }

    private static void AdvanceBufferList(List<ArraySegment<byte>> buffers, int bytesTransferred)
    {
        while (bytesTransferred > 0)
        {
            Debug.Assert(buffers.Count > 0);
            var current = buffers[0];
            if (bytesTransferred >= current.Count)
            {
                bytesTransferred -= current.Count;
                buffers.RemoveAt(0);
            }
            else
            {
                buffers[0] = new(current.Array!, current.Offset + bytesTransferred, current.Count - bytesTransferred);
                bytesTransferred = 0;
            }
        }
    }

    private Exception GetSendAsyncError()
    {
        Exception error;
        if (IsConnectionResetError(_socketSender.SocketError))
        {
            // 如果已设置 _shutdownReason，则可忽略此情况。
            var ex = _socketSender.Error!;
            error = new ConnectionResetException(ex.Message, ex);

            // DoReceive() 和 DoSend() 仍有很小的概率会记录同一次连接重置。
            // 两条日志的 ConnectionId 相同。仅为避免此情况而加锁并不值得。
            if (!_socketDisposed)
            {
                SocketsLog.ConnectionReset(_logger, this);
            }
        }
        else if (IsConnectionAbortError(_socketSender.SocketError))
        {
            // 此异常应始终忽略，因为此时应已设置 _shutdownReason。
            error = _socketSender.Error!;

            if (!_socketDisposed)
            {
                // 如果套接字尚未释放，则这是意外情况。
                SocketsLog.ConnectionError(_logger, this, error);
            }
        }
        else
        {
            // 这是意外情况。
            error = _socketSender.Error!;
            if (!_socketDisposed)
            {
                SocketsLog.ConnectionError(_logger, this, error);
            }
        }

        return error;
    }

    private static bool IsConnectionResetError(SocketError errorCode)
    {
        // 在 Windows 上，连接重置可能以 SocketError.ConnectionAborted 的形式报告。
        // https://github.com/dotnet/corefx/issues/31927 修复后即可移除 ProtocolType。
        return errorCode == SocketError.ConnectionReset ||
               errorCode == SocketError.Shutdown ||
               errorCode == SocketError.ConnectionAborted && IsWindows ||
               errorCode == SocketError.ProtocolType && IsMacOS;
    }

    private static bool IsConnectionAbortError(SocketError errorCode)
    {
        // 在类 Unix 系统上，在 ReceiveAsync 之后调用 Dispose 可能导致“InvalidArgument”错误。
        return errorCode == SocketError.OperationAborted ||
               errorCode == SocketError.Interrupted ||
               errorCode == SocketError.InvalidArgument && !IsWindows;
    }

    private static EndPoint? NormalizeEndpoint(EndPoint? endpoint)
    {
        if (endpoint is not IPEndPoint ep) return endpoint;

        // 规范化终结点。
        if (ep.Address.IsIPv4MappedToIPv6)
        {
            return new IPEndPoint(ep.Address.MapToIPv4(), ep.Port);
        }

        return ep;
    }

    public override string ToString() => $"Socket(Remote: {_remoteEndpointString}, Local: {_localEndpointString})";

    [LoggerMessage(0, LogLevel.Error, $"Unexpected exception in {nameof(SocketMessageTransport)}.{nameof(ProcessReads)}.")]
    private static partial void LogProcessReadsFailure(ILogger logger, Exception exception);

    [LoggerMessage(0, LogLevel.Error, $"Unexpected exception in {nameof(SocketMessageTransport)}.{nameof(ProcessWrites)}.")]
    private static partial void LogProcessWritesFailure(ILogger logger, Exception exception);

    [LoggerMessage(0, LogLevel.Error, $"Unexpected exception in {nameof(SocketMessageTransport)}.{nameof(ProcessConnectionAsync)}.")]
    private static partial void LogProcessConnectionFailure(ILogger logger, Exception exception);
}
