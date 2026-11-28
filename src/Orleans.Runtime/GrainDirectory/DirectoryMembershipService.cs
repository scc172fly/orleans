using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Internal;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Utilities;

namespace Orleans.Runtime.GrainDirectory;

internal sealed partial class DirectoryMembershipService : IAsyncDisposable
{
    private readonly IInternalGrainFactory _grainFactory;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _runTask;
    private readonly AsyncEnumerable<DirectoryMembershipSnapshot> _viewUpdates;
    private readonly int _partitionsPerSilo;
    private readonly Func<SiloAddress, int, ImmutableArray<uint>> _getRingBoundaries;
    private DirectoryMembershipSnapshot _currentView = DirectoryMembershipSnapshot.Default;
    private Task? _disposeTask;

    public DirectoryMembershipSnapshot CurrentView
    {
        get
        {
            if (_runTask.IsFaulted)
            {
                _runTask.GetAwaiter().GetResult();
            }

            return _currentView;
        }
    }

    public int PartitionsPerSilo => _partitionsPerSilo;

    public IAsyncEnumerable<DirectoryMembershipSnapshot> ViewUpdates => GetViewUpdates();

    public IClusterMembershipService ClusterMembershipService { get; }

    public async ValueTask<DirectoryMembershipSnapshot> RefreshViewAsync(MembershipVersion version, CancellationToken cancellationToken)
    {
        if (CurrentView.Version < version || version == default)
        {
            await ClusterMembershipService.Refresh(version, cancellationToken);
        }

        if (CurrentView.Version < version)
        {
            await foreach (var view in ViewUpdates.WithCancellation(cancellationToken))
            {
                if (view.Version >= version)
                {
                    break;
                }
            }
        }

        return CurrentView;
    }

    private async IAsyncEnumerable<DirectoryMembershipSnapshot> GetViewUpdates([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var view in _viewUpdates.WithCancellation(cancellationToken))
        {
            yield return view;
        }

        // 将处理器的失败传播给当前及未来的订阅者。
        await _runTask;
    }

    public DirectoryMembershipService(
        IClusterMembershipService clusterMembershipService,
        IInternalGrainFactory grainFactory,
        ILogger<DirectoryMembershipService> logger,
        int partitionsPerSilo,
        Func<SiloAddress, int, ImmutableArray<uint>> getRingBoundaries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionsPerSilo, 1);
        ArgumentNullException.ThrowIfNull(getRingBoundaries);
        _partitionsPerSilo = partitionsPerSilo;
        _getRingBoundaries = getRingBoundaries;
        _viewUpdates = new(
            DirectoryMembershipSnapshot.Default,
            (previous, proposed) => proposed.Version >= previous.Version,
            update => _currentView = update);
        ClusterMembershipService = clusterMembershipService;
        _grainFactory = grainFactory;
        _logger = logger;
        using var _ = new ExecutionContextSuppressor();
        _runTask = Task.Run(ProcessMembershipUpdates);
    }

    private async Task ProcessMembershipUpdates()
    {
        try
        {
            while (!_shutdownCts.IsCancellationRequested)
            {
                try
                {
                    await foreach (var update in ClusterMembershipService.MembershipUpdates.WithCancellation(_shutdownCts.Token))
                    {
                        DirectoryMembershipSnapshot view;
                        try
                        {
                            view = new DirectoryMembershipSnapshot(update, _grainFactory, _partitionsPerSilo, _getRingBoundaries);
                        }
                        catch (Exception exception)
                        {
                            throw new OrleansConfigurationException(
                                $"Failed to construct grain directory membership version '{update.Version}'.",
                                exception);
                        }

                        _viewUpdates.Publish(view);
                    }
                }
                catch (Exception exception) when (exception is not OrleansConfigurationException)
                {
                    if (!_shutdownCts.IsCancellationRequested)
                    {
                        LogErrorProcessingMembershipUpdates(exception);
                    }
                }
            }
        }
        catch (OrleansConfigurationException exception)
        {
            if (!_shutdownCts.IsCancellationRequested)
            {
                LogErrorProcessingMembershipUpdates(exception);
            }

            throw;
        }
        finally
        {
            _viewUpdates.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_shutdownCts)
        {
            if (_disposeTask is { } task)
            {
                return new(task);
            }

            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // 在取消回调可能重入释放操作之前，先发布用于通知完成的任务。
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(completion);
        return new(completion.Task);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
#pragma warning disable CA1849 // 在异步排空订阅之前，先在调用方的调度器上发出通知。
                _shutdownCts.Cancel();
#pragma warning restore CA1849
            }
            finally
            {
                // 成员资格处理器在自身完成之前始终拥有其订阅。
                await _runTask.SuppressThrowing();
                _shutdownCts.Dispose();
            }

            completion.SetResult();
        }
        catch (AggregateException exception)
        {
            completion.SetException(exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error processing membership updates."
    )]
    private partial void LogErrorProcessingMembershipUpdates(Exception exception);
}
