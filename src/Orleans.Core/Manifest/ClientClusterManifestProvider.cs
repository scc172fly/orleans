using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Diagnostics;
using Orleans.Internal;
using Orleans.Messaging;
using Orleans.Metadata;
using Orleans.Runtime.Utilities;

namespace Orleans.Runtime
{
    /// <summary>
    /// 面向外部客户端的 <see cref="IClusterManifestProvider"/> 实现。
    /// </summary>
    internal partial class ClientClusterManifestProvider : IClusterManifestProvider, IAsyncDisposable, IDisposable
    {
        private readonly ILogger<ClientClusterManifestProvider> _logger;
        private readonly TypeManagementOptions _typeManagementOptions;
        private readonly IServiceProvider _services;
        private readonly LocalClientDetails _localClientDetails;
        private readonly GatewayManager _gatewayManager;
        private readonly AsyncEnumerable<ClusterManifest> _updates;
        private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();
        private int _disposeState;
        private ClusterManifest _current;
        private Task? _runTask;

        public ClientClusterManifestProvider(
            IServiceProvider services,
            LocalClientDetails localClientDetails,
            GatewayManager gatewayManager,
            ILogger<ClientClusterManifestProvider> logger,
            ClientManifestProvider clientManifestProvider,
            IOptions<TypeManagementOptions> typeManagementOptions)
        {
            _logger = logger;
            _typeManagementOptions = typeManagementOptions.Value;
            _services = services;
            _localClientDetails = localClientDetails;
            _gatewayManager = gatewayManager;
            LocalGrainManifest = clientManifestProvider.ClientManifest;

            // 为最初的版本创建一个模拟清单，其中只包含本地客户端清单。
            var builder = ImmutableDictionary.CreateBuilder<SiloAddress, GrainManifest>();
            builder.Add(_localClientDetails.ClientAddress, LocalGrainManifest);
            _current = new ClusterManifest(MajorMinorVersion.MinValue, builder.ToImmutable());

            _updates = new AsyncEnumerable<ClusterManifest>(
                initialValue: _current,
                updateValidator: (previous, proposed) => proposed.Version > previous.Version,
                onPublished: update => Interlocked.Exchange(ref _current, update));
        }

        /// <inheritdoc />
        public ClusterManifest Current => _current;

        /// <inheritdoc />
        public IAsyncEnumerable<ClusterManifest> Updates => _updates;

        /// <inheritdoc />
        public GrainManifest LocalGrainManifest { get; }

        /// <summary>
        /// 启动此服务并等待初始集群清单。
        /// </summary>
        /// <remarks>
        /// 调用方将各次启动尝试串行化。初始化成功后，刷新操作随服务的生命周期运行。
        /// </remarks>
        /// <param name="cancellationToken">用于取消初始化并停止其后台工作的令牌。</param>
        /// <returns>一个在服务启动后即完成的 <see cref="Task"/>。</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.Assert(_runTask is null || _runTask.IsCompleted);
            var shutdownToken = _shutdownCts.Token;
            var initialization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runTask = _runTask = Task.Run(() => RunAsync(initialization), CancellationToken.None);
            try
            {
                var initialized = await initialization.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!initialized)
                {
                    await runTask.WaitAsync(cancellationToken);
                    Debug.Assert(shutdownToken.IsCancellationRequested);
                    throw new OperationCanceledException(shutdownToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await StopAsync(CancellationToken.None);
                throw;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1849:Call async methods when in an async method",
            Justification = "Shutdown must signal cancellation immediately without awaiting callback completion before observing the host stop token.")]
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _shutdownCts.Cancel();

                if (_runTask is { } task)
                {
                    await task.WaitAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogGracefulShutdownCanceled(_logger);
            }
            catch (Exception exception)
            {
                LogStoppingClusterManifestProvider(_logger, exception);
            }
        }

        private async Task RunAsync(TaskCompletionSource<bool> initialization)
        {
            try
            {
                var grainFactory = _services.GetRequiredService<IInternalGrainFactory>();
                SiloAddress? gateway = null;
                IClusterManifestSystemTarget? provider = null;
                var minorVersion = 0;
                var gatewayVersion = MajorMinorVersion.MinValue;
                while (!_shutdownCts.IsCancellationRequested)
                {
                    // 如果当前网关不可用，则选择新的网关。
                    // 这可能是暂时性问题或永久性网关故障所致。
                    if (gateway is null || !_gatewayManager.IsGatewayAvailable(gateway))
                    {
                        gateway = _gatewayManager.GetLiveGateway();
                        if (gateway is null)
                        {
                            await Task.Delay(StandardExtensions.Min(_typeManagementOptions.TypeMapRefreshInterval, TimeSpan.FromMilliseconds(500)), _shutdownCts.Token);
                            continue;
                        }

                        provider = grainFactory.GetGrain<IClusterManifestSystemTarget>(SystemTargetGrainId.Create(Constants.ManifestProviderType, gateway).GrainId);

                        // 接受新网关提供的任何集群清单版本。
                        // 由于清单的次版本号特定于各个网关，因此将其重置为最小可能值。
                        // 这意味着网关变更时可能收到更旧或等效的集群清单。
                        // 通过重置预期清单版本并合并不完整的清单，直到收到完整的
                        // 清单，来处理这一短暂异常。
                        gatewayVersion = MajorMinorVersion.MinValue;
                    }

                    Debug.Assert(provider is not null);

                    try
                    {
                        var updateResult = await GetClusterManifestUpdate(provider, gatewayVersion, _shutdownCts.Token);
                        if (updateResult is null)
                        {
                            // 没有更新的集群清单，因此等待下一个刷新间隔后重试。
                            await Task.Delay(_typeManagementOptions.TypeMapRefreshInterval, _shutdownCts.Token);
                            continue;
                        }

                        gatewayVersion = updateResult.Version;

                        // 如果清单未包含所有活动服务器，则与现有清单合并，直至包含所有活动服务器。
                        // 这样可以防止进度倒退，代价是可能包含已经失效的 Silo。
                        ImmutableDictionary<SiloAddress, GrainManifest> siloManifests;
                        if (!updateResult.IncludesAllActiveServers)
                        {
                            // 合并清单，直到其中包含所有活动服务器。
                            var mergedSilos = _current.Silos.ToBuilder();
                            mergedSilos.Add(_localClientDetails.ClientAddress, LocalGrainManifest);
                            foreach (var kvp in updateResult.SiloManifests)
                            {
                                mergedSilos[kvp.Key] = kvp.Value;
                            }

                            siloManifests = mergedSilos.ToImmutable();
                        }
                        else
                        {
                            siloManifests = updateResult.SiloManifests.Add(_localClientDetails.ClientAddress, LocalGrainManifest);
                        }

                        var updatedManifest = new ClusterManifest(new MajorMinorVersion(gatewayVersion.Major, ++minorVersion), siloManifests);
                        if (!_updates.TryPublish(updatedManifest))
                        {
                            await Task.Delay(StandardExtensions.Min(_typeManagementOptions.TypeMapRefreshInterval, TimeSpan.FromMilliseconds(500)), _shutdownCts.Token);
                            continue;
                        }

                        ManifestEvents.EmitClusterManifestUpdated(this, updatedManifest);
                        initialization.TrySetResult(true);

                        LogRefreshedClusterManifest(_logger);

                        await Task.Delay(_typeManagementOptions.TypeMapRefreshInterval, _shutdownCts.Token);
                    }
                    catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
                    {
                        // 在关闭期间忽略此情况。
                    }
                    catch (Exception exception)
                    {
                        LogErrorTryingToGetClusterManifest(_logger, exception, gateway);
                        await Task.Delay(StandardExtensions.Min(_typeManagementOptions.TypeMapRefreshInterval, TimeSpan.FromSeconds(5)), _shutdownCts.Token).SuppressThrowing();

                        // 重置网关，以便在下一次迭代中选择另一个网关。
                        gateway = null;
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                // 关闭操作也会中断等待可用网关期间的延迟。
            }
            finally
            {
                initialization.TrySetResult(false);

                LogStoppedRefreshingClusterManifest(_logger);
            }
        }

        private async Task<ClusterManifestUpdate?> GetClusterManifestUpdate(
            IClusterManifestSystemTarget provider,
            MajorMinorVersion previousVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                // 首先尝试调用提供更多信息的新 API。
                // 如果没有更新的集群清单，则返回 null。
                // 运行时拥有 RPC 回调。即使 RPC 策略要求确认取消，也要限制此提供程序的等待时间。
                var request = provider.GetClusterManifestUpdate(previousVersion, cancellationToken).AsTask();
                request.Ignore();
                return await request.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogFailedToFetchClusterManifestUpdate(_logger, exception, provider);

                // 如果提供程序不支持新 API，则回退到旧 API。
                var request = provider.GetClusterManifest(cancellationToken).AsTask();
                request.Ignore();
                var manifest = await request.WaitAsync(cancellationToken);
                var result = new ClusterManifestUpdate(manifest.Version, manifest.Silos, includesAllActiveServers: true);
                return result;
            }
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1849:Call async methods when in an async method",
            Justification = "Shutdown must signal cancellation immediately without awaiting callback completion before waiting for the run task.")]
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            try
            {
                if (!_shutdownCts.IsCancellationRequested)
                {
                    _shutdownCts.Cancel();
                }

                if (_runTask is Task task)
                {
                    await task.SuppressThrowing();
                }
            }
            finally
            {
                _shutdownCts.Dispose();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            try
            {
                _shutdownCts.Cancel();
                _runTask?.SuppressThrowing().GetAwaiter().GetResult();
            }
            finally
            {
                _shutdownCts.Dispose();
            }
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Graceful shutdown of cluster manifest provider was canceled."
        )]
        private static partial void LogGracefulShutdownCanceled(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error stopping cluster manifest provider."
        )]
        private static partial void LogStoppingClusterManifestProvider(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Refreshed cluster manifest."
        )]
        private static partial void LogRefreshedClusterManifest(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Error trying to get cluster manifest from gateway '{Gateway}'."
        )]
        private static partial void LogErrorTryingToGetClusterManifest(ILogger logger, Exception exception, SiloAddress gateway);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Stopped refreshing cluster manifest."
        )]
        private static partial void LogStoppedRefreshingClusterManifest(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to fetch cluster manifest update from '{Provider}'."
        )]
        private static partial void LogFailedToFetchClusterManifestUpdate(ILogger logger, Exception exception, IClusterManifestSystemTarget provider);
    }
}
