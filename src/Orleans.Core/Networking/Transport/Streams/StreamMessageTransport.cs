using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Runtime.Internal;
using Orleans.Serialization.Buffers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport.Streams;

internal abstract partial class StreamMessageTransport : MessageTransportBase
{
    private readonly ILogger _logger;
    private readonly SingleWaiterAutoResetEvent _writerSignal = new();
    private readonly SingleWaiterAutoResetEvent _readerSignal = new();
    private readonly Queue<WriteRequest> _pendingWrites = new();
    private readonly Queue<ReadRequest> _pendingReads = new();
    private readonly CancellationTokenSource _connectionClosingCts = new();
    private readonly CancellationTokenSource _connectionClosedCts = new();
    private readonly object _writesLock = new();
    private readonly object _readsLock = new();
    private readonly object _disposeLock = new();
    private Task? _runTask;
    private Exception? _shutdownReason;
    private bool _readsCompleted;
    private bool _writesCompleted;
    private volatile bool _streamDisposed;
    private int _disposeStarted;

    protected StreamMessageTransport(ILogger logger)
    {
        _logger = logger;
    }

    protected abstract Stream Stream { get; }

    public virtual void Start(CancellationToken cancellationToken = default)
    {
        using var _ = new ExecutionContextSuppressor();
        // 执行已取消的启动尝试，使已接受的请求在释放之前完成。
        _runTask = Task.Run(() => RunAsync(cancellationToken), CancellationToken.None);
    }

    public override CancellationToken Closed => _connectionClosedCts.Token;

    public override async ValueTask CloseAsync(Exception? closeException, CancellationToken cancellationToken = default)
    {
        _shutdownReason ??= closeException;

        // 如果已关闭，则提前退出。
        if (_connectionClosedCts.IsCancellationRequested)
        {
            return;
        }

        // 通知各循环停止。
        await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
        _readerSignal.Signal();
        _writerSignal.Signal();

        // 如果没有运行任务，则直接释放流。
        if (_runTask is null)
        {
            DisposeStream();
            await _connectionClosedCts.CancelAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DisposeStream();
        }
    }

    /// <summary>
    /// 释放底层流，以强制关闭任何待完成的 I/O 操作。
    /// </summary>
    private void DisposeStream()
    {
        if (_streamDisposed)
        {
            return;
        }

        lock (_disposeLock)
        {
            if (_streamDisposed)
            {
                return;
            }

            _streamDisposed = true;

            try
            {
                Stream.Dispose();
            }
            catch
            {
                // 忽略释放期间的错误。
            }
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
            // 即使未调用 CloseAsync 或调用超时，也确保流已释放。
            DisposeStream();

            // 如果尚未发出关闭信号，则通知正在关闭。
            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
            _readerSignal.Signal();
            _writerSignal.Signal();

            if (_runTask is { } runTask)
            {
                await runTask.ConfigureAwait(false);
            }
            else
            {
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

            _pendingReads.Enqueue(request);
        }

        _readerSignal.Signal();
        return true;
    }

    public override bool EnqueueWrite(WriteRequest request)
    {
        if (_connectionClosingCts.IsCancellationRequested)
        {
            return false;
        }

        lock (_writesLock)
        {
            if (_writesCompleted)
            {
                return false;
            }

            _pendingWrites.Enqueue(request);
        }

        _writerSignal.Signal();
        return true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunAsyncCore(cancellationToken);
        }
        catch (Exception exception)
        {
            _shutdownReason ??= exception;
            FailPendingRequests(exception);
        }
        finally
        {
            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
            _readerSignal.Signal();
            _writerSignal.Signal();
            await _connectionClosedCts.CancelAsync().ConfigureAwait(false);
        }

        void FailPendingRequests(Exception exception)
        {
            lock (_readsLock)
            {
                _readsCompleted = true;
                while (_pendingReads.TryDequeue(out var request))
                {
                    request.OnError(exception);
                }
            }

            lock (_writesLock)
            {
                _writesCompleted = true;
                while (_pendingWrites.TryDequeue(out var request))
                {
                    request.SetException(exception);
                }
            }
        }
    }

    protected virtual async Task RunAsyncCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var readsTask = ProcessReads();
            var writesTask = ProcessWrites();
            await readsTask;
            await writesTask;
        }
        catch (Exception exception)
        {
            _shutdownReason ??= exception;
        }
        finally
        {
            await _connectionClosedCts.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessReads()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        Exception? error = default;
        ReadRequest? operation = default;
        bool isGracefulTermination = false;
        using ArcBufferWriter bufferWriter = new();
        var reader = new ArcBufferReader(bufferWriter);
        try
        {
            while (!_connectionClosingCts.IsCancellationRequested)
            {
                while (TryDequeue(out operation))
                {
                    while (true)
                    {
                        if (operation.OnRead(reader))
                        {
                            break;
                        }

                        var bytesRead = await Stream.ReadAsync(bufferWriter.GetMemory(), _connectionClosingCts.Token);
                        if (bytesRead == 0)
                        {
                            goto gracefulTermination;
                        }

                        bufferWriter.AdvanceWriter(bytesRead);
                    }

                    operation = null;
                }

                await _readerSignal.WaitAsync();
            }

gracefulTermination:
            isGracefulTermination = true;
        }
        catch (Exception exception)
        {
            // 如果由我们发起关闭（流已释放或已请求关闭），则视为正常终止。
            if (_connectionClosingCts.IsCancellationRequested || _streamDisposed)
            {
                isGracefulTermination = true;
            }
            else
            {
                error = exception;
                isGracefulTermination = false;
            }
        }
        finally
        {
            _shutdownReason ??= error;
            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);

            lock (_readsLock)
            {
                _readsCompleted = true;
            }

            if (isGracefulTermination)
            {
                operation?.OnCanceled();
            }
            else
            {
                Debug.Assert(error is not null);
                operation?.OnError(error);
            }

            while (TryDequeue(out operation))
            {
                if (isGracefulTermination)
                {
                    operation.OnCanceled();
                }
                else
                {
                    Debug.Assert(error is not null);
                    operation.OnError(error);
                }
            }

            _writerSignal.Signal();

            // 仅记录意外错误（不包括主动释放流时发生的错误）。
            if (error is not null && !_streamDisposed)
            {
                LogProcessReadsFailure(_logger, error);
            }
        }

        bool TryDequeue([NotNullWhen(true)] out ReadRequest? operation)
        {
            lock (_readsLock)
            {
                return _pendingReads.TryDequeue(out operation);
            }
        }
    }

    private async Task ProcessWrites()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        Exception? error = default;
        WriteRequest? operation = default;
        try
        {
            while (!_connectionClosingCts.IsCancellationRequested)
            {
                while (TryDequeue(out operation))
                {
                    using var slice = operation.Buffers.ConsumeSlice(operation.Buffers.Length);
                    foreach (var buffer in slice.MemorySegments)
                    {
                        await Stream.WriteAsync(buffer, _connectionClosingCts.Token);
                    }

                    operation.SetResult();
                    operation = null;
                }

                await _writerSignal.WaitAsync();
            }
        }
        catch (Exception exception)
        {
            // 如果由我们发起关闭，则不视为错误。
            if (!_connectionClosingCts.IsCancellationRequested && !_streamDisposed)
            {
                error = exception;
            }
        }
        finally
        {
            _shutdownReason ??= error;
            await _connectionClosingCts.CancelAsync().ConfigureAwait(false);
            _readerSignal.Signal();
            var requestError = _shutdownReason ?? new ConnectionClosedException();
            operation?.SetException(requestError);

            // 仅记录意外错误（不包括主动释放流时发生的错误）。
            if (error is not null && !_streamDisposed)
            {
                LogProcessWritesFailure(_logger, error);
            }

            lock (_writesLock)
            {
                _writesCompleted = true;
                while (_pendingWrites.TryDequeue(out operation))
                {
                    operation.SetException(requestError);
                }
            }

        }

        bool TryDequeue([NotNullWhen(true)] out WriteRequest? operation)
        {
            lock (_writesLock)
            {
                return _pendingWrites.TryDequeue(out operation);
            }
        }
    }

    [LoggerMessage(0, LogLevel.Error, $"Unexpected exception in {nameof(StreamMessageTransport)}.{nameof(ProcessReads)}.")]
    private static partial void LogProcessReadsFailure(ILogger logger, Exception exception);

    [LoggerMessage(0, LogLevel.Error, $"Unexpected exception in {nameof(StreamMessageTransport)}.{nameof(ProcessWrites)}.")]
    private static partial void LogProcessWritesFailure(ILogger logger, Exception exception);
}
