using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Diagnostics;

namespace Orleans.Runtime.MembershipService.SiloMetadata;

internal partial class SiloMetadataCache(
    ISiloMetadataClient siloMetadataClient,
    IMembershipManager membershipManager,
    IOptions<ClusterMembershipOptions> clusterMembershipOptions,
    ILocalSiloDetails localSiloDetails,
    ILogger<SiloMetadataCache> logger)
    : ISiloMetadataCache, ILifecycleParticipant<ISiloLifecycle>, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<SiloAddress, SiloMetadata> _metadata = new();
    private readonly Dictionary<SiloAddress, DateTime> _negativeCache = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _runTask;
    private Task? _disposeTask;
    private TimeSpan negativeCachePeriod;
    private long _updateSequence;

    void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
    {
        Task OnStart(CancellationToken _)
        {
            // 这使该组有时间投票表决,让成员更新以宣传
            negativeCachePeriod = clusterMembershipOptions.Value.GetFailureDetectionTimeout()
                                  + (2 * clusterMembershipOptions.Value.TableRefreshTimeout);
            lock (_cts)
            {
                ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
                var cancellationToken = _cts.Token;
                _runTask = Task.Run(() => this.ProcessMembershipUpdates(cancellationToken), CancellationToken.None);
            }
            return Task.CompletedTask;
        }

        async Task OnStop(CancellationToken ct)
        {
            await DisposeAsync().AsTask().WaitAsync(ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        lifecycle.Subscribe(
            nameof(ClusterMembershipService),
            ServiceLifecycleStage.RuntimeServices,
            OnStart,
            OnStop);
    }

    private async Task ProcessMembershipUpdates(CancellationToken ct)
    {
        try
        {
            LogDebugStartProcessingMembershipUpdates(logger);
            await foreach (var update in membershipManager.MembershipUpdates.WithCancellation(ct))
            {
                // 添加未在缓存中的成员条目
                var now = DateTime.UtcNow;
                var recentlyActiveSilos = update.Entries
                    .Where(e => e.Value.Status is SiloStatus.Active or SiloStatus.Joining)
                    .Where(e => !e.Value.HasMissedIAmAlives(clusterMembershipOptions.Value, now));
                foreach (var membershipEntry in recentlyActiveSilos)
                {
                    if (!_metadata.ContainsKey(membershipEntry.Key))
                    {
                        if (_negativeCache.TryGetValue(membershipEntry.Key, out var expiration) && expiration > now)
                        {
                            continue;
                        }
                        try
                        {
                            var metadata = await siloMetadataClient.GetSiloMetadata(membershipEntry.Key, ct);
                            _metadata.TryAdd(membershipEntry.Key, metadata);
                            _negativeCache.Remove(membershipEntry.Key, out _);
                        }
                        catch (OperationCanceledException exception) when (exception.CancellationToken == ct)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            _negativeCache.TryAdd(membershipEntry.Key, now + negativeCachePeriod);
                            LogErrorFetchingSiloMetadata(logger, exception, membershipEntry.Key);
                        }
                    }
                }

                // 移除已死成员条目
                var deadSilos = update.Entries
                    .Where(e => e.Value.Status == SiloStatus.Dead);
                foreach (var membershipEntry in deadSilos)
                {
                    _metadata.TryRemove(membershipEntry.Key, out _);
                    _negativeCache.Remove(membershipEntry.Key, out _);
                }

                // 删除表格中已不包含的成员条目
                foreach (var silo in _metadata.Keys.ToList())
                {
                    if (!update.Entries.ContainsKey(silo))
                    {
                        _metadata.TryRemove(silo, out _);
                        _negativeCache.Remove(silo, out _);
                    }
                }

                if (SiloMetadataEvents.IsCacheUpdatedEnabled)
                {
                    SiloMetadataEvents.EmitCacheUpdated(localSiloDetails.SiloAddress, ++_updateSequence, update.Version, _metadata.Keys);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 忽略并继续关闭
        }
        catch (Exception exception)
        {
            LogErrorProcessingMembershipUpdates(logger, exception);
        }
        finally
        {
            LogDebugStoppingMembershipProcessor(logger);
        }
    }

    public SiloMetadata GetSiloMetadata(SiloAddress siloAddress) => _metadata.GetValueOrDefault(siloAddress) ?? SiloMetadata.Empty;

    public void SetMetadata(SiloAddress siloAddress, SiloMetadata metadata) => _metadata.TryAdd(siloAddress, metadata);

    public void Dispose()
    {
        var task = StartDisposal(out var started);
        // 同时同步的呼喊者,包括取消回电、返回和业主排水。
        // DisposeAsync 总是返回需要完成处置的呼叫者的共同任务。
        if (started || task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
        }
    }

    public ValueTask DisposeAsync() => new(StartDisposal(out _));

    private Task StartDisposal(out bool started)
    {
        TaskCompletionSource completion;
        lock (_cts)
        {
            if (_disposeTask is { } task)
            {
                started = false;
                return task;
            }

            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // 在取消回馈可以重新进入处置之前公布完成情况。
            _disposeTask = completion.Task;
            started = true;
        }

        _ = DisposeCoreAsync(completion);
        return completion.Task;
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
#pragma warning disable CA1849 // Notify on the caller's scheduler before asynchronously draining metadata requests.
                _cts.Cancel();
#pragma warning restore CA1849
            }
            finally
            {
                // 订阅和元数据请求保留源,直至处理者退出。
                if (_runTask is { } task)
                {
                    await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }

                _cts.Dispose();
            }

            completion.SetResult();
        }
        catch (AggregateException exception)
        {
            completion.SetException(exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Starting to process membership updates.")]
    private static partial void LogDebugStartProcessingMembershipUpdates(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error fetching metadata for silo {Silo}")]
    private static partial void LogErrorFetchingSiloMetadata(ILogger logger, Exception exception, SiloAddress silo);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error processing membership updates")]
    private static partial void LogErrorProcessingMembershipUpdates(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Stopping membership update processor")]
    private static partial void LogDebugStoppingMembershipProcessor(ILogger logger);
}
