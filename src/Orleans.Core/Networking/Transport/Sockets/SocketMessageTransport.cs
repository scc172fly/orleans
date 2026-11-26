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
    private readonly string _remoteEndpointString; // 仅用于诊断
    private readonly string _localEndpointString; // 仅用于诊断
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
        // 立即回到同步呼叫器
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        try
        {
            // 斯普曼发送和接收逻辑
            var receiveTask = ProcessReads();
            var sendTask = ProcessWrites();

            // 等待两个都完成
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

                // shutdownReason 只有在产出顺利完成的情况下才应无效,所以任何人都不应
                // 是否观察非描述 ConnectionAbortedException ,但连接中器尝试
                // 关闭当前不支持的连接的一半。
                _shutdownReason ??= new ConnectionAbortedException("The Socket transport's send loop completed gracefully.");
                SocketsLog.ConnectionWriteFin(_logger, this, _shutdownReason.Message);

                // 唯有我们还没有做就叫停工
                if (!_socketShutdown)
                {
                    _socketShutdown = true;
                    try
                    {
                        _socket.Shutdown(SocketShutdown.Both);
                    }
                    catch
                    {
                        // 忽略从 Socket.Shutdown() 中的任何错误, 因为无论如何我们都要关闭连接 。
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
    /// 启动一个优雅的关闭插座 中断等待 I/O 操作。
    /// 此操作不会处理插座 - 发生在 <see cref="Shutdown"/>的套接字 。
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
                // 忽略错误 - 插件可能已经处于错误状态或未连接
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

        // 如果关闭已提前退出
        if (_connectionClosedCts.IsCancellationRequested)
        {
            return;
        }

        // 停止信号循环
        await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
        _readSignal.Signal();
        _writeSignal.Signal();

        // 如果没有处理任务,直接处置插座
        if (_processingTask is null)
        {
            Shutdown();
            await _connectionClosedCts.CancelAsync().ConfigureAwait(false);
            return;
        }

        // 关闭插座以打断任何未完成的 I/O 操作
        // 这将导致 ReceiveAsync/SendAsync 完成一个错误,
        // 允许处理循环优雅地退出。
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
            // 确保套接网关闭和处置,即使 CloseAsync 没有被调用或超时
            Shutdown();

            // 信号,我们即将关闭 如果还没有完成
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

        // 请注意套接字 API 一般只能接受最大缓冲次数 。
        // 例如,在Linux, <limits.h> 中通过IOV_MAX界定了最大值,通常为16。
        // 见 https://www.man7.org/linux/man-pages/man0/limits.h.0p.html
        // 在这里, 我们选择 8 作为最大缓冲数。 CoreCLR 将使用 *nix 上的堆叠式缓冲量,
        // 见: https://github.com/dotnet/runtime/blob/0cf461b302f58c7add3f6dc405873fb2212b513f/src/libraries/System.Net.Sockets/src/System/Net/Sockets/SocketPal.Unix.cs#L24
        List<ArraySegment<byte>> networkBuffers = new(capacity: 8);

        try
        {
            // 循环到终止
            while (!_connectionClosingCts.IsCancellationRequested)
            {
                // 处理每个请求。
                while (TryDequeue(out request))
                {
                    // 处理完成请求。
                    while (true)
                    {
                        if (request.OnRead(reader))
                        {
                            // 此项请求已完整, 转入下一个请求。
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
                            // FIN 财务处
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
/* 无操作 */
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

        // 如果我们开始关闭, 任何错误都视为优雅的终止
        if (_socketDisposed || _socketShutdown)
        {
            error = null;
            return true;
        }

        if (error is ObjectDisposedException)
        {
            // 如果插座还没处理好的话,这出乎意料
            SocketsLog.ConnectionError(_logger, this, error);
        }
        else if (IsConnectionResetError(_socketReceiver.SocketError))
        {
            // 如果已经设置了 shutdownReason, 则此选项会被忽略 。
            error = null;

            // 仍然有很小的可能性 DoReceive() 和 DoSend() 可以同时记录相同的连接重设。
            // 两个日志都有相同的 ConnectionId。我认为不值得为避免这种情况而锁定。
            SocketsLog.ConnectionReset(_logger, this);
        }
        else if (IsConnectionAbortError(_socketReceiver.SocketError))
        {
            // 此例外应总是被忽略, 因为应该设置 shutdownReason 。
            error = null;
        }
        else if (error is { })
        {
            // 这出乎意料
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
            // 循环到终止
            while (!_connectionClosingCts.IsCancellationRequested)
            {
                while (buffers.Count < bufferLimit)
                {
                    // 尝试从当前计算器中消耗缓冲 。
                    if (enumerator.MoveNext())
                    {
                        Debug.Assert(enumerator.Current.Count > 0);
                        buffers.Add(enumerator.Current);
                    }
                    else
                    {
DequeueRequest:
// 试着拿到下一个请求,然后用它来消耗。
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

                            // 开始列出下个请求 。
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

                            // 等待前检查待定信件 。
                            RefreshRequestQueue(requests, requestBuffer);

                            // 等待更多的请求。
                            if (requests.Count == 0)
                            {
                                await _writeSignal.WaitAsync().ConfigureAwait(false);
                                goto RefreshRequestQueue;
                            }

                            goto DequeueRequest;
                        }
                        else
                        {
                            // 发送当前缓冲带。
                            enumerator = default;
                            break;
                        }
                    }
                }

                // 如果没有缓冲发送,则继续检查是否有更多的请求或出境
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

                // 信号要求已满足
                for (var i = 0; i < processingRequests.Count - 1; i++)
                {
                    var (request, slice) = processingRequests[i];
                    request.SetResult();
                    slice.Dispose();
                }

                var last = processingRequests[^1];
                processingRequests.Clear();

                // 除非查点完成,否则避免处置最后一项。
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

            // 排水请求
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
            // 如果已经设置了 shutdownReason, 则此选项会被忽略 。
            var ex = _socketSender.Error!;
            error = new ConnectionResetException(ex.Message, ex);

            // 仍然有很小的可能性 DoReceive() 和 DoSend() 可以同时记录相同的连接重设。
            // 两个日志都有相同的 ConnectionId。我认为不值得为避免这种情况而锁定。
            if (!_socketDisposed)
            {
                SocketsLog.ConnectionReset(_logger, this);
            }
        }
        else if (IsConnectionAbortError(_socketSender.SocketError))
        {
            // 此例外应总是被忽略, 因为应该设置 shutdownReason 。
            error = _socketSender.Error!;

            if (!_socketDisposed)
            {
                // 如果插座还没处理好的话,这出乎意料
                SocketsLog.ConnectionError(_logger, this, error);
            }
        }
        else
        {
            // 这出乎意料
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
        // 连接重置可在 Windows 上以 SocketError.ConnectionAborted 的形式报告。
        // ProtocolType 能够被删除一次 https://github.com/dotnet/corefx/issues/31927 固定。
        return errorCode == SocketError.ConnectionReset ||
               errorCode == SocketError.Shutdown ||
               errorCode == SocketError.ConnectionAborted && IsWindows ||
               errorCode == SocketError.ProtocolType && IsMacOS;
    }

    private static bool IsConnectionAbortError(SocketError errorCode)
    {
        // 请求在 ReceiveAsync 能够造成 "InvalidArgument" 错误 *尼克斯,你没有
        return errorCode == SocketError.OperationAborted ||
               errorCode == SocketError.Interrupted ||
               errorCode == SocketError.InvalidArgument && !IsWindows;
    }

    private static EndPoint? NormalizeEndpoint(EndPoint? endpoint)
    {
        if (endpoint is not IPEndPoint ep) return endpoint;

        // 将终点正常化
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
