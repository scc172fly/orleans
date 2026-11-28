using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Data;
using Orleans.Internal;
using Orleans.Configuration;
using Orleans.Runtime.Utilities;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Orleans.Placement.Repartitioning;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Orleans.Runtime.Placement.Repartitioning;

// 参见：https://www.microsoft.com/en-us/research/wp-content/uploads/2016/06/eurosys16loca_camera_ready-1.pdf
internal sealed partial class ActivationRepartitioner : SystemTarget, IActivationRepartitionerSystemTarget, ILifecycleParticipant<ISiloLifecycle>, IDisposable, ISiloStatusListener
{
    internal sealed class DeactivatedGrainQueue : IActivationWorkingSetObserver
    {
        // 交换可能长时间被跳过，因此保持此尽力而为队列的容量有界。
        internal const int MaxTrackedDeactivatedGrains = 16_384;
        private readonly ConcurrentQueue<GrainId> _deactivatedGrains = new();
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void DrainTo(HashSet<GrainId> destination)
        {
            while (_deactivatedGrains.TryDequeue(out var grainId))
            {
                Interlocked.Decrement(ref _count);
                destination.Add(grainId);
            }
        }

        void IActivationWorkingSetObserver.OnDeactivated(IActivationWorkingSetMember member)
        {
            if (member is IGrainContext context)
            {
                Add(context.GrainId);
            }
        }

        internal bool Add(GrainId grainId)
        {
            while (true)
            {
                var count = Volatile.Read(ref _count);
                if (count >= MaxTrackedDeactivatedGrains)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _count, count + 1, count) == count)
                {
                    _deactivatedGrains.Enqueue(grainId);
                    return true;
                }
            }
        }
    }

    private readonly DeactivatedGrainQueue _deactivatedGrains;
    private readonly ILogger _logger;
    private readonly ISiloStatusOracle _siloStatusOracle;
    private readonly IInternalGrainFactory _grainFactory;
    private readonly IRepartitionerMessageFilter _messageFilter;
    private readonly IImbalanceToleranceRule _toleranceRule;
    private readonly IActivationMigrationManager _migrationManager;
    private readonly ActivationDirectory _activationDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly ActivationRepartitionerOptions _options;
    private readonly StripedMpscBuffer<Message> _pendingMessages;
    private readonly SingleWaiterAutoResetEvent _pendingMessageEvent = new() { RunContinuationsAsynchronously = true };
    private readonly FrequentEdgeCounter _edgeWeights;
    private readonly IGrainTimer _timer;
    private SiloAddress? _currentExchangeSilo;
    private CoarseStopwatch _lastExchangedStopwatch;
    private int _activationCountOffset;
    private bool _enableMessageSampling;

    public ActivationRepartitioner(
        ISiloStatusOracle siloStatusOracle,
        ILoggerFactory loggerFactory,
        IInternalGrainFactory internalGrainFactory,
        IRepartitionerMessageFilter messageFilter,
        IImbalanceToleranceRule toleranceRule,
        IActivationMigrationManager migrationManager,
        ActivationDirectory activationDirectory,
        DeactivatedGrainQueue deactivatedGrainQueue,
        IOptions<ActivationRepartitionerOptions> options,
        [FromKeyedServices(TimeProviderNames.ActivationManagement)] TimeProvider timeProvider,
        SystemTargetShared shared)
        : base(Constants.ActivationRepartitionerType, shared)
    {
        _deactivatedGrains = deactivatedGrainQueue;
        _logger = loggerFactory.CreateLogger<ActivationRepartitioner>();
        _options = options.Value;
        _siloStatusOracle = siloStatusOracle;
        _grainFactory = internalGrainFactory;
        _messageFilter = messageFilter;
        _toleranceRule = toleranceRule;
        _migrationManager = migrationManager;
        _activationDirectory = activationDirectory;
        _timeProvider = timeProvider;
        _edgeWeights = new(options.Value.MaxEdgeCount);
        _lastExchangedStopwatch = CoarseStopwatch.StartNew();
        _pendingMessages = new StripedMpscBuffer<Message>(Environment.ProcessorCount, options.Value.MaxUnprocessedEdges / Environment.ProcessorCount);
        shared.ActivationDirectory.RecordNewTarget(this);
        _siloStatusOracle.SubscribeToSiloStatusEvents(this);
        _timer = RegisterTimer(
            _ => TriggerExchangeRequest(_shutdownCts.Token).AsTask(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        if (options.Value.AnchoringFilterEnabled)
        {
            _anchoredFilter = new AnchoredGrainsFilter(100_000, options.Value.ProbabilisticFilteringMaxAllowedErrorRate, options.Value.AnchoringFilterGenerations);
        }
    }

    private Task OnActiveStart(CancellationToken cancellationToken)
    {
        Scheduler.QueueAction(() =>
        {
            // 安排定时器的首次触发。
            UpdateTimer();
            StartProcessingEdges();
        });

        return Task.CompletedTask;
    }

    public ValueTask ResetCounters(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pendingMessages.Clear();
        _edgeWeights.Clear();
        _anchoredFilter?.Reset();
        return ValueTask.CompletedTask;
    }

    ValueTask<int> IActivationRepartitionerSystemTarget.GetActivationCount(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_activationDirectory.Count);
    }

    ValueTask IActivationRepartitionerSystemTarget.SetActivationCountOffset(
        int activationCountOffset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _activationCountOffset = activationCountOffset;
        return ValueTask.CompletedTask;
    }

    private void UpdateTimer() => UpdateTimer(RandomTimeSpan.Next(_options.MinRoundPeriod, _options.MaxRoundPeriod));
    private void UpdateTimer(TimeSpan dueTime)
    {
        _timer.Change(dueTime, dueTime);
        LogPeriodicallyInvokeProtocol(_options.MinRoundPeriod, _options.MaxRoundPeriod, dueTime);
    }

    public async ValueTask TriggerExchangeRequest(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_anchoredFilter is { } filter)
        {
            filter.Rotate();
        }

        var coolDown = _options.RecoveryPeriod - _lastExchangedStopwatch.Elapsed;
        if (coolDown > TimeSpan.Zero)
        {
            LogCoolingDown(coolDown);
            await Task.Delay(coolDown, _timeProvider, cancellationToken);
        }

        // 安排定时器的下一次触发。
        UpdateTimer();

        if (_currentExchangeSilo is not null)
        {
            // 若已在与其他 Silo 交换，则跳过本轮。
            return;
        }

        var silos = _siloStatusOracle.GetActiveSilos();
        if (silos.Length == 1)
        {
            LogSingleSiloCluster();
            return;
        }
        else if (!_enableMessageSampling)
        {
            return;
        }

        var sw = ValueStopwatch.StartNew();
        var migrationCandidates = GetMigrationCandidates();
        var sets = CreateCandidateSets(migrationCandidates, silos);
        var anchoredSet = ComputeAnchoredGrains(migrationCandidates);
        LogComputedCandidateSets(sw.Elapsed);
        foreach ((var candidateSilo, var offeredGrains, var _) in sets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offeredGrains.Count == 0)
            {
                LogExchangeSetIsEmpty(candidateSilo);
                continue;
            }

            try
            {
                // 在操作期间设置交换对端。
                // 这样可阻止其他请求交错执行。
                _currentExchangeSilo = candidateSilo;

                LogBeginningProtocol(Silo, candidateSilo);
                var remoteRef = IActivationRepartitionerSystemTarget.GetReference(_grainFactory, candidateSilo);
                var response = await remoteRef.AcceptExchangeRequest(
                    new(Silo, [.. offeredGrains], GetLocalActivationCount()),
                    CancellationToken.None);

                switch (response.Type)
                {
                    case AcceptExchangeResponse.ResponseType.Success:
                        // 交换成功，无需继续遍历其他候选项。
                        await FinalizeProtocol(response.AcceptedGrainIds, response.GivenGrainIds, candidateSilo, anchoredSet);
                        return;
                    case AcceptExchangeResponse.ResponseType.ExchangedRecently:
                        // 远程 Silo 最近参与过另一场交换，尝试次优候选项。
                        LogExchangedRecentlyResponse(Silo, candidateSilo);
                        continue;
                    case AcceptExchangeResponse.ResponseType.MutualExchangeAttempt:
                        // 远程 Silo 已在与当前 Silo 交换，且远程 Silo 发起的交换
                        // 优先于当前 Silo 正在发起的交换。
                        LogMutualExchangeAttemptResponse(Silo, candidateSilo);
                        return;
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogErrorOnProtocolExecution(ex, Silo, candidateSilo);
                continue; // 发生问题，尝试次优候选项。
            }
            finally
            {
                _currentExchangeSilo = null;
            }
        }
    }

    private int GetLocalActivationCount() => _activationDirectory.Count + _activationCountOffset;

    public async ValueTask<AcceptExchangeResponse> AcceptExchangeRequest(
        AcceptExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LogReceivedExchangeRequest(request.SendingSilo, request.ExchangeSet.Length, request.ActivationCountSnapshot);
        if (request.SendingSilo.Equals(_currentExchangeSilo) && Silo.CompareTo(request.SendingSilo) <= 0)
        {
            // 拒绝请求，因为已在与发送方 Silo 进行交换。
            // 此处的 '<=' 比较用于在两个 Silo 同时向对方发起交换时确定优先方。

            // 在 'min' 和 'max' 之间随机选择时间，再减去 'min'。这样当前 Silo 无需像首次启动时那样等待 'min + random'。
            // 由于 'random' 至少等于 'min'，因此保证 'random - min' >= 0。
            UpdateTimer(RandomTimeSpan.Next(_options.MinRoundPeriod, _options.MaxRoundPeriod) - _options.MinRoundPeriod);
            LogMutualExchangeAttempt(request.SendingSilo);

            return AcceptExchangeResponse.CachedMutualExchangeAttempt;
        }

        var lastExchangeElapsed = _lastExchangedStopwatch.Elapsed;
        if (lastExchangeElapsed < _options.RecoveryPeriod)
        {
            LogExchangedRecently(request.SendingSilo, lastExchangeElapsed, _options.RecoveryPeriod);
            return AcceptExchangeResponse.CachedExchangedRecently;
        }

        // 在请求处理期间设置交换 Silo。
        // 这样可阻止其他请求交错执行。
        _currentExchangeSilo = request.SendingSilo;

        try
        {
            var acceptedGrains = ImmutableArray.CreateBuilder<GrainId>();
            var givingGrains = ImmutableArray.CreateBuilder<GrainId>();
            var remoteSet = request.ExchangeSet;
            var migrationCandidates = GetMigrationCandidates();
            var localSet = GetCandidatesForSilo(migrationCandidates, request.SendingSilo);
            var anchoredSet = ComputeAnchoredGrains(migrationCandidates);

            // 需要确定两个子集：
            // - 源自发送方 Silo（request.ExchangeSet），由当前 Silo（部分）接收的子集。
            // - 源自当前 Silo（candidateSet），由发送方 Silo（全部）接收的子集。
            var remoteActivations = request.ActivationCountSnapshot;
            var localActivations = GetLocalActivationCount();

            var initialImbalance = CalculateImbalance(remoteActivations, localActivations);
            int imbalance = initialImbalance;
            LogImbalance(imbalance, remoteActivations, localActivations);

            var stopwatch = ValueStopwatch.StartNew();
            var (localHeap, remoteHeap) = CreateCandidateHeaps(localSet, remoteSet);
            LogComputedCandidateHeaps(stopwatch.Elapsed);
            stopwatch.Restart();

            var iterations = 0;
            var yieldStopwatch = CoarseStopwatch.StartNew();
            while (true)
            {
                if (++iterations % 128 == 0 && yieldStopwatch.ElapsedMilliseconds > 25)
                {
                    // 定期让其他任务有机会执行。
                    yieldStopwatch.Restart();
                    await Task.Delay(1, cancellationToken);
                }

                // 若向远程 Silo 迁出 Grain 比从其迁入 Grain 获得的收益更大，则优先尝试迁出。
                var localScore = localHeap.FirstOrDefault()?.AccumulatedTransferScore ?? 0;
                var remoteScore = remoteHeap.FirstOrDefault()?.AccumulatedTransferScore ?? 0;
                if (localScore > remoteScore || localScore == remoteScore && localActivations > remoteActivations)
                {
                    if (TryMigrateLocalToRemote()) continue;
                    if (TryMigrateRemoteToLocal()) continue;
                }
                else
                {
                    if (TryMigrateRemoteToLocal()) continue;
                    if (TryMigrateLocalToRemote()) continue;
                }

                // 已无法继续迁移，因此候选集合计算完毕。
                break;
            }

            LogTransferSetComputed(stopwatch.Elapsed, imbalance);
            var giving = givingGrains.ToImmutable();
            var accepting = acceptedGrains.ToImmutable();
            cancellationToken.ThrowIfCancellationRequested();
            await FinalizeProtocol(giving, accepting, request.SendingSilo, anchoredSet);

            return new(AcceptExchangeResponse.ResponseType.Success, accepting, giving);

            bool TryMigrateLocalToRemote()
            {
                if (!TryMigrateCore(localHeap, localDelta: -1, remoteDelta: 1, out var chosenVertex))
                {
                    return false;
                }

                givingGrains.Add(chosenVertex.Id);
                foreach (var (connectedVertex, transferScore) in chosenVertex.ConnectedVertices)
                {
                    switch (connectedVertex.Location)
                    {
                        case VertexLocation.Local:
                            // 加上迁移评分，因为这两个向量将处于不同的 Silo。
                            connectedVertex.AccumulatedTransferScore += transferScore;
                            localHeap.OnIncreaseElementPriority(connectedVertex);
                            break;
                        case VertexLocation.Remote:
                            // 减去迁移评分，因为这两个向量将处于同一个 Silo。
                            connectedVertex.AccumulatedTransferScore -= transferScore;
                            remoteHeap.OnDecreaseElementPriority(connectedVertex);
                            break;
                    }
                }

                // 后续操作均假定该向量位于远程 Silo。
                chosenVertex.Location = VertexLocation.Remote;
                Debug.Assert(((IHeapElement<CandidateVertexHeapElement>)chosenVertex).HeapIndex == -1);

                return true;
            }

            bool TryMigrateRemoteToLocal()
            {
                if (!TryMigrateCore(remoteHeap, localDelta: 1, remoteDelta: -1, out var chosenVertex))
                {
                    return false;
                }

                acceptedGrains.Add(chosenVertex.Id);
                foreach (var (connectedVertex, transferScore) in chosenVertex.ConnectedVertices)
                {
                    switch (connectedVertex.Location)
                    {
                        case VertexLocation.Local:
                            // 减去迁移评分，因为这两个向量将处于同一个 Silo。
                            connectedVertex.AccumulatedTransferScore -= transferScore;
                            localHeap.OnDecreaseElementPriority(connectedVertex);
                            break;
                        case VertexLocation.Remote:
                            // 加上迁移评分，因为这两个向量将处于不同的 Silo。
                            connectedVertex.AccumulatedTransferScore += transferScore;
                            remoteHeap.OnIncreaseElementPriority(connectedVertex);
                            break;
                    }
                }

                // 后续操作均假定该向量位于本地 Silo。
                chosenVertex.Location = VertexLocation.Local;

                return true;
            }

            bool TryMigrateCore(MaxHeap<CandidateVertexHeapElement> sourceHeap, int localDelta, int remoteDelta, [NotNullWhen(true)] out CandidateVertexHeapElement? chosenVertex)
            {
                var anticipatedImbalance = CalculateImbalance(localActivations + localDelta, remoteActivations + remoteDelta);
                if (anticipatedImbalance >= imbalance && !_toleranceRule.IsSatisfiedBy((uint)anticipatedImbalance))
                {
                    // 从此堆获取元素无法改善不均衡程度。
                    chosenVertex = null;
                    return false;
                }

                if (!sourceHeap.TryPop(out chosenVertex))
                {
                    // 堆为空。
                    return false;
                }

                if (chosenVertex.AccumulatedTransferScore <= 0)
                {
                    // 如果它受之前操作影响，且评分为零或负数，则直接弹出并忽略。
                    return false;
                }

                localActivations += localDelta;
                remoteActivations += remoteDelta;
                imbalance = anticipatedImbalance;
                return true;
            }

        }
        catch (Exception exception)
        {
            LogErrorAcceptingExchangeRequest(exception, request.SendingSilo);
            throw;
        }
        finally
        {
            _currentExchangeSilo = null;
        }
    }

    private static int CalculateImbalance(int left, int right) => Math.Abs(Math.Abs(left) - Math.Abs(right));
    private static (MaxHeap<CandidateVertexHeapElement> Local, MaxHeap<CandidateVertexHeapElement> Remote) CreateCandidateHeaps(List<CandidateVertex> local, ImmutableArray<CandidateVertex> remote)
    {
        Dictionary<GrainId, CandidateVertex> sourceIndex = new(local.Count + remote.Length);
        foreach (var element in local)
        {
            sourceIndex[element.Id] = element;
        }

        foreach (var element in remote)
        {
            sourceIndex[element.Id] = element;
        }

        Dictionary<GrainId, CandidateVertexHeapElement> heapIndex = [];
        List<CandidateVertexHeapElement> localVertexList = new(local.Count);
        foreach (var element in local)
        {
            var vertex = CreateVertex(sourceIndex, heapIndex, element);
            vertex.Location = VertexLocation.Local;
            localVertexList.Add(vertex);
        }

        List<CandidateVertexHeapElement> remoteVertexList = new(remote.Length);
        foreach (var element in remote)
        {
            var vertex = CreateVertex(sourceIndex, heapIndex, element);
            if (vertex.Location is not VertexLocation.Unknown)
            {
                // 此顶点已属于本地集合，因此假定它位于本地，并忽略远程顶点。
                continue;
            }

            vertex.Location = VertexLocation.Remote;
            remoteVertexList.Add(vertex);
        }

        var localHeap = new MaxHeap<CandidateVertexHeapElement>(localVertexList);
        var remoteHeap = new MaxHeap<CandidateVertexHeapElement>(remoteVertexList);
        return (localHeap, remoteHeap);

        static CandidateVertexHeapElement CreateVertex(Dictionary<GrainId, CandidateVertex> sourceIndex, Dictionary<GrainId, CandidateVertexHeapElement> index, CandidateVertex element)
        {
            var vertex = GetOrAddVertex(index, element);
            foreach (var connectedVertex in element.ConnectedVertices)
            {
                if (sourceIndex.TryGetValue(connectedVertex.Id, out var connected))
                {
                    vertex.ConnectedVertices.Add((GetOrAddVertex(index, connected), connectedVertex.TransferScore));
                }
                else
                {
                    // 相连顶点不属于任一迁移候选集合，因此忽略该顶点。
                }
            }

            return vertex;

            static CandidateVertexHeapElement GetOrAddVertex(Dictionary<GrainId, CandidateVertexHeapElement> index, CandidateVertex element)
            {
                ref var vertex = ref CollectionsMarshal.GetValueRefOrAddDefault(index, element.Id, out var exists);
                vertex ??= new(element);
                return vertex;
            }
        }
    }

    /// <summary>
    /// <list type="number">
    /// <item>发起将 <paramref name="giving"/> 从本地 Silo 迁移至 <paramref name="targetSilo"/> 的操作。</item>
    /// <item>更新受影响的计数器，以反映两个 Silo 之间交换的 Grain。</item>
    /// </list>
    /// </summary>
    /// <param name="giving">要从本地 Silo 迁移至 <paramref name="targetSilo"/> 的 Grain 标识。</param>
    /// <param name="accepting">从 <paramref name="targetSilo"/> 迁移至本地 Silo 的 Grain 标识。</param>
    /// <param name="targetSilo">与本地 Silo 参与交换的 Silo。</param>
    /// <param name="newlyAnchoredGrains">其通信模式更适合留在本地 Silo 的本地 Grain 标识。</param>
    /// <returns>表示交换收尾操作的任务。</returns>
    internal async Task FinalizeProtocol(ImmutableArray<GrainId> giving, ImmutableArray<GrainId> accepting, SiloAddress targetSilo, HashSet<GrainId> newlyAnchoredGrains)
    {
        // 协议确定当前 Silo 应接收该集合，因此相应地向决策器提供提示。
        var affected = new HashSet<GrainId>(giving.Length + accepting.Length + _deactivatedGrains.Count);
        _deactivatedGrains.DrainTo(affected);

        try
        {
            Dictionary<string, object> migrationRequestContext = new() { [IPlacementDirector.PlacementHintKey] = targetSilo };
            var deactivationTasks = new List<Task>();
            foreach (var grainId in giving)
            {
                if (_activationDirectory.FindTarget(grainId) is { } localActivation)
                {
                    if (localActivation is ActivationData activation)
                    {
                        if (activation.TryStartMigration(migrationRequestContext))
                        {
                            deactivationTasks.Add(activation.Deactivated);
                        }
                    }
                    else
                    {
                        localActivation.Migrate(migrationRequestContext);
                    }
                }
            }

            await Task.WhenAll(deactivationTasks);
        }
        catch (Exception exception)
        {
            // 这种情况应该很少发生，但此时已超出我们的控制范围，无法采取太多措施。
            // 即使部分迁移失败，算法最终还会再次运行，因此激活实例仍有后续迁移机会。
            LogErrorOnMigratingActivations(exception);
        }

        if (_anchoredFilter is { } filter)
        {
            LogAddingAnchoredGrains(newlyAnchoredGrains.Count, Silo, _edgeWeights.Count);
            foreach (var id in newlyAnchoredGrains)
            {
                filter.Add(id);
            }
        }

        foreach (var id in accepting)
        {
            affected.Add(id);
        }

        foreach (var id in giving)
        {
            affected.Add(id);
        }

        await PruneAffectedEdges(_edgeWeights, affected, _anchoredFilter);

        // 记录此 Silo 的交换时间，供可能的下一次配对交换请求使用。
        _lastExchangedStopwatch.Restart();
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            LogProtocolFinalizedTrace(string.Join(", ", giving), string.Join(", ", accepting));
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogProtocolFinalized(giving.Length, accepting.Length);
        }
    }

    internal static async Task PruneAffectedEdges(FrequentEdgeCounter edgeWeights, HashSet<GrainId> affected, AnchoredGrainsFilter? anchoredFilter)
    {
        if (affected.Count > 0)
        {
            var iterations = 0;
            var toRemove = new List<Edge>();
            var yieldStopwatch = CoarseStopwatch.StartNew();
            foreach (var (edge, _, _) in edgeWeights.Elements)
            {
                if (affected.Contains(edge.Source.Id) || affected.Contains(edge.Target.Id) || anchoredFilter is not null && (anchoredFilter.Contains(edge.Source.Id) || anchoredFilter.Contains(edge.Target.Id)))
                {
                    toRemove.Add(edge);
                }
            }

            foreach (var edge in toRemove)
            {
                if (++iterations % 128 == 0 && yieldStopwatch.ElapsedMilliseconds > 25)
                {
                    // 定期让其他任务有机会执行。
                    yieldStopwatch.Restart();
                    await Task.Delay(1);
                }

                // 完全移除此计数器，因为一个或两个顶点已迁移；否则会导致下一轮协议的结果失真。
                // 仅移除受影响的计数器，因为当前 Silo 可能还有与其他 Silo（未参与本轮交换）连接的计数器。
                edgeWeights.Remove(edge);
            }
        }
    }

    private List<(SiloAddress Silo, List<CandidateVertex> Candidates, long TransferScore)> CreateCandidateSets(List<IGrouping<GrainId, VertexEdge>> migrationCandidates, SiloAddress[] silos)
    {
        List<(SiloAddress Silo, List<CandidateVertex> Candidates, long TransferScore)> candidateSets = new(silos.Length - 1);

        foreach (var siloAddress in silos)
        {
            if (siloAddress.Equals(Silo))
            {
                // 不会与自身交换任何内容，因此跳过当前 Silo。
                continue;
            }

            var candidatesForRemote = GetCandidatesForSilo(migrationCandidates, siloAddress);
            var totalAccTransferScore = candidatesForRemote.Sum(x => x.AccumulatedTransferScore);

            candidateSets.Add(new(siloAddress, [.. candidatesForRemote], totalAccTransferScore));
        }

        // 按累计迁移评分从高到低排序。
        candidateSets.Sort(static (a, b) => -a.TransferScore.CompareTo(b.TransferScore));

        return candidateSets;
    }

    private List<CandidateVertex> GetCandidatesForSilo(List<IGrouping<GrainId, VertexEdge>> migrationCandidates, SiloAddress otherSilo)
    {
        Debug.Assert(!otherSilo.Equals(Silo));

        List<CandidateVertex> result = [];

        // 跳过不可迁移的类型。相同的边会由接收方记录，因此接收方所在的 Silo 会将接收方加入迁移候选集合（迁移到此处）。
        // 可以确定接收方是可迁移 Grain，因为网关禁止两端顶点均不可迁移的边。
        foreach (var grainEdges in migrationCandidates)
        {
            var accLocalScore = 0L;
            var accRemoteScore = 0L;

            foreach (var edge in grainEdges)
            {
                if (edge.Direction is Direction.LocalToLocal)
                {
                    // 由于它是 L2L，配对 Silo 即当前 Silo，因此无需按配对 Silo 筛选。
                    accLocalScore += edge.Weight;
                }
                else if (edge.PartnerSilo.Equals(otherSilo))
                {
                    Debug.Assert(edge.Direction is Direction.RemoteToLocal or Direction.LocalToRemote);

                    // 此处需要按 otherSilo 筛选，因为任一 L2R 或 R2L 边都可能连接当前顶点与不在 otherSilo 中的顶点。
                    accRemoteScore += edge.Weight;
                }
            }

            if (accLocalScore >= accRemoteScore)
            {
                // 跳过本地调用多于远程调用的顶点。
                continue;
            }

            var totalAccScore = accRemoteScore - accLocalScore;
            var connVertices = ImmutableArray.CreateBuilder<CandidateConnectedVertex>();
            foreach (var edge in grainEdges)
            {
                // 注意，相连顶点可能属于不可迁移类型，保留它们十分重要，
                // 因为它们也会影响当前候选顶点的迁移成本，尤其当它们与候选顶点位于同一 Silo 时，
                // 当前候选顶点迁移后，这些调用可能转变为远程调用。
                // 此处 Weight 表示单条边的权重，而不是前面的累计权重。
                connVertices.Add(new CandidateConnectedVertex(edge.TargetId, edge.Weight));
            }

            CandidateVertex candidate = new()
            {
                Id = grainEdges.Key,
                AccumulatedTransferScore = totalAccScore,
                ConnectedVertices = connVertices.ToImmutable()
            };

            result.Add(candidate);
        }

        return result;
    }

    private static HashSet<GrainId> ComputeAnchoredGrains(List<IGrouping<GrainId, VertexEdge>> migrationCandidates)
    {
        HashSet<GrainId> anchoredGrains = [];
        foreach (var grainEdges in migrationCandidates)
        {
            var accLocalScore = 0L;
            var accRemoteScore = 0L;

            foreach (var edge in grainEdges)
            {
                if (edge.Direction is Direction.LocalToLocal)
                {
                    accLocalScore += edge.Weight;
                }
                else
                {
                    Debug.Assert(edge.Direction is Direction.RemoteToLocal or Direction.LocalToRemote);
                    accRemoteScore += edge.Weight;
                }
            }

            if (accLocalScore > accRemoteScore)
            {
                anchoredGrains.Add(grainEdges.Key);
            }
        }

        return anchoredGrains;
    }

    private List<IGrouping<GrainId, VertexEdge>> GetMigrationCandidates() => CreateLocalVertexEdges().Where(x => x.IsMigratable).GroupBy(x => x.SourceId).ToList();

    /// <summary>
    /// 创建本地顶点边的集合。多个条目可以具有相同的 Id。
    /// </summary>
    /// <remarks><see cref="VertexEdge.SourceId"/> 保证属于位于当前 Silo 的 Grain，而 <see cref="VertexEdge.TargetId"/> 可能属于本地或远程 Silo。</remarks>
    private IEnumerable<VertexEdge> CreateLocalVertexEdges()
    {
        foreach (var (edge, count, error) in _edgeWeights.Elements)
        {
            if (count == 0)
            {
                continue;
            }

            var vertexEdge = CreateVertexEdge(edge, count);
            if (vertexEdge.Direction is Direction.Unspecified)
            {
                // 消息经由当前 Silo 重新路由时可能出现此情况。
                continue;
            }

            yield return vertexEdge;

            if (vertexEdge.Direction == Direction.LocalToLocal)
            {
                // 之所以反转方向，是因为对于本地到本地的边，两个 Grain 通过一条通信边相连。
                // 一旦存在边，表示两个 Grain 在一段时间内存在联系，因此迁移其中任意一个都可能产生代价。
                // 由于候选集合的构建也考虑本地连接（本地连接会增加迁移成本），因此需要
                // 同时从源顶点和目标顶点的角度考虑此边，因为目标顶点也会参与候选集合的构建。
                var flippedEdge = CreateVertexEdge(edge.Flip(), count);
                yield return flippedEdge;
            }
        }

        VertexEdge CreateVertexEdge(in Edge edge, long weight)
        {
            return (IsSourceLocal(edge), IsTargetLocal(edge)) switch
            {
                (true, true) => new(
                   SourceId: edge.Source.Id,                // 本地顶点是通信源。
                   TargetId: edge.Target.Id,
                   IsMigratable: edge.Source.IsMigratable,
                   PartnerSilo: Silo,                       // 对端位于本地（注意：this.Silo = Source.Silo = Target.Silo）。
                   Direction: Direction.LocalToLocal,
                   Weight: weight),
                (true, false) => new(
                    SourceId: edge.Source.Id,               // 本地顶点是通信源。
                    TargetId: edge.Target.Id,
                    IsMigratable: edge.Source.IsMigratable,
                    PartnerSilo: edge.Target.Silo,          // 对端位于远程 Silo。
                    Direction: Direction.LocalToRemote,
                    Weight: weight),
                (false, true) => new(
                    SourceId: edge.Target.Id,               // 本地顶点是通信目标。
                    TargetId: edge.Source.Id,
                    IsMigratable: edge.Target.IsMigratable,
                    PartnerSilo: edge.Source.Silo,          // 对端位于远程 Silo。
                    Direction: Direction.RemoteToLocal,
                    Weight: weight),
                _ => default
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsSourceLocal(in Edge edge) => edge.Source.Silo.Equals(Silo);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsTargetLocal(in Edge edge) => edge.Target.Silo.Equals(Silo);
    }

    public void Participate(ISiloLifecycle observer)
    {
        // 当 Silo 进入活动状态时启动。
        observer.Subscribe(
            nameof(ActivationRepartitioner),
            ServiceLifecycleStage.Active,
            OnActiveStart,
            ct => Task.CompletedTask);

        // 当 Silo 停止应用程序服务时停止。
        observer.Subscribe(
            nameof(ActivationRepartitioner),
            ServiceLifecycleStage.ApplicationServices,
            ct => Task.CompletedTask,
            StopProcessingEdgesAsync);
    }

    void IDisposable.Dispose()
    {
        base.Dispose();
        _enableMessageSampling = false;
        _siloStatusOracle.UnSubscribeFromSiloStatusEvents(this);
        _shutdownCts.Cancel();
    }

    void ISiloStatusListener.SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status)
    {
        _enableMessageSampling = _siloStatusOracle.GetActiveSilos().Length > 1;
    }

    public ValueTask<ImmutableArray<(Edge, ulong)>> GetGrainCallFrequencies(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ImmutableArray.CreateBuilder<(Edge, ulong)>(_edgeWeights.Count);
        foreach (var (edge, count, _) in _edgeWeights.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add((edge, count));
        }

        return new(result.ToImmutable());
    }

    private enum Direction : byte
    {
        Unspecified,
        LocalToLocal,
        LocalToRemote,
        RemoteToLocal
    }

    /// <summary>
    /// 表示两个顶点之间的连接。
    /// </summary>
    /// <param name="SourceId">此顶点表示的 Grain 的标识。</param>
    /// <param name="TargetId">相连顶点（通信对端）的标识。</param>
    /// <param name="IsMigratable">指定具有 <paramref name="SourceId"/> 的顶点是否属于可迁移类型。</param>
    /// <param name="PartnerSilo">与具有 <paramref name="SourceId"/> 的顶点所在 Silo 发生交互的对端 Silo。</param>
    /// <param name="Direction">边的方向。</param>
    /// <param name="Weight"><paramref name="SourceId"/> 与 <paramref name="TargetId"/> 之间交换的估计消息数。</param>
    private readonly record struct VertexEdge(GrainId SourceId, GrainId TargetId, bool IsMigratable, SiloAddress PartnerSilo, Direction Direction, long Weight);
}
