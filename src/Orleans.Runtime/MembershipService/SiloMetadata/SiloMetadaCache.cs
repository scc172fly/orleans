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
            // 这样可以留出时间，使集群投票判定节点死亡，并让成员资格更新传播该变更。
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
                // 为尚未在缓存中的成员添加条目。
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

                // 删除当前已死亡成员的条目。
                var deadSilos = update.Entries
                    .Where(e => e.Value.Status == SiloStatus.Dead);
                foreach (var membershipEntry in deadSilos)
                {
                    _metadata.TryRemove(membershipEntry.Key, out _);
                    _negativeCache.Remove(membershipEntry.Key, out _);
                }

                // 删除已不在成员表中的成员条目。
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
            // 忽略并继续关闭。
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
        // 在所有者排空操作期间，并发的同步调用方（包括取消回调）直接返回。
        // DisposeAsync 始终返回共享任务，供需要等待释放操作完成的调用方使用。
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
            // 在取消回调有机会重入释放操作之前发布用于通知完成的任务。
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
#pragma warning disable CA1849 // 在异步排空元数据请求之前，先在调用方的调度器上发出通知。
                _cts.Cancel();
#pragma warning restore CA1849
            }
            finally
            {
                // 订阅及其元数据请求会保留取消源，直至处理器退出。
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
