using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Diagnostics;
using Orleans.Internal;
using Orleans.Placement;
using Orleans.Runtime.Diagnostics;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Placement.Filtering;
using Orleans.Runtime.Versions;
using Polly;
using Polly.Registry;
using Polly.Timeout;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 安置决定的中心点。
    /// </summary>
    internal partial class PlacementService : IPlacementContext, ILifecycleParticipant<ISiloLifecycle>, PlacementService.ITestAccessor
    {
        private const int PlacementWorkerCount = 16;
        private readonly PlacementStrategyResolver _strategyResolver;
        private readonly PlacementDirectorResolver _directorResolver;
        private readonly ILogger<PlacementService> _logger;
        private readonly GrainLocator _grainLocator;
        private readonly GrainVersionManifest _grainInterfaceVersions;
        private readonly CachedVersionSelectorManager _versionSelectorManager;
        private readonly ISiloStatusOracle _siloStatusOracle;
        private readonly bool _assumeHomogeneousSilosForTesting;
        private readonly PlacementWorker[] _workers;
        private readonly PlacementFilterStrategyResolver _filterStrategyResolver;
        private readonly PlacementFilterDirectorResolver _placementFilterDirectoryResolver;
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly ResiliencePipeline<SiloAddress> _resiliencePipeline;

        internal interface ITestAccessor
        {
            Task[] WorkerTasks { get; }

            Task<SiloAddress> GetOrPlaceActivationAsync(Message message);
        }

        /// <summary>
        /// 创建 <see cref="PlacementService"/> 实例。
        /// </summary>
        public PlacementService(
            IOptionsMonitor<SiloMessagingOptions> siloMessagingOptions,
            ILocalSiloDetails localSiloDetails,
            ISiloStatusOracle siloStatusOracle,
            ILogger<PlacementService> logger,
            GrainLocator grainLocator,
            GrainVersionManifest grainInterfaceVersions,
            CachedVersionSelectorManager versionSelectorManager,
            PlacementDirectorResolver directorResolver,
            PlacementStrategyResolver strategyResolver,
            PlacementFilterStrategyResolver filterStrategyResolver,
            PlacementFilterDirectorResolver placementFilterDirectoryResolver,
            ResiliencePipelineProvider<string> resiliencePipelineProvider)
        {
            LocalSilo = localSiloDetails.SiloAddress;
            _strategyResolver = strategyResolver;
            _directorResolver = directorResolver;
            _filterStrategyResolver = filterStrategyResolver;
            _placementFilterDirectoryResolver = placementFilterDirectoryResolver;
            _logger = logger;
            _grainLocator = grainLocator;
            _grainInterfaceVersions = grainInterfaceVersions;
            _versionSelectorManager = versionSelectorManager;
            _siloStatusOracle = siloStatusOracle;
            _assumeHomogeneousSilosForTesting = siloMessagingOptions.CurrentValue.AssumeHomogenousSilosForTesting;
            _resiliencePipeline = resiliencePipelineProvider.GetPipeline<SiloAddress>(OrleansRuntimeResiliencePolicies.PlacementResiliencePipelineKey);
            _workers = new PlacementWorker[PlacementWorkerCount];
            for (var i = 0; i < PlacementWorkerCount; i++)
            {
                _workers[i] = new(this, i);
            }
        }

        public SiloAddress LocalSilo { get; }

        public SiloStatus LocalSiloStatus => _siloStatusOracle.CurrentStatus;

        Task[] ITestAccessor.WorkerTasks => _workers.Select(static worker => worker.CompletionTask).ToArray();

        Task<SiloAddress> ITestAccessor.GetOrPlaceActivationAsync(Message message)
        {
            if (message.TargetGrain.IsDefault)
            {
                throw new InvalidOperationException("Cannot address a message without a target");
            }

            var worker = _workers[message.TargetGrain.GetUniformHashCode() % PlacementWorkerCount];
            return worker.GetOrPlaceActivationAsyncForTest(message);
        }

        private bool IsStopping => _shutdownCts.IsCancellationRequested;

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(
                nameof(PlacementService),
                ServiceLifecycleStage.RuntimeInitialize + 1,
                _ => Task.CompletedTask,
                StopAsync);
        }

        private async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_shutdownCts.IsCancellationRequested)
            {
                _shutdownCts.Cancel();
            }

            foreach (var worker in _workers)
            {
                worker.Stop();
            }

            var completionTask = Task.WhenAll(_workers.Select(static worker => worker.CompletionTask));
            if (cancellationToken.IsCancellationRequested)
            {
                await completionTask.SuppressThrowing();
            }
            else
            {
                await completionTask.WaitAsync(cancellationToken).SuppressThrowing();
            }

        }

        /// <summary>
        /// 启动或设置激活。
        /// </summary>
        public Task AddressMessage(Message message)
        {
            if (message.IsTargetFullyAddressed) return Task.CompletedTask;
            if (message.TargetGrain.IsDefault) ThrowMissingAddress();
            ThrowIfStopping();

            var grainId = message.TargetGrain;
            if (_grainLocator.TryLookupInCache(grainId, out var result) && CachedAddressIsValid(message, result))
            {
                LogDebugFoundAddress(result, grainId, message);
                SetMessageTargetPlacement(message, result.SiloAddress);
                return Task.CompletedTask;
            }

            LogDebugLookingUpAddress(grainId, message);
            var worker = _workers[grainId.GetUniformHashCode() % PlacementWorkerCount];
            return worker.AddressMessage(message);

            static void ThrowMissingAddress() => throw new InvalidOperationException("Cannot address a message without a target");
        }

        private void SetMessageTargetPlacement(Message message, SiloAddress? targetSilo)
        {
            message.TargetSilo = targetSilo;
            LogTraceAddressMessageSelectTarget(message);
        }

        public SiloAddress[] GetCompatibleSilos(PlacementTarget target)
        {
            ThrowIfStopping();

            var grainType = target.GrainIdentity.Type;
            // 仅用于测试:如果我们有 silos 尚未列入组群 TypeMap,我们假定它们相容
            // 与当前 silo结合
            SiloAddress[] compatibleSilos;
            if (_assumeHomogeneousSilosForTesting)
            {
                compatibleSilos = _siloStatusOracle.GetActiveSilos();
            }
            else
            {
                compatibleSilos = GetCompatibleSilos(grainType, target.InterfaceType, target.InterfaceVersion);
                ThrowIfStopping();
                var filters = _filterStrategyResolver.GetPlacementFilterStrategies(grainType);
                if (filters.Length > 0)
                {
                    // 获取父活动环境, 所以每个过滤范围都以
                    // 当前活动 (e.g. PlaceGrain) 而不是用来为可能
                    // 推迟计数期间活动。
                    var parentActivityContext = Activity.Current?.Context;

                    IEnumerable<SiloAddress> filteredSilos = compatibleSilos;
                    foreach (var placementFilter in filters)
                    {
                        ThrowIfStopping();
                        var director = _placementFilterDirectoryResolver.GetFilterDirector(placementFilter);
                        filteredSilos = InstrumentFilteredSilos(
                            director.Filter(placementFilter, target, filteredSilos),
                            placementFilter,
                            grainType,
                            parentActivityContext);
                    }

                    ThrowIfStopping();
                    compatibleSilos = filteredSilos.ToArray();
                }
            }

            if (compatibleSilos.Length == 0)
            {
                if (_assumeHomogeneousSilosForTesting)
                {
                    throw new OrleansException($"No active nodes are compatible with grain {grainType} and interface {target.InterfaceType} version {target.InterfaceVersion}.");
                }

                var allWithType = _grainInterfaceVersions.GetSupportedSilos(grainType).Result;
                var versions = _grainInterfaceVersions.GetSupportedSilos(target.InterfaceType, target.InterfaceVersion).Result;
                var allWithTypeString = string.Join(", ", allWithType.Select(s => s.ToString())) is string withGrain && !string.IsNullOrWhiteSpace(withGrain) ? withGrain : "none";
                var allWithInterfaceString = string.Join(", ", versions.Select(s => s.ToString())) is string withIface && !string.IsNullOrWhiteSpace(withIface) ? withIface : "none";
                throw new OrleansException(
                    $"No active nodes are compatible with grain {grainType} and interface {target.InterfaceType} version {target.InterfaceVersion}. "
                    + $"Known nodes with grain type: {allWithTypeString}. "
                    + $"All known nodes compatible with interface version: {allWithInterfaceString}");
            }

            return compatibleSilos;
        }

        private SiloAddress[] GetCompatibleSilos(GrainType grainType, GrainInterfaceType interfaceType, ushort interfaceVersion)
        {
            if (interfaceVersion == 0)
            {
                return _versionSelectorManager.GetSupportedSilos(grainType);
            }

            return _versionSelectorManager.GetSuitableSilos(grainType, interfaceType, interfaceVersion).SuitableSilos;
        }

        public IReadOnlyDictionary<ushort, SiloAddress[]> GetCompatibleSilosWithVersions(PlacementTarget target)
        {
            if (target.InterfaceVersion == 0)
            {
                throw new ArgumentException("Interface version not provided", nameof(target));
            }

            ThrowIfStopping();
            var grainType = target.GrainIdentity.Type;
            return _versionSelectorManager
                .GetSuitableSilos(grainType, target.InterfaceType, target.InterfaceVersion)
                .SuitableSilosByVersion;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CachedAddressIsValid(Message message, GrainAddress cachedAddress)
        {
            // 验证缓存的结果是否已被信件所处理而无效 。
            if (message.CacheInvalidationHeader is { } cacheUpdates)
            {
                lock (cacheUpdates)
                {
                    return CachedAddressIsValidCore(message, cachedAddress, cacheUpdates);
                }
            }

            return true;

            [MethodImpl(MethodImplOptions.NoInlining)]
            bool CachedAddressIsValidCore(Message message, GrainAddress cachedAddress, List<GrainAddressCacheUpdate> cacheUpdates)
            {
                var resultIsValid = true;
                LogDebugInvalidatingCachedEntries(cacheUpdates.Count, message);

                foreach (var update in cacheUpdates)
                {
                    // 正在检查时, 无效/ 更新缓存条目 。
                    var invalidAddress = update.InvalidGrainAddress;
                    var validAddress = update.ValidGrainAddress;
                    _grainLocator.UpdateCache(update);

                    if (cachedAddress.Matches(validAddress))
                    {
                        resultIsValid = true;
                    }
                    else if (cachedAddress.Matches(invalidAddress))
                    {
                        resultIsValid = false;
                    }
                }

                return resultIsValid;
            }
        }

        /// <summary>
        /// 位于 grain 位置,而不考虑 grain现有地点(如果有的话)。
        /// </summary>
        /// <param name="grainId">下 grain 缩写 grain 被放置。</param>
        /// <param name="requestContextData">申请背景,将提供给安置战略。</param>
        /// <param name="placementStrategy">使用安置策略。</param>
        /// <returns>新激活的地点。</returns>
        public async Task<SiloAddress> PlaceGrainAsync(GrainId grainId, Dictionary<string, object>? requestContextData, PlacementStrategy placementStrategy)
        {
            using var _ = TryRestoreActivityContext(requestContextData, ActivityNames.PlaceGrain);
            var target = new PlacementTarget(grainId, requestContextData!, default, 0);
            ThrowIfStopping();
            var director = _directorResolver.GetPlacementDirector(placementStrategy);
            return await director.OnAddActivation(placementStrategy, target, this);
        }

        private void ThrowIfStopping()
        {
            if (IsStopping)
            {
                throw CreateStoppingException();
            }
        }

        private SiloUnavailableException CreateStoppingException() => new($"Silo '{LocalSilo}' is shutting down.");

        private partial class PlacementWorker
        {
            private readonly Dictionary<GrainId, GrainPlacementWorkItem> _inProgress = new();
            private readonly SingleWaiterAutoResetEvent _workSignal = new() { RunContinuationsAsynchronously = true };
            private readonly ILogger _logger;
#pragma warning disable IDE0052 // Remove unread private members. Justification: retained for debugging purposes
            private readonly Task _processLoopTask;
#pragma warning restore IDE0052 // Remove unread private members
            private readonly Lock _lockObj = new();
            private readonly PlacementService _placementService;
            private readonly Func<ResilienceContext, PlacementContext, ValueTask<Outcome<SiloAddress>>> _executePlacementDelegate;
            private readonly int _workerIndex;
            private List<(Message Message, TaskCompletionSource Completion)>? _messages = new();

            public PlacementWorker(PlacementService placementService, int workerIndex)
            {
                _logger = placementService._logger;
                _placementService = placementService;
                _workerIndex = workerIndex;
                _executePlacementDelegate = ExecutePlacementOutcomeAsync;

                using var _ = new ExecutionContextSuppressor();
                _processLoopTask = Task.Run(ProcessLoop);
            }

            public Task CompletionTask => _processLoopTask;

            internal Task<SiloAddress> GetOrPlaceActivationAsyncForTest(Message message) => GetOrPlaceActivationAsync(message);

            public void Stop() => _workSignal.Signal();

            public Task AddressMessage(Message message)
            {
                TaskCompletionSource completion;

                lock (_lockObj)
                {
                    if (_placementService.IsStopping)
                    {
                        return Task.FromException(_placementService.CreateStoppingException());
                    }

                    completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _messages ??= new();
                    _messages.Add((message, completion));
                }

                _workSignal.Signal();
                return completion.Task;
            }

            private List<(Message Message, TaskCompletionSource Completion)>? GetMessages()
            {
                lock (_lockObj)
                {
                    if (_messages is { Count: > 0 } result)
                    {
                        _messages = null;
                        return result;
                    }

                    return null;
                }
            }

            private bool HasPendingMessages()
            {
                lock (_lockObj)
                {
                    return _messages is { Count: > 0 };
                }
            }

            private async Task ProcessLoop()
            {
                try
                {
                    Action signalWaiter = _workSignal.Signal;
                    while (true)
                    {
                        try
                        {
                            // 开始处理新请求
                            var messages = GetMessages();
                            if (messages is not null)
                            {
                                if (_placementService.IsStopping)
                                {
                                    FailWaitingMessages(messages);
                                }
                                else
                                {
                                    foreach (var message in messages)
                                    {
                                        var target = message.Message.TargetGrain;
                                        var workItem = GetOrAddWorkItem(target);
                                        workItem.Messages.Add(message);
                                        if (workItem.Result is null)
                                        {
                                            // 请注意,第一条电文被用作发送电文的目标,
                                            // 如果后续信息与第一封电文的接口不一致,则可以
                                            // 类型或版本,然后它们可发送到不兼容的 silo,即
                                            // 由远程 silo 处理不兼容的问题,因此被罚款。
                                            workItem.Result = GetOrPlaceActivationAsync(message.Message);

                                            // 当任务完成时提醒此处理循环
                                            workItem.Result.GetAwaiter().UnsafeOnCompleted(signalWaiter);
                                        }
                                    }
                                }
                            }

                            // 完成处理任何已完成的请求
                            foreach (var pair in _inProgress)
                            {
                                var workItem = pair.Value;
                                if (workItem.Result.IsCompleted)
                                {
                                    AddressWaitingMessages(workItem);
                                    _inProgress.Remove(pair.Key);
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            LogWarnInPlacementWorker(_logger, exception);
                        }

                        if (_placementService.IsStopping && _inProgress.Count == 0 && !HasPendingMessages())
                        {
                            break;
                        }

                        await _workSignal.WaitAsync();
                    }

                    GrainPlacementWorkItem GetOrAddWorkItem(GrainId target)
                    {
                        ref var workItem = ref CollectionsMarshal.GetValueRefOrAddDefault(_inProgress, target, out _);
                        workItem ??= new();
                        return workItem;
                    }
                }
                finally
                {
                    PlacementServiceEvents.EmitWorkerStopped(_placementService.LocalSilo, _workerIndex);
                }
            }

            private void FailWaitingMessages(List<(Message Message, TaskCompletionSource Completion)> messages)
            {
                foreach (var message in messages)
                {
                    message.Completion.TrySetException(_placementService.CreateStoppingException());
                }

                messages.Clear();
            }

            private void AddressWaitingMessages(GrainPlacementWorkItem completedWorkItem)
            {
                var resultTask = completedWorkItem.Result;
                var messages = completedWorkItem.Messages;

                try
                {
                    var siloAddress = resultTask.GetAwaiter().GetResult();
                    foreach (var message in messages)
                    {
                        _placementService.SetMessageTargetPlacement(message.Message, siloAddress);
                        message.Completion.TrySetResult();
                    }
                }
                catch (Exception exception)
                {
                    var originalException = exception switch
                    {
                        AggregateException ae when ae.InnerExceptions.Count == 1 => ae.InnerException!,
                        _ => exception,
                    };

                    foreach (var message in messages)
                    {
                        message.Completion.TrySetException(originalException);
                    }
                }

                messages.Clear();
            }

            private async Task<SiloAddress> GetOrPlaceActivationAsync(Message firstMessage)
            {
                await Task.Yield();
                _placementService.ThrowIfStopping();

                if (firstMessage.IsExpired)
                {
                    LogMessageExpiredDuringPlacement(_logger, firstMessage.TargetGrain);
                    throw new OperationCanceledException($"Message expired before placement could complete for grain {firstMessage.TargetGrain}.");
                }

                var target = new PlacementTarget(
                    firstMessage.TargetGrain,
                    firstMessage.RequestContextData!,
                    firstMessage.InterfaceType,
                    firstMessage.InterfaceVersion);

                var currentActivity = Activity.Current;
                var resilienceContext = ResilienceContextPool.Shared.Get(_placementService._shutdownCts.Token);
                try
                {
                    var outcome = await _placementService._resiliencePipeline.ExecuteOutcomeAsync(
                        _executePlacementDelegate,
                        resilienceContext,
                        new PlacementContext(firstMessage, target));
                    outcome.ThrowIfException();
                    return outcome.Result!;
                }
                catch (TimeoutRejectedException exception)
                {
                    throw new TimeoutException($"Grain placement operation timed out for grain {firstMessage.TargetGrain}.", exception);
                }
                catch (OperationCanceledException) when (_placementService.IsStopping)
                {
                    throw _placementService.CreateStoppingException();
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(resilienceContext);
                    Activity.Current = currentActivity;
                }
            }

            private ValueTask<Outcome<SiloAddress>> ExecutePlacementOutcomeAsync(ResilienceContext context, PlacementContext state)
            {
                try
                {
                    var pending = ExecutePlacementAsync(state, context.CancellationToken);
                    return pending.IsCompletedSuccessfully
                        ? Outcome.FromResultAsValueTask(pending.Result)
                        : AwaitOutcome(pending);
                }
                catch (Exception exception)
                {
                    return Outcome.FromExceptionAsValueTask<SiloAddress>(exception);
                }

                static async ValueTask<Outcome<SiloAddress>> AwaitOutcome(ValueTask<SiloAddress> pending)
                {
                    try
                    {
                        return Outcome.FromResult(await pending);
                    }
                    catch (Exception exception)
                    {
                        return Outcome.FromException<SiloAddress>(exception);
                    }
                }
            }

            private async ValueTask<SiloAddress> ExecutePlacementAsync(PlacementContext context, CancellationToken cancellationToken)
            {
                var firstMessage = context.FirstMessage;
                var target = context.Target;
                var targetGrain = target.GrainIdentity;

                if (firstMessage.IsExpired)
                {
                    LogMessageExpiredDuringPlacement(_logger, targetGrain);
                    throw new OperationCanceledException($"Message expired before placement could complete for grain {targetGrain}.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 每次尝试都恢复活动环境,因此重新调查后的调查仍然是请求中的儿童。
                using var restoredActivity = TryRestoreActivityContext(firstMessage.RequestContextData, ActivityNames.PlaceGrain);
                var lookup = _placementService._grainLocator.Lookup(targetGrain);
                var result = lookup.IsCompletedSuccessfully
                    ? lookup.Result
                    : await lookup.AsTask().WaitAsync(cancellationToken);
                if (result is not null)
                {
                    return result.SiloAddress!;
                }

                _placementService.ThrowIfStopping();
                var strategy = _placementService._strategyResolver.GetPlacementStrategy(target.GrainIdentity.Type);
                var director = _placementService._directorResolver.GetPlacementDirector(strategy);
                var siloAddress = await director.OnAddActivation(strategy, target, _placementService).WaitAsync(cancellationToken);

                // 给 grain 定位器最后一次机会告诉我们 grain 已经放置
                if (_placementService._grainLocator.TryLookupInCache(targetGrain, out result) && _placementService.CachedAddressIsValid(firstMessage, result))
                {
                    return result.SiloAddress!;
                }

                _placementService._grainLocator.InvalidateCache(targetGrain);
                _placementService._grainLocator.UpdateCache(targetGrain, siloAddress);
                return siloAddress;
            }

            [LoggerMessage(
                Level = LogLevel.Debug,
                Message = "Message expired during placement for grain {GrainId}."
            )]
            private static partial void LogMessageExpiredDuringPlacement(ILogger logger, GrainId grainId);

            /// <summary>
            /// 环境转至复原力管道,以避免分配。
            /// </summary>
            private readonly struct PlacementContext(Message firstMessage, PlacementTarget target)
            {
                public Message FirstMessage { get; } = firstMessage;
                public PlacementTarget Target { get; } = target;
            }

            private class GrainPlacementWorkItem
            {
                public List<(Message Message, TaskCompletionSource Completion)> Messages { get; } = new();

                public Task<SiloAddress> Result { get; set; } = null!;
            }
        }

        /// <summary>
        /// 将过滤器的输出以数字方式进行折行,以便当
        /// 实际列出序列,而不是当过滤器组成时。此选项避免
        /// 每一过滤器阵列实现,同时仍然提供准确的间隔时间。
        /// </summary>
        private IEnumerable<SiloAddress> InstrumentFilteredSilos(
            IEnumerable<SiloAddress> silos,
            PlacementFilterStrategy filter,
            GrainType grainType,
            ActivityContext? parentActivityContext)
        {
            ThrowIfStopping();
            using var filterSpan = parentActivityContext is { } parentContext
                ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.FilterPlacementCandidates, ActivityKind.Internal, parentContext)
                : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.FilterPlacementCandidates);
            filterSpan?.SetTag(ActivityTagKeys.PlacementFilterType, filter.GetType().Name);
            filterSpan?.SetTag(ActivityTagKeys.GrainType, grainType.ToString());

            foreach (var silo in silos)
            {
                ThrowIfStopping();
                yield return silo;
            }
        }

        /// <summary>
        /// 试图从请求的上下文数据中恢复父活动背景。
        /// </summary>
        private static Activity? TryRestoreActivityContext(Dictionary<string, object>? requestContextData, string operationName)
        {
            if (requestContextData is null)
            {
                return null;
            }

            var activityContext = requestContextData.TryGetActivityContext();

            if (activityContext is { } parentContext)
            {
                // 从目录 ActivitySource 开始活动, 以适当将其与激活跟踪连接
                return ActivitySources.LifecycleGrainSource.StartActivity(operationName, ActivityKind.Internal, parentContext);
            }

            return null;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Found address {Address} for grain {GrainId} in cache for message {Message}"
        )]
        private partial void LogDebugFoundAddress(GrainAddress address, GrainId grainId, Message message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Looking up address for grain {GrainId} for message {Message}"
        )]
        private partial void LogDebugLookingUpAddress(GrainId grainId, Message message);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "AddressMessage Placement SelectTarget {Message}"
        )]
        private partial void LogTraceAddressMessageSelectTarget(Message message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Invalidating {Count} cached entries for message {Message}"
        )]
        private partial void LogDebugInvalidatingCachedEntries(int count, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Error in placement worker."
        )]
        private static partial void LogWarnInPlacementWorker(ILogger logger, Exception exception);

    }
}
