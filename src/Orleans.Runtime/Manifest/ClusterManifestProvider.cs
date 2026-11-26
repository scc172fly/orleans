using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Core.Diagnostics;
using Orleans.Internal;
using Orleans.Metadata;
using Orleans.Runtime.Utilities;

namespace Orleans.Runtime.Metadata
{
    internal partial class ClusterManifestProvider : IClusterManifestProvider, IAsyncDisposable, IDisposable, ILifecycleParticipant<ISiloLifecycle>
    {
        private const int MaxConcurrentPeerManifestProbes = 3;
        private static readonly TimeSpan PeerManifestProbeTimeout = TimeSpan.FromSeconds(1);
        private readonly SiloAddress _localSiloAddress;
        private readonly ILogger<ClusterManifestProvider> _logger;
        private readonly IServiceProvider _services;
        private readonly IClusterMembershipService _clusterMembershipService;
        private readonly IFatalErrorHandler _fatalErrorHandler;
        private readonly TimeProvider _timeProvider;
        private readonly ClusterManifestInstruments _instruments;
        private readonly bool _enableContentAddressedRetrieval;
        private readonly SemaphoreSlim? _peerProbeSlots;
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly AsyncEnumerable<ClusterManifest> _updates;
        private readonly Lock _currentLock = new();
        private ClusterManifest _current;
        private IInternalGrainFactory? _grainFactory;
        private Task? _runTask;
        private int _peerProbeRound;
        private ConcurrentDictionary<ManifestHash, GrainManifest>? _manifestCache;
        private ManifestUpdateAttemptDiagnostics? _lastAttemptDiagnostics;
        private long _attemptSequence;

        public ClusterManifestProvider(
            ILocalSiloDetails localSiloDetails,
            SiloManifestProvider siloManifestProvider,
            IClusterMembershipService clusterMembershipService,
            IFatalErrorHandler fatalErrorHandler,
            ILogger<ClusterManifestProvider> logger,
            IServiceProvider services,
            TimeProvider timeProvider,
            IOptions<ClusterManifestOptions> options,
            ClusterManifestInstruments instruments)
        {
            _localSiloAddress = localSiloDetails.SiloAddress;
            _logger = logger;
            _services = services;
            _clusterMembershipService = clusterMembershipService;
            _fatalErrorHandler = fatalErrorHandler;
            _timeProvider = timeProvider;
            _instruments = instruments;
            _enableContentAddressedRetrieval = options.Value.EnableContentAddressedRetrieval;
            LocalGrainManifest = siloManifestProvider.SiloManifest;
            if (_enableContentAddressedRetrieval)
            {
                _peerProbeSlots = new(MaxConcurrentPeerManifestProbes, MaxConcurrentPeerManifestProbes);
            }

            _current = CreateClusterManifest(
                MajorMinorVersion.MinValue,
                ImmutableDictionary<SiloAddress, GrainManifest>.Empty);
            _updates = new AsyncEnumerable<ClusterManifest>(
                initialValue: _current,
                updateValidator: (previous, proposed) => proposed.Version > previous.Version,
                onPublished: OnManifestPublished);
        }

        public ClusterManifest Current => EnsureValidManifestForCurrentMembership(_clusterMembershipService.CurrentSnapshot);

        public IAsyncEnumerable<ClusterManifest> Updates => _updates;

        public GrainManifest LocalGrainManifest { get; }

        /// <summary>
        /// 简略描述该供应商最近最新数据更新的尝试,包括
        /// 正在对成员版本进行校正, silos 仍在等待一份清单和最新检索
        /// 曾经诊断过停滞的趋同,等待而不重复。
        /// </summary>
        internal ManifestUpdateAttemptDiagnostics? LastAttemptDiagnostics => Volatile.Read(ref _lastAttemptDiagnostics);

        /// <summary>
        /// 获取在阅读期间共存的已公布的清单和最新尝试诊断。
        /// 尝试的入会版的企图成员名单可以确定正在进行的和解;显示可
        /// 反映新的成员数目,在下次尝试开始前同步到 <see cref="Current"/> 。
        /// </summary>
        internal (ClusterManifest Manifest, ManifestUpdateAttemptDiagnostics? Diagnostics) GetManifestAndDiagnosticsSnapshot()
        {
            ManifestUpdateAttemptDiagnostics? diagnostics;
            ClusterManifest manifest;
            do
            {
                diagnostics = LastAttemptDiagnostics;
                manifest = Current;
            }
            while (!ReferenceEquals(diagnostics, LastAttemptDiagnostics));

            return (manifest, diagnostics);
        }

        /// <summary>
        /// 记录其原始尝试的抓取完成记录。 比较和擦拭同时保存
        /// 尝试身份使得晚进与新尝试隔离
        /// </summary>
        /// <param name="attemptId">开始获取此抓取的尝试的身份。</param>
        /// <param name="siloAddress">下 silo 其取回已不再未了。</param>
        /// <param name="exception">如果抓取失败, 失败。 <see langword="null"/> 成功失败
        /// (包括由同行修理取代的) 或根据取回的标记要求取消 。</param>
        private void MarkManifestFetchComplete(long attemptId, SiloAddress siloAddress, Exception? exception)
        {
            ManifestUpdateAttemptDiagnostics? original;
            ManifestUpdateAttemptDiagnostics updated;
            do
            {
                original = Volatile.Read(ref _lastAttemptDiagnostics);
                if (original is null || original.AttemptId != attemptId)
                {
                    return;
                }

                var pendingSilos = original.PendingSilos.Remove(siloAddress);
                updated = exception is null
                    ? original with { PendingSilos = pendingSilos }
                    : original with
                    {
                        PendingSilos = pendingSilos,
                        LastFailedSilo = siloAddress,
                        LastFailureMessage = exception.Message,
                        LastFailureAt = _timeProvider.GetUtcNow().UtcDateTime
                    };
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _lastAttemptDiagnostics, updated, original), original));
        }

        private ClusterManifest EnsureValidManifestForCurrentMembership(ClusterMembershipSnapshot clusterMembership)
        {
            var current = _current;
            var membershipVersion = clusterMembership.Version.Value;
            if (current.Version.Major >= membershipVersion)
            {
                return current;
            }

            lock (_currentLock)
            {
                current = _current;
                if (current.Version.Major >= membershipVersion)
                {
                    return current;
                }

                var synchronizedSilos = RemoveNonActiveSilos(current.Silos, clusterMembership);
                if (clusterMembership.GetSiloStatus(_localSiloAddress) == SiloStatus.Active
                    && !synchronizedSilos.ContainsKey(_localSiloAddress))
                {
                    synchronizedSilos = synchronizedSilos.Add(_localSiloAddress, LocalGrainManifest);
                }

                var version = new MajorMinorVersion(membershipVersion, 0);
                var updated = CreateClusterManifest(version, synchronizedSilos);
                TryPublishManifest(updated);
                return _current;
            }
        }

        private async Task ProcessMembershipUpdates(CancellationToken cancellationToken)
        {
            try
            {
                LogDebugStartingToProcessMembershipUpdates();

                await using var membershipUpdates = _clusterMembershipService.MembershipUpdates.GetAsyncEnumerator(cancellationToken);
                var nextUpdateTask = membershipUpdates.MoveNextAsync().AsTask();
                ClusterMembershipSnapshot? membershipSnapshot = null;

                while (true)
                {
                    if (membershipSnapshot is null)
                    {
                        if (!await nextUpdateTask)
                        {
                            return;
                        }

                        membershipSnapshot = membershipUpdates.Current;
                        nextUpdateTask = membershipUpdates.MoveNextAsync().AsTask();
                    }

                    if (await UpdateManifest(membershipSnapshot, cancellationToken))
                    {
                        membershipSnapshot = null;
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var retryDelayTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    if (await Task.WhenAny(nextUpdateTask, retryDelayTask) == nextUpdateTask)
                    {
                        if (!await nextUpdateTask)
                        {
                            return;
                        }

                        membershipSnapshot = membershipUpdates.Current;
                        nextUpdateTask = membershipUpdates.MoveNextAsync().AsTask();
                    }
                    else
                    {
                        await retryDelayTask;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 关闭时忽略 。
            }
            catch (Exception exception) when (_fatalErrorHandler.IsUnexpected(exception))
            {
                _fatalErrorHandler.OnFatalException(this, nameof(ProcessMembershipUpdates), exception);
            }
            finally
            {
                LogDebugStoppedProcessingMembershipUpdates();
            }
        }

        private async Task<bool> UpdateManifest(ClusterMembershipSnapshot clusterMembership, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 更新后被新出版物取代了 不断充斥着它原来的缓存
            var cache = Volatile.Read(ref _manifestCache);
            var existingManifest = EnsureValidManifestForCurrentMembership(clusterMembership);
            if (existingManifest.Version.Major > clusterMembership.Version.Value)
            {
                return true;
            }

            var builder = existingManifest.Silos.ToBuilder();
            var modified = false;

            // 填充缺失的条目
            var missingSilos = new List<SiloAddress>();
            foreach (var entry in clusterMembership.Members)
            {
                var member = entry.Value;
                if (member.Status != SiloStatus.Active)
                {
                    // 如果成员尚未积极开展活动,则可能无法处理请求。
                    continue;
                }

                var siloAddress = member.SiloAddress;
                if (builder.ContainsKey(siloAddress))
                {
                    // 已为该组成员检索到声明。
                    continue;
                }

                missingSilos.Add(siloAddress);
            }

            var attemptId = Interlocked.Increment(ref _attemptSequence);
            var pendingSilos = missingSilos.ToImmutableHashSet();
            ManifestUpdateAttemptDiagnostics? previous;
            ManifestUpdateAttemptDiagnostics attempt;
            do
            {
                previous = Volatile.Read(ref _lastAttemptDiagnostics);
                attempt = new ManifestUpdateAttemptDiagnostics(
                    attemptId,
                    clusterMembership.Version,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    pendingSilos,
                    previous?.LastFailedSilo,
                    previous?.LastFailureMessage,
                    previous?.LastFailureAt);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _lastAttemptDiagnostics, attempt, previous), previous));

            using var peerRepairCancellation = cache is not null && missingSilos.Count > 1
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            var peerRepairTask = peerRepairCancellation is not null
                ? TryFillMissingManifestsFromPeers(clusterMembership, builder, missingSilos, cache!, peerRepairCancellation.Token)
                : Task.FromResult(false);

            var tasks = new Dictionary<SiloAddress, Task<(SiloAddress Key, GrainManifest? Value, Exception? Exception)>>();
            foreach (var siloAddress in missingSilos)
            {
                tasks.Add(siloAddress, GetManifest(siloAddress, cancellationToken));
            }

            var directFetches = Task.WhenAll(tasks.Values);
            var peerRepaired = false;
            if (!peerRepairTask.IsCompleted
                && await Task.WhenAny(peerRepairTask, directFetches) == directFetches
                && (await directFetches).All(static result => result.Value is not null && result.Exception is null))
            {
                peerRepairCancellation!.Cancel();
                try
                {
                    peerRepaired = await peerRepairTask;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // 直接检索提供所有缺失的物品
                }
            }
            else
            {
                peerRepaired = await peerRepairTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (peerRepaired)
            {
                modified = true;
                var repairedSilos = builder.ToImmutable();
                var repairedManifest = CreateClusterManifest(
                    new MajorMinorVersion(clusterMembership.Version.Value, existingManifest.Version.Minor + 1),
                    repairedSilos);
                if (!TryPublishManifest(repairedManifest))
                {
                    return false;
                }

                _instruments.OnPeerRepair(missingSilos.Count(builder.ContainsKey));
                existingManifest = repairedManifest;
                modified = false;
                foreach (var siloAddress in missingSilos)
                {
                    if (builder.ContainsKey(siloAddress))
                    {
                        tasks.Remove(siloAddress);
                        MarkManifestFetchComplete(attemptId, siloAddress, exception: null);
                    }
                }

                // 每个取回都观察自己的失败, 所以多余的请求可以独立完成 。
                if (tasks.Count == 0)
                {
                    return true;
                }
            }

            async Task<(SiloAddress Key, GrainManifest? Value, Exception? Exception)> GetManifest(
                SiloAddress siloAddress,
                CancellationToken requestCancellationToken)
            {
                try
                {
                    var manifest = await GetSiloManifest(siloAddress, cache, requestCancellationToken);
                    MarkManifestFetchComplete(attemptId, siloAddress, exception: null);
                    return (siloAddress, manifest, null);
                }
                catch (OperationCanceledException exception) when (requestCancellationToken.IsCancellationRequested)
                {
                    MarkManifestFetchComplete(attemptId, siloAddress, exception: null);
                    return (siloAddress, null, exception);
                }
                catch (Exception exception)
                {
                    MarkManifestFetchComplete(attemptId, siloAddress, exception);
                    return (siloAddress, null, exception);
                }
            }

            var fetchSuccess = true;
            await Task.WhenAll(tasks.Values);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var task in tasks.Values)
            {
                var result = await task;
                if (result.Exception is Exception exception)
                {
                    fetchSuccess = false;
                    LogWarningErrorRetrievingSiloManifest(exception, result.Key);
                }
                else
                {
                    if (result.Value is not null)
                    {
                        modified = true;
                        builder[result.Key] = result.Value;
                    }
                    else
                    {
                        fetchSuccess = false;
                    }
                }
            }

            // 无论成功与否,如果经过修改,更新清单。
            var version = new MajorMinorVersion(clusterMembership.Version.Value, existingManifest.Version.Minor + 1);
            if (modified)
            {
                var silos = builder.ToImmutable();
                var manifest = CreateClusterManifest(version, silos);
                var publishSuccess = TryPublishManifest(manifest);
                return publishSuccess && fetchSuccess;
            }
            return fetchSuccess;
        }

        private void OnManifestPublished(ClusterManifest manifest)
        {
            if (!_enableContentAddressedRetrieval)
            {
                Interlocked.Exchange(ref _current, manifest);
                return;
            }

            // 每个出版物都用其现场清单播种一个新的读通缓存。
            // 保存了在他们最初时代的孤立状态
            var cache = new ConcurrentDictionary<ManifestHash, GrainManifest>();
            foreach (var grainManifest in manifest.AllGrainManifests)
            {
                cache[ManifestHashCalculator.ComputeHash(grainManifest)] = grainManifest;
            }

            cache[ManifestHashCalculator.ComputeHash(LocalGrainManifest)] = LocalGrainManifest;
            // 读者在检查清单版本之前先捕捉缓存, 所以首先曝光版本 。
            Interlocked.Exchange(ref _current, manifest);
            Interlocked.Exchange(ref _manifestCache, cache);
        }

        private async Task<bool> TryFillMissingManifestsFromPeers(
            ClusterMembershipSnapshot clusterMembership,
            ImmutableDictionary<SiloAddress, GrainManifest>.Builder builder,
            List<SiloAddress> missingSilos,
            ConcurrentDictionary<ManifestHash, GrainManifest> cache,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var missing = new HashSet<SiloAddress>(missingSilos);
            var modified = false;
            var peers = clusterMembership.Members.Values
                .Where(static member => member.Status == SiloStatus.Active)
                .Select(static member => member.SiloAddress)
                .Where(peer => !peer.Equals(_localSiloAddress))
                .OrderBy(static silo => silo)
                .ToArray();
            if (peers.Length == 0)
            {
                return false;
            }

            var round = Interlocked.Increment(ref _peerProbeRound);
            var start = (int)((uint)(_localSiloAddress.GetConsistentHashCode() + round) % (uint)peers.Length);
            var probeCount = Math.Min(MaxConcurrentPeerManifestProbes, peers.Length);
            var probes = new Task<PeerManifestProbeResult?>[probeCount];
            for (var i = 0; i < probeCount; i++)
            {
                probes[i] = ProbePeerForManifests(peers[(start + i) % peers.Length], missingSilos, cache, cancellationToken);
            }

            var results = await Task.WhenAll(probes);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var result in results)
            {
                if (result is null || missing.Count == 0)
                {
                    continue;
                }

                FillFromCachedHashes(result.Summary, missing, builder, cache, ref modified);
                if (result.Update?.SiloManifests is { } manifests)
                {
                    foreach (var silo in missing.ToArray())
                    {
                        if (!result.Summary.SiloManifestHashes.TryGetValue(silo, out var expectedHash)
                            || !manifests.TryGetValue(silo, out var manifest)
                            || ManifestHashCalculator.ComputeHash(manifest) != expectedHash)
                        {
                            continue;
                        }

                        cache[expectedHash] = manifest;
                        builder[silo] = manifest;
                        missing.Remove(silo);
                        modified = true;
                    }
                }
            }

            return modified;
        }

        private async Task<PeerManifestProbeResult?> ProbePeerForManifests(
            SiloAddress peer,
            IReadOnlyCollection<SiloAddress> missingSilos,
            ConcurrentDictionary<ManifestHash, GrainManifest> cache,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slots = _peerProbeSlots!;
            if (!slots.Wait(0, cancellationToken))
            {
                _instruments.OnPeerProbe("skipped");
                LogDebugClusterManifestPeerProbeAtCapacity(peer);
                return null;
            }

            using var timeoutCancellation = new CancellationTokenSource(PeerManifestProbeTimeout, _timeProvider);
            using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
            var probeToken = probeCancellation.Token;
            var status = "error";
            Task? probeTask = null;
            try
            {
                var remoteManifestProvider = _grainFactory!.GetSystemTarget<IClusterManifestSystemTarget>(Constants.ManifestProviderType, peer);
                var summaryTask = remoteManifestProvider.GetClusterManifestHashSummary(probeToken).AsTask();
                probeTask = summaryTask;
                var summary = await summaryTask
                    .WaitAsync(probeToken);
                probeTask = null;
                probeToken.ThrowIfCancellationRequested();
                if (missingSilos.All(silo =>
                    summary.SiloManifestHashes.TryGetValue(silo, out var hash)
                    && cache.ContainsKey(hash)))
                {
                    status = "success";
                    return new(summary, Update: null);
                }

                probeToken.ThrowIfCancellationRequested();

                // 没有保留每个同比的表列体,所以要求完整更新,而不是综合
                // 当地供应商版本的基准。
                var updateTask = remoteManifestProvider.GetClusterManifestUpdate(MajorMinorVersion.MinValue, probeToken).AsTask();
                probeTask = updateTask;
                var update = await updateTask
                    .WaitAsync(probeToken);
                probeTask = null;
                probeToken.ThrowIfCancellationRequested();
                status = "success";
                return new(summary, update);
            }
            catch (TimeoutException)
            {
                status = "timeout";
                LogDebugClusterManifestPeerProbeTimedOut(peer, PeerManifestProbeTimeout);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                status = "canceled";
                throw;
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                status = "timeout";
                LogDebugClusterManifestPeerProbeTimedOut(peer, PeerManifestProbeTimeout);
                return null;
            }
            catch (Exception exception)
            {
                LogDebugErrorRetrievingClusterManifestFromPeer(exception, peer);
                return null;
            }
            finally
            {
                slots.Release();
                _instruments.OnPeerProbe(status);
                if (probeTask is { IsCompleted: false })
                {
                    probeTask.ContinueWith(
                        task => LogDebugLateClusterManifestPeerProbeFailure(task.Exception!, peer),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default).Ignore();
                }
                else
                {
                    _ = probeTask?.Exception;
                }
            }
        }

        private sealed record PeerManifestProbeResult(
            ClusterManifestHashSummary Summary,
            ClusterManifestUpdate? Update);

        private void FillFromCachedHashes(
            ClusterManifestHashSummary summary,
            HashSet<SiloAddress> missing,
            ImmutableDictionary<SiloAddress, GrainManifest>.Builder builder,
            ConcurrentDictionary<ManifestHash, GrainManifest> cache,
            ref bool modified)
        {
            foreach (var silo in missing.ToArray())
            {
                if (summary.SiloManifestHashes.TryGetValue(silo, out var hash))
                {
                    var hit = cache.TryGetValue(hash, out var cached);
                    _instruments.OnCacheLookup(hit, "peer");
                    if (hit)
                    {
                        builder[silo] = cached!;
                        missing.Remove(silo);
                        modified = true;
                    }
                }
            }
        }

        private ClusterManifest CreateClusterManifest(
            MajorMinorVersion version,
            ImmutableDictionary<SiloAddress, GrainManifest> silos)
        {
            return new ClusterManifest(version, silos, [LocalGrainManifest]);
        }

        private bool TryPublishManifest(ClusterManifest manifest)
        {
            var publishSuccess = _updates.TryPublish(manifest);
            if (publishSuccess)
            {
                ManifestEvents.EmitClusterManifestUpdated(this, manifest);
            }

            return publishSuccess;
        }

        private static ImmutableDictionary<SiloAddress, GrainManifest> RemoveNonActiveSilos(
            ImmutableDictionary<SiloAddress, GrainManifest> silos,
            ClusterMembershipSnapshot clusterMembership)
        {
            ImmutableDictionary<SiloAddress, GrainManifest>.Builder? builder = null;
            foreach (var entry in silos)
            {
                if (clusterMembership.GetSiloStatus(entry.Key) == SiloStatus.Active)
                {
                    continue;
                }

                builder ??= silos.ToBuilder();
                builder.Remove(entry.Key);
            }

            return builder?.ToImmutable() ?? silos;
        }

        private Task<GrainManifest> GetSiloManifest(
            SiloAddress siloAddress,
            ConcurrentDictionary<ManifestHash, GrainManifest>? cache,
            CancellationToken cancellationToken) =>
            _instruments.RetrievalDurationEnabled
                ? GetSiloManifestWithMetrics(siloAddress, cache, cancellationToken)
                : GetSiloManifestCore(siloAddress, cache, cancellationToken);

        private async Task<GrainManifest> GetSiloManifestWithMetrics(
            SiloAddress siloAddress,
            ConcurrentDictionary<ManifestHash, GrainManifest>? cache,
            CancellationToken cancellationToken)
        {
            var started = _timeProvider.GetTimestamp();
            var status = "error";
            try
            {
                var manifest = await GetSiloManifestCore(siloAddress, cache, cancellationToken);
                status = "success";
                return manifest;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                status = "canceled";
                throw;
            }
            finally
            {
                _instruments.OnRetrievalCompleted(_timeProvider.GetElapsedTime(started), cache is null ? "direct" : "hash", status);
            }
        }

        private async Task<GrainManifest> GetSiloManifestCore(
            SiloAddress siloAddress,
            ConcurrentDictionary<ManifestHash, GrainManifest>? cache,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache is not null)
            {
                try
                {
                    var remoteManifestProvider = _grainFactory!.GetSystemTarget<IClusterManifestSystemTarget>(Constants.ManifestProviderType, siloAddress);
                    var hash = await AwaitManifestRequest(remoteManifestProvider.GetSiloManifestHash(cancellationToken).AsTask(), cancellationToken);
                    var hit = cache.TryGetValue(hash, out var cached);
                    _instruments.OnCacheLookup(hit, "silo");
                    if (hit)
                    {
                        return cached!;
                    }

                    var manifest = await AwaitManifestRequest(remoteManifestProvider.GetSiloManifestByHash(hash, cancellationToken).AsTask(), cancellationToken);
                    if (manifest is not null && ManifestHashCalculator.ComputeHash(manifest) == hash)
                    {
                        cache[hash] = manifest;
                        return manifest;
                    }

                    _instruments.OnFallback(manifest is null ? "missing" : "mismatch");
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    _instruments.OnFallback("error");
                    LogDebugErrorRetrievingSiloManifestByHash(exception, siloAddress);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var legacyManifestProvider = _grainFactory!.GetSystemTarget<ISiloManifestSystemTarget>(Constants.ManifestProviderType, siloAddress);
            var legacyManifest = await AwaitManifestRequest(legacyManifestProvider.GetSiloManifest(cancellationToken).AsTask(), cancellationToken);
            if (cache is not null)
            {
                cache[ManifestHashCalculator.ComputeHash(legacyManifest)] = legacyManifest;
            }

            return legacyManifest;
        }

        private async Task<T> AwaitManifestRequest<T>(Task<T> request, CancellationToken cancellationToken)
        {
            try
            {
                return await request.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                request.Ignore();
                throw;
            }
        }

        [MemberNotNull(nameof(_runTask))]
        private Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Debug.Assert(_grainFactory is not null);
            var shutdownToken = _shutdownCts.Token;
            _runTask = Task.Run(() => ProcessMembershipUpdates(shutdownToken), shutdownToken);
            return Task.CompletedTask;
        }

        [MemberNotNull(nameof(_grainFactory))]
        private Task Initialize(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _grainFactory = _services.GetRequiredService<IInternalGrainFactory>();
            return Task.CompletedTask;
        }

        private async Task StopAsync(CancellationToken cancellationToken)
        {
            _shutdownCts.Cancel();
            if (_runTask is Task task)
            {
                await task.WaitAsync(cancellationToken).SuppressThrowing();
            }
        }

        public void Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(
                nameof(ClusterManifestProvider),
                ServiceLifecycleStage.RuntimeServices,
                Initialize,
                NoOpStop);

            lifecycle.Subscribe(
                nameof(ClusterManifestProvider),
                ServiceLifecycleStage.RuntimeGrainServices,
                StartAsync,
                StopAsync);

            static Task NoOpStop(CancellationToken _) => Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Error retrieving silo manifest for silo {SiloAddress}"
        )]
        private partial void LogWarningErrorRetrievingSiloManifest(Exception exception, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Error retrieving silo manifest by hash for silo {SiloAddress}. Falling back to direct manifest fetch."
        )]
        private partial void LogDebugErrorRetrievingSiloManifestByHash(Exception exception, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Error retrieving cluster manifest from peer {SiloAddress}. Falling back to direct manifest fetch."
        )]
        private partial void LogDebugErrorRetrievingClusterManifestFromPeer(Exception exception, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Cluster manifest peer probe to {SiloAddress} exceeded {Timeout}. Direct manifest fetch continues."
        )]
        private partial void LogDebugClusterManifestPeerProbeTimedOut(SiloAddress siloAddress, TimeSpan timeout);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Skipping cluster manifest peer probe for {SiloAddress} while three local probe attempts are active. Direct manifest fetch continues."
        )]
        private partial void LogDebugClusterManifestPeerProbeAtCapacity(SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Cluster manifest peer probe task for {SiloAddress} faulted after the caller stopped waiting."
        )]
        private partial void LogDebugLateClusterManifestPeerProbeFailure(Exception exception, SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Starting to process membership updates"
        )]
        private partial void LogDebugStartingToProcessMembershipUpdates();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Stopped processing membership updates"
        )]
        private partial void LogDebugStoppedProcessingMembershipUpdates();
    }

    /// <summary>
    /// 最新 <see cref="ClusterManifestProvider"/>的最近一份清点更新的点对时间的快照
    /// 用来判断未在最后期限内完成的趋同等待。
    /// </summary>
    /// <param name="AttemptId">一名每个提供者单质增加这一企图的识别特征,过去曾
    /// 直接取出后遗漏的完成量,超过其尝试(e.g.一个被同侪修理所取代)
    /// 一旦新的尝试开始。</param>
    /// <param name="MembershipVersion">成员版的这一尝试与会员国文本相协调。</param>
    /// <param name="AttemptStartedAt">协调世界时,这一尝试开始。</param>
    /// <param name="PendingSilos">其舱单取出尚未进行此项尝试的 silos 。
    /// 每件取来都完成( 成功、 通过同行修理或失败); 每次取回一空, 就会空
    /// 即使未遂行为尚未公布清单,也已完成。</param>
    /// <param name="LastFailedSilo">被最近的调试失败(如果有的话)所针对 silo 。</param>
    /// <param name="LastFailureMessage">最近的调取失败的例外信息。</param>
    /// <param name="LastFailureAt">最新调取失败(如果有的话)的UTC时间。</param>
    internal sealed record ManifestUpdateAttemptDiagnostics(
        long AttemptId,
        MembershipVersion MembershipVersion,
        DateTime AttemptStartedAt,
        ImmutableHashSet<SiloAddress> PendingSilos,
        SiloAddress? LastFailedSilo,
        string? LastFailureMessage,
        DateTime? LastFailureAt);
}
