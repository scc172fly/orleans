using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Orleans.Placement.Repartitioning;
using Orleans.Runtime.Internal;

namespace Orleans.Runtime.Placement.Repartitioning;

internal sealed partial class ActivationRepartitioner : IMessageStatisticsSink
{
    private readonly CancellationTokenSource _shutdownCts = new();

    // 此过滤器包含 grain id, 它将锁定在当前 silo上。
    // grain 被发现负转移分数时插入了日期。
    private readonly AnchoredGrainsFilter? _anchoredFilter;
    private Task? _processPendingEdgesTask;

    public void StartProcessingEdges()
    {
        using var _ = new ExecutionContextSuppressor();
        _processPendingEdgesTask = ProcessPendingEdges(_shutdownCts.Token);

        LogTraceServiceStarted(_logger, nameof(ActivationRepartitioner));
    }

    public async Task StopProcessingEdgesAsync(CancellationToken cancellationToken)
    {
        _shutdownCts.Cancel();
        if (_processPendingEdgesTask is null)
        {
            return;
        }

        _pendingMessageEvent.Signal();
        await _processPendingEdgesTask.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

        LogTraceServiceStopped(_logger, nameof(ActivationRepartitioner));
    }

    private async Task ProcessPendingEdges(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);

        var drainBuffer = new Message[128];
        var iteration = 0;
        const int MaxIterationsPerYield = 128;
        while (!cancellationToken.IsCancellationRequested)
        {
            var count = _pendingMessages.DrainTo(drainBuffer);
            if (count > 0)
            {
                foreach (var message in drainBuffer[..count])
                {
                    if (!IsFullyAddressed(message) || // silo 地址(目标可能)在稍后一段时间后被设为无效(在信件被记录后),这可能导致 NRE
                        !_messageFilter.IsAcceptable(message, out var isSenderMigratable, out var isTargetMigratable))
                    {
                        continue;
                    }

                    EdgeVertex sourceVertex;
                    if (_anchoredFilter != null && _anchoredFilter.Contains(message.SendingGrain) && Silo.Equals(message.SendingSilo))
                    {
                        sourceVertex = new(GrainId, Silo, isMigratable: false);
                    }
                    else
                    {
                        sourceVertex = new(message.SendingGrain, message.SendingSilo!, isSenderMigratable);
                    }

                    EdgeVertex destinationVertex;
                    if (_anchoredFilter != null && _anchoredFilter.Contains(message.TargetGrain) && Silo.Equals(message.TargetSilo))
                    {
                        destinationVertex = new(GrainId, Silo, isMigratable: false);
                    }
                    else
                    {
                        destinationVertex = new(message.TargetGrain, message.TargetSilo!, isTargetMigratable);
                    }

                    if (!sourceVertex.IsMigratable && !destinationVertex.IsMigratable)
                    {
                        // 忽略两个非移位 grains之间的边缘 。
                        continue;
                    }

                    Edge edge = new(sourceVertex, destinationVertex);
                    _edgeWeights.Add(edge);
                }

                if (++iteration >= MaxIterationsPerYield)
                {
                    iteration = 0;
                    await Task.Delay(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond), CancellationToken.None);
                }
            }
            else
            {
                iteration = 0;
                await _pendingMessageEvent.WaitAsync();
            }
        }
    }

    public Action<Message>? GetMessageObserver() => RecordMessage;

    private void RecordMessage(Message message)
    {
        if (!_enableMessageSampling || message.IsSystemMessage)
        {
            return;
        }

        // 分析必须有一个方向,
        if (message.Direction is Message.Directions.None or Message.Directions.Response)
        {
            return;
        }

        // 发信人和目标需要完全可处理,才能知道向何处移动或走向何方。
        if (!IsFullyAddressed(message))
        {
            return;
        }

        if (_pendingMessages.TryAdd(message) == Utilities.BufferStatus.Success)
        {
            _pendingMessageEvent.Signal();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsFullyAddressed(Message message) =>
        message.IsSenderFullyAddressed && message.IsTargetFullyAddressed;

    async ValueTask IActivationRepartitionerSystemTarget.FlushBuffers(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (_pendingMessages.Count > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "{Service} has started."
    )]
    private static partial void LogTraceServiceStarted(ILogger logger, string service);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "{Service} has stopped."
    )]
    private static partial void LogTraceServiceStopped(ILogger logger, string service);
}
