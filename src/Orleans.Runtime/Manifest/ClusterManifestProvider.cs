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
        /// 获取描述此提供程序最近一次清单更新尝试的快照，其中包括
        /// 正在协调的成员资格版本、仍待获取清单的 Silo，以及最近一次获取
        /// 失败（如果有）。用于在无需复现的情况下诊断停滞的收敛等待。
        /// </summary>
        internal ManifestUpdateAttemptDiagnostics? LastAttemptDiagnostics => Volatile.Read(ref _lastAttemptDiagnostics);

        /// <summary>
        /// 获取读取期间同时存在的已发布清单及最新尝试的诊断信息。
        /// 尝试的成员资格版本标识正在进行的协调；清单可能反映
        /// 下一次尝试开始前由 <see cref="Current"/> 同步的更新成员资格。
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
        /// 记录获取操作完成的结果，将其归属到发起该操作的尝试。比较并交换操作保留并发的
        /// 完成结果，而尝试标识将迟到的获取结果与更新的尝试隔离。
        /// </summary>
        /// <param name="attemptId">此次获取为之启动的尝试的标识。</param>
        /// <param name="siloAddress">其获取操作已不再待处理的 Silo。</param>
        /// <param name="exception">如果获取未成功，则为失败信息。如果成功，则为 <see langword="null"/>
        /// （包括已被对等节点修复取代的情况），若获取令牌请求取消也为该值。</param>
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
                // 在关闭期间忽略。
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
            // 被更新发布操作超越的更新仍继续填充自身原先的缓存。
            var cache = Volatile.Read(ref _manifestCache);
            var existingManifest = EnsureValidManifestForCurrentMembership(clusterMembership);
            if (existingManifest.Version.Major > clusterMembership.Version.Value)
            {
                return true;
            }

            var builder = existingManifest.Silos.ToBuilder();
            var modified = false;

            // 填充缺失条目。
            var missingSilos = new List<SiloAddress>();
            foreach (var entry in clusterMembership.Members)
            {
                var member = entry.Value;
                if (member.Status != SiloStatus.Active)
                {
                    // 如果成员尚未处于活动状态，它可能还未准备好处理请求。
                    continue;
                }

                var siloAddress = member.SiloAddress;
                if (builder.ContainsKey(siloAddress))
                {
                    // 已获取该集群成员的清单。
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
                    // 直接获取已提供全部缺失清单。
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

                // 每次获取都观察自身的失败，因此冗余请求能够独立完成。
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

            // 无论成功或失败，如果清单发生修改，都更新清单。
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

            // 每次发布都用当前有效清单初始化新的读穿透缓存。获取操作填充
            // 各自捕获的缓存实例，从而将迟到的完成结果隔离在原来的世代中。
            var cache = new ConcurrentDictionary<ManifestHash, GrainManifest>();
            foreach (var grainManifest in manifest.AllGrainManifests)
            {
                cache[ManifestHashCalculator.ComputeHash(grainManifest)] = grainManifest;
            }

            cache[ManifestHashCalculator.ComputeHash(LocalGrainManifest)] = LocalGrainManifest;
            // 读取方先捕获缓存，再检查清单版本，因此先公开版本。
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

                // 不保留每个对等节点的清单正文，因此请求完整更新，而不是根据本地
                // 提供程序的版本合成基线。
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
    /// <see cref="ClusterManifestProvider"/> 最近一次清单更新尝试的某一时点快照，
    /// 用于诊断在截止时间内未完成的收敛等待。
    /// </summary>
    /// <param name="AttemptId">此尝试在每个提供程序内部单调递增的标识，用于在
    /// 更新的尝试开始后丢弃超出原尝试生命周期的直接获取完成结果
    /// （例如已被对等节点修复取代的获取）。</param>
    /// <param name="MembershipVersion">此次尝试要协调的成员资格版本。</param>
    /// <param name="AttemptStartedAt">此次尝试开始的 UTC 时间。</param>
    /// <param name="PendingSilos">此次尝试中仍有待完成清单获取的 Silo。随着每次获取完成
    /// （成功、通过对等节点修复完成或失败），此集合逐渐缩小；一旦每次获取都已
    /// 结束，即使尝试尚未发布清单，此集合也为空。</param>
    /// <param name="LastFailedSilo">最近一次获取失败所针对的 Silo（如果有）。</param>
    /// <param name="LastFailureMessage">最近一次获取失败的异常消息（如果有）。</param>
    /// <param name="LastFailureAt">最近一次获取失败的 UTC 时间（如果有）。</param>
    internal sealed record ManifestUpdateAttemptDiagnostics(
        long AttemptId,
        MembershipVersion MembershipVersion,
        DateTime AttemptStartedAt,
        ImmutableHashSet<SiloAddress> PendingSilos,
        SiloAddress? LastFailedSilo,
        string? LastFailureMessage,
        DateTime? LastFailureAt);
}
