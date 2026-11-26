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

// 见: https://www.microsoft.com/en-us/research/wp-content/uploads/2016/06/eurosys16loca_camera_ready-1.pdf
internal sealed partial class ActivationRepartitioner : SystemTarget, IActivationRepartitionerSystemTarget, ILifecycleParticipant<ISiloLifecycle>, IDisposable, ISiloStatusListener
{
    internal sealed class DeactivatedGrainQueue : IActivationWorkingSetObserver
    {
        // 交换可以被跳过很长一段时间, 所以保持这个最努力的队列的条纹 。
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
            // 调度第一个定时器的勾选 。
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

        // 下个计时器计时器计数 。
        UpdateTimer();

        if (_currentExchangeSilo is not null)
        {
            // 如果我们已经与另一个 silo交换, 则跳过此回合 。
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
                // 设置交易期间的交换伙伴。
                // 这样做可以防止其他请求相互交错。
                _currentExchangeSilo = candidateSilo;

                LogBeginningProtocol(Silo, candidateSilo);
                var remoteRef = IActivationRepartitionerSystemTarget.GetReference(_grainFactory, candidateSilo);
                var response = await remoteRef.AcceptExchangeRequest(
                    new(Silo, [.. offeredGrains], GetLocalActivationCount()),
                    CancellationToken.None);

                switch (response.Type)
                {
                    case AcceptExchangeResponse.ResponseType.Success:
                        // 交流成功,不需要重复其他候选人。
                        await FinalizeProtocol(response.AcceptedGrainIds, response.GivenGrainIds, candidateSilo, anchoredSet);
                        return;
                    case AcceptExchangeResponse.ResponseType.ExchangedRecently:
                        // 远程 silo 最近参与了另一个交流,尝试下一个最佳候选人。
                        LogExchangedRecentlyResponse(Silo, candidateSilo);
                        continue;
                    case AcceptExchangeResponse.ResponseType.MutualExchangeAttempt:
                        // 远程 silo 已经与此 silo 交换, 远程 silo 开始的交换
                        // 优先于此 silo 启动的优先。
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
                continue; // 出了点问题,试下一位最佳候选人
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
            // 拒绝请求,因为我们已在与发件人交换 silo。
            // The '<=' comparison here is used to break the tie in case both silos 彼此交换。

            // 我们随机选择“ min” 和“ max” 之间的时间, 而不是从中减去“ min ” 。 这样我们就可以这样做了, 这样这个 silo 不必等待“ min+ object ” ,
            // 因为它第一次开始。 它保证“ 随机性- 分钟” = @ 零; “ 随机性” 至少等于“ 分钟 ” 。
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

        // 设定请求期间的交换 silo 。
        // 这样做可以防止其他请求相互交错。
        _currentExchangeSilo = request.SendingSilo;

        try
        {
            var acceptedGrains = ImmutableArray.CreateBuilder<GrainId>();
            var givingGrains = ImmutableArray.CreateBuilder<GrainId>();
            var remoteSet = request.ExchangeSet;
            var migrationCandidates = GetMigrationCandidates();
            var localSet = GetCandidatesForSilo(migrationCandidates, request.SendingSilo);
            var anchoredSet = ComputeAnchoredGrains(migrationCandidates);

            // 我们需要确定两个子集:
            // - 发自发送 silo (request.ExchangeSet并且被接受(部分)从此 silo.
            // - 源自于此 silo (candidateSet并且被派遣去接受, silo.
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
                    // 给其他任务一个定期执行的机会。
                    yieldStopwatch.Restart();
                    await Task.Delay(1, cancellationToken);
                }

                // 如果通过给予获得更多 grains 远程 silo 而不是取而代之, 我们将尝试先给。
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

                // 不能再进行移民,因此已计算出候选人的总数。
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
                            // 添加传输分数, 因为这两个矢量现在彼此距离遥远。
                            connectedVertex.AccumulatedTransferScore += transferScore;
                            localHeap.OnIncreaseElementPriority(connectedVertex);
                            break;
                        case VertexLocation.Remote:
                            // 减去转移得分,因为这两个矢量现在将彼此为本地矢量。
                            connectedVertex.AccumulatedTransferScore -= transferScore;
                            remoteHeap.OnDecreaseElementPriority(connectedVertex);
                            break;
                    }
                }

                // 假设矢量是遥远的 我们今后会执行任何行动
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
                            // 减去转移得分,因为这两个矢量现在将彼此为本地矢量。
                            connectedVertex.AccumulatedTransferScore -= transferScore;
                            localHeap.OnDecreaseElementPriority(connectedVertex);
                            break;
                        case VertexLocation.Remote:
                            // 添加传输分数, 因为这两个矢量现在彼此距离遥远。
                            connectedVertex.AccumulatedTransferScore += transferScore;
                            remoteHeap.OnIncreaseElementPriority(connectedVertex);
                            break;
                    }
                }

                // 我们今后将执行任何行动 假设矢量是本地的
                chosenVertex.Location = VertexLocation.Local;

                return true;
            }

            bool TryMigrateCore(MaxHeap<CandidateVertexHeapElement> sourceHeap, int localDelta, int remoteDelta, [NotNullWhen(true)] out CandidateVertexHeapElement? chosenVertex)
            {
                var anticipatedImbalance = CalculateImbalance(localActivations + localDelta, remoteActivations + remoteDelta);
                if (anticipatedImbalance >= imbalance && !_toleranceRule.IsSatisfiedBy((uint)anticipatedImbalance))
                {
                    // 从这一堆中取出这种气体不会改善不平衡。
                    chosenVertex = null;
                    return false;
                }

                if (!sourceHeap.TryPop(out chosenVertex))
                {
                    // 堆肥是空的。
                    return false;
                }

                if (chosenVertex.AccumulatedTransferScore <= 0)
                {
                    // 如果它受到前一次跑跑的影响 得分是零或负的 只需弹出就忽略了
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
                // 此顶点已经是本地集的一部分, 所以假设顶点是本地的, 忽略远程顶点 。
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
                    // 连接的顶点不是任何移民候选人的一部分, 所以我们会忽略它。
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
    /// <item>开始将 <paramref name="giving"/> 从当地 silo 迁移到 <paramref name="targetSilo"/>。</item>
    /// <item>更新受影响柜台的最新情况,以反映 grains 交换 silos.</item>
    /// </list>
    /// </summary>
    /// <param name="giving">下 grain 当地移民的idids silo 至 <paramref name="targetSilo"/>.</param>
    /// <param name="accepting">下 grain 移民的idid 人 <paramref name="targetSilo"/> 当地 silo.</param>
    /// <param name="targetSilo">下 silo 参与与当地人交流 silo.</param>
    /// <param name="newlyAnchoredGrains">当地 grain 其通信模式倾向于留在当地 silo.</param>
    /// <returns>代表交易所最终确定业务的任务。</returns>
    internal async Task FinalizeProtocol(ImmutableArray<GrainId> giving, ImmutableArray<GrainId> accepting, SiloAddress targetSilo, HashSet<GrainId> newlyAnchoredGrains)
    {
        // 协议的结论是,“这” silo 应该采取“设置”,所以我们相应地向主任暗示。
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
            // 很少会发生这种情况,但目前我们无法做很多事情,因为它超出了我们的控制范围。
            // 即使有些失败,算法最终也会再次运行 所以激活将有更多机会迁移
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

        // 标注此 silos 交换可能提出的下一对交换请求。
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
                    // 给其他任务一个定期执行的机会。
                    yieldStopwatch.Restart();
                    await Task.Delay(1);
                }

                // 完全删除此计数器, 因为一个或两个顶点都迁移了。 不这样做的话, 它会扭曲下一个协议周期的结果 。
                // 我们只删除受影响的计数器, 因为可能还有其他的计数器“ 此” silo 与另一个 silo (不属于本交换周期的一部分) 有关联。
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
                // 我们不会和自己交换任何东西 所以别提这个了 silo.
                continue;
            }

            var candidatesForRemote = GetCandidatesForSilo(migrationCandidates, siloAddress);
            var totalAccTransferScore = candidatesForRemote.Sum(x => x.AccumulatedTransferScore);

            candidateSets.Add(new(siloAddress, [.. candidatesForRemote], totalAccTransferScore));
        }

        // 以最高累计转移分数命令他们
        candidateSets.Sort(static (a, b) => -a.TransferScore.CompareTo(b.TransferScore));

        return candidateSets;
    }

    private List<CandidateVertex> GetCandidatesForSilo(List<IGrouping<GrainId, VertexEdge>> migrationCandidates, SiloAddress otherSilo)
    {
        Debug.Assert(!otherSilo.Equals(Silo));

        List<CandidateVertex> result = [];

        // 我们跳过无法迁移的类型。 相反, 同样的边缘将从接收者那里记录下来, 所以其主机 silo 会添加为要迁移的候选方( 在这里) 。
        // 我们确信接收者是偏差 grain, 因为网关禁止两边有非移动性脊椎的边缘。
        foreach (var grainEdges in migrationCandidates)
        {
            var accLocalScore = 0L;
            var accRemoteScore = 0L;

            foreach (var edge in grainEdges)
            {
                if (edge.Direction is Direction.LocalToLocal)
                {
                    // 因为它的L2L,就是说合伙人 silo 将会是 '此' silo,所以我们不需要被伙伴过滤 silo.
                    accLocalScore += edge.Weight;
                }
                else if (edge.PartnerSilo.Equals(otherSilo))
                {
                    Debug.Assert(edge.Direction is Direction.RemoteToLocal or Direction.LocalToRemote);

                    // 我们需要在这里过滤 by 'otherSilo”因为任何 L2R 或 R2L 边缘都可以在当前顶点和 a 中的一个顶点之间 silo 中不是 'otherSilo'.
                    accRemoteScore += edge.Weight;
                }
            }

            if (accLocalScore >= accRemoteScore)
            {
                // 我们省略了当地电话胜过远程电话的顶点。
                continue;
            }

            var totalAccScore = accRemoteScore - accLocalScore;
            var connVertices = ImmutableArray.CreateBuilder<CandidateConnectedVertex>();
            foreach (var edge in grainEdges)
            {
                // 请注意连接的顶端可以是非偏差型,必须保持它们,
                // 因为它们也影响到当前候选人顶顶端的迁移成本,特别是如果这些顶点是当地向候选人迁移的话。
                // 因为当候选人迁移之后,这些电话可能会被转换为远程通话。
                // 体重代表一个边缘的重量, 不是上面那样累积的。
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
    /// 创建“ 本地” 顶端集合。 多条目可以拥有相同的日期 。
    /// </summary>
    /// <remarks>下 <see cref="VertexEdge.SourceId"/> 保证属于a grain 此处的点 silo,而 <see cref="VertexEdge.TargetId"/> 属于本地或远程 silo.</remarks>
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
                // 可以通过此 silo将消息改道时,才会发生这种情况。
                continue;
            }

            yield return vertexEdge;

            if (vertexEdge.Direction == Direction.LocalToLocal)
            {
                // 我们之所以这样做,是因为当边缘是本地对本地时,我们有两个通过通信边缘连接的 grains 。
                // 一旦存在边缘,它意味着2 grains 与时间挂钩,这意味着其中任一边缘都有可能移动。
                // 由于候选人的搭建也考虑到当地连接(这增加了移徙成本),我们需要
                // 不仅从消息来源的角度考虑优势,而且考虑到目标的边缘,因为它也将参与候选人集。
                var flippedEdge = CreateVertexEdge(edge.Flip(), count);
                yield return flippedEdge;
            }
        }

        VertexEdge CreateVertexEdge(in Edge edge, long weight)
        {
            return (IsSourceLocal(edge), IsTargetLocal(edge)) switch
            {
                (true, true) => new(
                   SourceId: edge.Source.Id,                // “ 本地” 顶部是通信的“ 源头 ”
                   TargetId: edge.Target.Id,
                   IsMigratable: edge.Source.IsMigratable,
                   PartnerSilo: Silo,                       // 伙伴是“当地”(注: this.Silo = Source.Silo = Target.Silo)
                   Direction: Direction.LocalToLocal,
                   Weight: weight),
                (true, false) => new(
                    SourceId: edge.Source.Id,               // “ 本地” 顶部是通信的“ 源头 ”
                    TargetId: edge.Target.Id,
                    IsMigratable: edge.Source.IsMigratable,
                    PartnerSilo: edge.Target.Silo,          // 合伙人是"远程"
                    Direction: Direction.LocalToRemote,
                    Weight: weight),
                (false, true) => new(
                    SourceId: edge.Target.Id,               // “ 本地” 顶部是通信的“ 目标” 。
                    TargetId: edge.Source.Id,
                    IsMigratable: edge.Target.IsMigratable,
                    PartnerSilo: edge.Source.Silo,          // 合伙人是"远程"
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
        // 启动时, silo 将激活。
        observer.Subscribe(
            nameof(ActivationRepartitioner),
            ServiceLifecycleStage.Active,
            OnActiveStart,
            ct => Task.CompletedTask);

        // 停止应用服务时停止 silo 。
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
    /// <param name="SourceId">它所代表的 grain 的代号。</param>
    /// <param name="TargetId">连接的顶点(通信与之一起发生的顶点)的代号。</param>
    /// <param name="IsMigratable">指定 <paramref name="SourceId"/> 的顶部是偏差型号。</param>
    /// <param name="PartnerSilo">下 silo 与《公约》和 silo 顶面 <paramref name="SourceId"/>.</param>
    /// <param name="Direction">边缘方向</param>
    /// <param name="Weight">估计的 <paramref name="SourceId"/> 和 <paramref name="TargetId"/>.</param>
    private readonly record struct VertexEdge(GrainId SourceId, GrainId TargetId, bool IsMigratable, SiloAddress PartnerSilo, Direction Direction, long Weight);
}
