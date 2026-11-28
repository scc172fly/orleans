using System.Runtime.CompilerServices;
using Orleans.Runtime;
using static Orleans.Clustering.TestKit.MembershipTableTestData;

namespace Orleans.Clustering.TestKit;

/// <summary>不可改写、无条件且可单独调用的成员资格表保证。</summary>
/// <remarks>每个方法使用新初始化的测试夹具。失败的断言和基础设施错误传播给调用方。</remarks>
public sealed class MembershipTableTestRunner
{
    private readonly MembershipTableTestFixture _fixture;
    private readonly int _seed;
    private readonly Action<string>? _output;
    private readonly int _concurrencyRowCount;
    private readonly int _concurrencyRowPadding;
    private IMembershipTable A => _fixture.First;
    private IMembershipTable B => _fixture.Second;
    private IMembershipTable Other => _fixture.OtherCluster;

    /// <summary>基于已初始化的独立句柄创建运行器。记录数仅控制工作负载，不影响保证。</summary>
    public MembershipTableTestRunner(MembershipTableTestFixture fixture, int seed = 0, Action<string>? output = null, int concurrencyRowCount = 128)
        : this(fixture, seed, output, concurrencyRowCount, concurrencyRowPadding: 0)
    {
    }

    /// <summary>基于已初始化的独立句柄创建运行器，并允许配置并发读取记录的有效负载。</summary>
    /// <param name="fixture">拥有提供程序句柄的已初始化测试夹具。</param>
    /// <param name="seed">确定性的测试数据生成种子。</param>
    /// <param name="output">接收符合性测试的诊断信息。</param>
    /// <param name="concurrencyRowCount">并发读取场景使用的记录数。</param>
    /// <param name="concurrencyRowPadding">追加到每个并发读取记录的主机名末尾的字符数。</param>
    public MembershipTableTestRunner(
        MembershipTableTestFixture fixture,
        int seed,
        Action<string>? output,
        int concurrencyRowCount,
        int concurrencyRowPadding)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        if (fixture.First is null || fixture.Second is null || fixture.OtherCluster is null)
            throw new ArgumentException("Initialize the fixture before constructing its runner.", nameof(fixture));
        if (concurrencyRowCount is < 3 or > 10000) throw new ArgumentOutOfRangeException(nameof(concurrencyRowCount));
        if (concurrencyRowPadding is < 0 or > 256 * 1024) throw new ArgumentOutOfRangeException(nameof(concurrencyRowPadding));
        _seed = seed;
        _output = output;
        _concurrencyRowCount = concurrencyRowCount;
        _concurrencyRowPadding = concurrencyRowPadding;
    }

    /// <summary>G01：插入及其版本变更构成一次精确递增 1 的提交，并保留哨兵记录。</summary>
    public Task InsertRow_CurrentTableVersion_CommitsExactlyOneVersion(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Insert(A, Entry(1), ct);
        await Insert(B, Entry(2), ct);
        await SameHandles(ct);
    }, cancellationToken);

    /// <summary>G02：使用当前令牌的向前更新仅替换其目标记录。</summary>
    public Task UpdateRow_CurrentTokens_CommitsExactlyOneVersion(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var populated = Forward(Entry(1));
        await Update(A, populated, ct);
        var updated = Forward(populated);
        updated.IAmAliveTime = T2;
        updated.SuspectTimes![0] = Tuple.Create(updated.SuspectTimes[0].Item1, T1);
        await Update(B, updated, ct);
        await SameHandles(ct);
        updated = Forward(updated);
        updated.SuspectTimes = [];
        await Update(A, updated, ct);
        await SameHandles(ct);
        updated = Forward(updated);
        await Update(B, updated, ct);
        updated = Forward(updated);
        updated.SuspectTimes = null;
        await Update(A, updated, ct);
        await SameHandles(ct);
    }, cancellationToken);

    /// <summary>G03：版本相同时，规范字段和非 Dead 记录保持不变，而存活信息可以变化，Dead 记录可以压实清理。</summary>
    public Task Reads_SameVersion_PreservesRetainedCanonicalFields(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var dead = Entry(3, SiloStatus.Dead);
        await Insert(A, dead, ct);
        var history = new MembershipHistory();
        history.Observe(await Read(A, ct));
        history.Observe(await Read(B, ct));
        await Heartbeat(B, Entry(1), T2, ct);
        var beforeCleanup = await Read(A, ct);
        history.Observe(beforeCleanup);
        await B.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        var compacted = await Read(A, ct);
        AssertCleanup(beforeCleanup, compacted, T1);
        history.Observe(compacted);
        Check(!compacted.Rows.ContainsKey(dead.SiloAddress.ToParsableString()), "old Dead was not compacted");
        Check(history.IsTerminal(dead.SiloAddress.ToParsableString()), "terminal history lost during compaction");
        history.Observe(await Read(B, ct));
    }, cancellationToken);

    /// <summary>G04：读取可以跳过两次已分别验证的提交。</summary>
    public Task Reads_MaySkipCommittedVersions_WithoutSkippingHistoryValidation(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var initial = await Read(A, ct);
        var history = new MembershipHistory();
        history.Observe(initial);
        await Update(B, Forward(Entry(1)), ct);
        var final = await Update(B, Forward(Entry(2)), ct);
        var observed = await Read(A, ct);
        Check(observed.Version == initial.Version + 2, $"expected skipped read version={initial.Version + 2}, observed={observed.Version}");
        Equal(final, observed);
        history.Observe(observed);
    }, cancellationToken);

    /// <summary>G05：合法生命周期、终止状态台账以及不同的后继代次。</summary>
    public Task Lifecycle_DeadRemainsTerminalAfterCompaction_SuccessorUsesNewGeneration(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        var entry = Entry(1);
        var history = new MembershipHistory();
        history.Observe(await Insert(A, entry, ct));
        while (entry.Status != SiloStatus.Dead)
        {
            entry = Forward(entry);
            history.Observe(await Update(B, entry, ct));
        }

        var beforeCleanup = await SameHandles(ct);
        await A.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        var compacted = await Read(B, ct);
        AssertCleanup(beforeCleanup, compacted, T1);
        history.Observe(compacted);
        Check(compacted.Rows.Count == 0, "Dead predecessor must compact");
        var successor = CreateSuccessor(entry);
        Check(successor.SiloAddress.Generation > entry.SiloAddress.Generation
            && successor.SiloAddress.Endpoint.Equals(entry.SiloAddress.Endpoint), "successor must have same endpoint and greater generation");
        history.Observe(await Insert(B, successor, ct));
        var final = await SameHandles(ct);
        Check(!final.Rows.ContainsKey(entry.SiloAddress.ToParsableString()), "compacted predecessor reappeared");
        Check(history.IsTerminal(entry.SiloAddress.ToParsableString()), "predecessor terminality was forgotten");
        EqualRow(MembershipEntrySnapshot.Capture(successor), final.Row(successor.SiloAddress).Entry);
    }, cancellationToken);

    /// <summary>G06：所属 Silo 的存活信息写入保留规范成员资格字段、表版本及 ETag。</summary>
    public Task UpdateIAmAlive_OwnerWrites_PreserveCanonicalFieldsAndTableVersion(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        await Heartbeat(A, Entry(1), T1, ct);
        await Heartbeat(A, Entry(1), T2, ct);
        await Heartbeat(A, Entry(1), T2, ct);
        await SameHandles(ct);
    }, cancellationToken);

    /// <summary>G07：在所属 Silo 的心跳写入之前捕获的令牌，仅提交一次规范状态变更。</summary>
    public Task UpdateRow_TokensCapturedBeforeHeartbeat_CommitStatusChange(CancellationToken cancellationToken = default)
        => Run(ct => UpdateAfterHeartbeat(updateStatus: true, ct), cancellationToken);

    /// <summary>G08：在所属 Silo 的心跳写入之前捕获的令牌，仅提交一次规范怀疑投票变更。</summary>
    public Task UpdateRow_TokensCapturedBeforeHeartbeat_CommitVoteChange(CancellationToken cancellationToken = default)
        => Run(ct => UpdateAfterHeartbeat(updateStatus: false, ct), cancellationToken);

    private async Task UpdateAfterHeartbeat(bool updateStatus, CancellationToken ct)
    {
        await Seed(ct);
        var before = await SameHandles(ct);
        var payload = updateStatus ? Forward(Entry(1)) : Entry(1);
        if (!updateStatus)
        {
            payload.AddOrUpdateSuspector(Entry(2).SiloAddress, T1, maxVotes: 10);
        }

        await B.UpdateIAmAliveAsync(new MembershipEntry { SiloAddress = payload.SiloAddress, IAmAliveTime = T2 }, ct);
        Check(await A.UpdateRowAsync(payload, before.Row(payload.SiloAddress).Etag, before.Next(), ct),
            "canonical update with pre-heartbeat tokens returned false");
        AssertCommit(before, await SameHandles(ct), payload);
    }

    /// <summary>G09：分别构造的不同句柄共享同一个已提交的后端表。</summary>
    public Task Handles_IndependentlyConstructed_ShareCommittedBackingState(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        Check(!ReferenceEquals(A, B), "two independently constructed IMembershipTable instances required");
        var first = await Insert(A, Entry(1), ct);
        Equal(first, await Read(B, ct));
        var second = await Insert(B, Entry(2), ct);
        Equal(second, await Read(A, ct));
    }, cancellationToken);

    /// <summary>G10：使用过期表版本的插入没有副作用。</summary>
    public Task InsertRow_StaleTableToken_ReturnsFalseWithoutSideEffects(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Insert(A, Entry(1), ct);
        var stale = (await Read(A, ct)).Next();
        await Insert(B, Entry(2), ct);
        await Reject(() => A.InsertRowAsync(Entry(3), stale, ct), ct);
    }, cancellationToken);

    /// <summary>G11：即使刚刚读取了目标记录的元数据，过期的表令牌仍会被拒绝。</summary>
    public Task UpdateRow_StaleTableTokenWithCurrentRowToken_ReturnsFalseWithoutSideEffects(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var stale = await Read(A, ct);
        await Update(B, Forward(Entry(2)), ct);
        var current = await Read(A, ct);
        var rowEtag = current.Row(Entry(1).SiloAddress).Etag;
        Check(stale.TableEtag != current.TableEtag,
            "stale-table arrangement must advance the table token while using the freshly read target row token");
        var rejected = Forward(Entry(1));
        rejected.IAmAliveTime = T2;
        await Reject(() => A.UpdateRowAsync(rejected, rowEtag, stale.Next(), ct), ct);
    }, cancellationToken);

    /// <summary>G12：同一记录提交之前的快照会被拒绝，且不产生部分变更。</summary>
    public Task UpdateRow_StaleSnapshotAfterSameRowCommit_ReturnsFalseWithoutSideEffects(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var stale = await Read(A, ct);
        var joining = Forward(Entry(1));
        var current = await Update(B, joining, ct);
        Check(stale.TableEtag != current.TableEtag, "arrangement did not produce a stale table token");
        var rejected = Forward(joining);
        rejected.IAmAliveTime = T2;
        await Reject(() => A.UpdateRowAsync(rejected, stale.Row(joining.SiloAddress).Etag, stale.Next(), ct), ct);
    }, cancellationToken);

    /// <summary>G13：使用当前有效候选值的重复标识会被原子地拒绝。</summary>
    public Task InsertRow_DuplicateIdentity_ReturnsFalseWithoutSideEffects(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var current = await Read(B, ct);
        var rejected = Forward(Entry(1));
        rejected.IAmAliveTime = T2;
        await Reject(() => A.InsertRowAsync(rejected, current.Next(), ct), ct);
    }, cancellationToken);

    /// <summary>G14：真实的记录令牌不能通过 UpdateRow 创建缺失的记录。</summary>
    public Task UpdateRow_MissingIdentityWithRealToken_ReturnsFalseWithoutSideEffects(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var current = await Read(B, ct);
        var rejected = Entry(3);
        rejected.IAmAliveTime = T2;
        await Reject(() => A.UpdateRowAsync(rejected, current.Row(Entry(1).SiloAddress).Etag, current.Next(), ct), ct);

        var dead = Entry(4, SiloStatus.Active);
        var beforeDeath = await Insert(A, dead, ct);
        dead.Status = SiloStatus.Dead;
        var beforeCleanup = await Update(B, dead, ct);
        await Reject(() => A.UpdateRowAsync(dead, beforeDeath.Row(dead.SiloAddress).Etag, beforeDeath.Next(), ct), ct);
        var delayedRowEtag = beforeCleanup.Row(dead.SiloAddress).Etag;
        var delayedVersion = beforeCleanup.Next();
        await B.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        var compacted = await SameHandles(ct);
        AssertCleanup(beforeCleanup, compacted, T1);
        await Reject(() => A.UpdateRowAsync(dead, delayedRowEtag, delayedVersion, ct), ct);
        await Reject(() => A.UpdateRowAsync(dead, delayedRowEtag, compacted.Next(), ct), ct);
    }, cancellationToken);

    /// <summary>针对存在和不存在的标识，运行完整快照查找保证的验证。</summary>
    [Obsolete("Use ReadAll_SelectsPresentAndAbsentIdentities instead.")]
    public Task ReadRow_AndReadAll_AgreeForPresentAndAbsentIdentities(CancellationToken cancellationToken = default)
        => ReadAll_SelectsPresentAndAbsentIdentities(cancellationToken);

    /// <summary>G15：完整快照可选取不同代次、其他终结点及不存在的标识。</summary>
    public Task ReadAll_SelectsPresentAndAbsentIdentities(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var successor = CreateSuccessor(Entry(1));
        await Insert(B, successor, ct);
        var all = await SameHandles(ct);
        Check(all.Rows.Count == 3, $"expected three complete identities, observed={all.Rows.Count}");
        var snapshot = await B.ReadAllAsync(ct);
        foreach (var entry in new[] { Entry(1), Entry(2), successor, Entry(3) })
        {
            var selected = snapshot.TryGet(entry.SiloAddress);
            var actual = selected is null ? new MembershipTableData(snapshot.Version) : new MembershipTableData(selected, snapshot.Version);
            Equal(all.Select(entry.SiloAddress), ClusteringMembershipSnapshot.Capture(actual));
        }
    }, cancellationToken);

    /// <summary>G16：实际保留的数据、条目及嵌套列表在后续写入后仍保持不变。</summary>
    public Task Reads_RetainedObjectsRemainUnchangedAfterLaterWrites(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var populated = Forward(Entry(1));
        await Update(A, populated, ct);
        var retained = await A.ReadAllAsync(ct);
        var row = retained.TryGet(Entry(1).SiloAddress)!;
        var entryReference = row.Item1;
        var listReference = entryReference.SuspectTimes;
        var listBaseline = listReference?.Select(v => new SuspectSnapshot(v.Item1.ToParsableString(), v.Item2)).ToArray() ?? [];
        var baseline = ClusteringMembershipSnapshot.Capture(retained);
        await Heartbeat(B, Entry(1), T2, ct);
        await Update(A, Forward(populated), ct);
        await Update(B, Forward(Entry(2)), ct);
        Equal(baseline, ClusteringMembershipSnapshot.Capture(retained), complete: true);
        EqualRow(baseline.Row(Entry(1).SiloAddress).Entry, MembershipEntrySnapshot.Capture(entryReference), complete: true);
        Check(listBaseline.SequenceEqual(listReference?.Select(v => new SuspectSnapshot(v.Item1.ToParsableString(), v.Item2)) ?? []),
            "retained actual suspect-list reference changed after provider writes");
    }, cancellationToken);

    /// <summary>G17：调用方在插入后保留的条目和嵌套怀疑列表别名无法修改存储。</summary>
    public Task InsertRow_MutatingInputAndSuspectList_DoesNotMutateStoredState(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Insert(A, Entry(2), ct);
        var input = Entry(1);
        var committed = await Insert(A, input, ct);
        MutateCaller(input);
        Equal(committed, await SameHandles(ct));
    }, cancellationToken);

    /// <summary>G18：调用方在更新后保留的条目和嵌套怀疑列表别名无法修改存储。</summary>
    public Task UpdateRow_MutatingInputAndSuspectList_DoesNotMutateStoredState(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var input = Forward(Entry(1));
        var committed = await Update(A, input, ct);
        MutateCaller(input);
        Equal(committed, await SameHandles(ct));
    }, cancellationToken);

    /// <summary>G19：调用方修改完整快照及从中选取的记录，不能修改存储。</summary>
    public Task Reads_MutatingReturnedEntryAndSuspectList_DoesNotMutateStoredState(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var baseline = await Read(B, ct);
        var all = await A.ReadAllAsync(ct);
        MutateCaller(all.TryGet(Entry(1).SiloAddress)!.Item1);
        Equal(baseline, await Read(B, ct));
        var second = await B.ReadAllAsync(ct);
        MutateCaller(second.TryGet(Entry(2).SiloAddress)!.Item1);
        Equal(baseline, await SameHandles(ct));
    }, cancellationToken);

    /// <summary>G20：使用同一个表候选值、已准备就绪的两次跨记录写入恰好有一次成功。</summary>
    public Task ConcurrentCrossRowUpdates_SharedTableVersion_HaveExactlyOneWinner(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var before = await SameHandles(ct);
        var first = Forward(Entry(1));
        var second = Forward(Entry(2));
        var start = Gate();
        var readyA = Gate();
        var readyB = Gate();
        async Task<bool> Write(IMembershipTable table, MembershipEntry entry, TaskCompletionSource ready)
        {
            ready.SetResult();
            await start.Task.WaitAsync(ct);
            return await table.UpdateRowAsync(entry, before.Row(entry.SiloAddress).Etag, before.Next(), ct);
        }

        var writes = new[] { Write(A, first, readyA), Write(B, second, readyB) };
        await Task.WhenAll(readyA.Task, readyB.Task).WaitAsync(ct);
        start.SetResult();
        var results = await Task.WhenAll(writes).WaitAsync(ct);
        Check(results.Count(success => success) == 1, $"exactly one winner required; observed=[{string.Join(",", results)}]");
        AssertCommit(before, await SameHandles(ct), results[0] ? first : second);
    }, cancellationToken);

    /// <summary>G21：有界的同时完整读取只能匹配提交前或提交后的完整视图。</summary>
    public Task ConcurrentReadAll_ReturnsOnlyAtomicCommittedViews(CancellationToken cancellationToken = default)
        => Run(ConcurrentReads, cancellationToken);

    /// <summary>运行并发完整快照保证的验证。</summary>
    [Obsolete("Use ConcurrentReadAll_ReturnsOnlyAtomicCommittedViews instead.")]
    public Task ConcurrentReadRow_ReturnsOnlyAtomicCommittedViews(CancellationToken cancellationToken = default)
        => ConcurrentReadAll_ReturnsOnlyAtomicCommittedViews(cancellationToken);

    /// <summary>G23：在现有句柄及新句柄上重复初始化，精确保留非空状态。</summary>
    public Task InitializeMembershipTable_RepeatedWithData_PreservesCommittedState(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        await Update(B, Forward(Entry(1)), ct);
        await Heartbeat(A, Entry(1), T2, ct);
        var baseline = await SameHandles(ct);
        var additional = await _fixture.CreateAdditionalHandleAsync(_fixture.ClusterId, ct);
        foreach (var table in new[] { A, additional.Table })
        {
            foreach (var initializeVersion in new[] { true, false })
            {
                await table.InitializeMembershipTableAsync(initializeVersion, ct);
                Equal(baseline, await Read(table, ct));
                Equal(baseline, await SameHandles(ct));
            }
        }
    }, cancellationToken);

    /// <summary>G24：共享后端中重叠的标识按集群隔离，而非按服务或地址隔离。</summary>
    public Task Clusters_SharedBackendWithOverlappingSiloAddresses_AreIsolated(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var a = await SameHandles(ct);
        var otherEntry = Entry(1);
        otherEntry.HostName = "other-cluster-host";
        var other = await Insert(Other, otherEntry, ct);
        Equal(a, await SameHandles(ct));
        await Heartbeat(B, Entry(1), T2, ct);
        await Update(A, Forward(Entry(1)), ct);
        await A.InitializeMembershipTableAsync(true, ct);
        Equal(other, await Read(Other, ct));
        a = await SameHandles(ct);
        await Heartbeat(Other, otherEntry, T2, ct);
        await Update(Other, Forward(otherEntry), ct);
        await Other.InitializeMembershipTableAsync(false, ct);
        Equal(a, await SameHandles(ct));
        other = await Read(Other, ct);
        foreach (var tableAndSnapshot in new[] { (A, a), (Other, other) })
        {
            var (table, snapshot) = tableAndSnapshot;
            var key = otherEntry.SiloAddress;
            var observed = await table.ReadAllAsync(ct);
            var selected = observed.TryGet(key);
            Check(selected is not null, $"isolated snapshot omitted identity={key}");
            Equal(snapshot.Select(key), ClusteringMembershipSnapshot.Capture(new MembershipTableData(selected!, observed.Version)));
        }

        var otherSnapshot = await Other.ReadAllAsync(ct);
        Check(otherSnapshot.TryGet(Entry(2).SiloAddress) is null, "isolated snapshot leaked an identity from another cluster");
        Equal(other, ClusteringMembershipSnapshot.Capture(otherSnapshot));
    }, cancellationToken);

    /// <summary>G25：符合条件的 Dead 状态行在同一版本或原子递增 1 的批次中进行压实清理；保留行的规范字段保持不变。</summary>
    public Task CleanupDefunctSiloEntries_RemovesOnlyStrictlyOldDeadRows(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        var publishedHeartbeats = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var entry in CreateCleanupEntries(_seed)) await SeedCleanupEntry(entry);
        var other = await Insert(Other, Entry(1, SiloStatus.Dead), ct);
        var baseline = await SameHandles(ct);
        var history = new MembershipHistory();
        history.Observe(baseline);
        Check(baseline.Rows.Values.Count(row => IsEligibleForCleanup(row.Entry, T1, publishedHeartbeats)) == 1,
            "cleanup matrix must contain exactly one removable Dead row");
        await B.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        var actual = await SameHandles(ct);
        AssertCleanup(baseline, actual, T1, publishedHeartbeats: publishedHeartbeats);
        history.Observe(actual);
        await A.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        Equal(actual, await SameHandles(ct));
        var exclusiveCutoff = T1.AddTicks(1);
        Check(actual.Rows.Values.Count(row => IsEligibleForCleanup(row.Entry, exclusiveCutoff, publishedHeartbeats)) == 1,
            "the one-tick-later cutoff must select only the equality-boundary Dead row");
        await B.CleanupDefunctSiloEntriesAsync(new(exclusiveCutoff), ct);
        var boundaryRemoved = await SameHandles(ct);
        AssertCleanup(actual, boundaryRemoved, exclusiveCutoff, publishedHeartbeats: publishedHeartbeats);
        history.Observe(boundaryRemoved);
        for (var i = 200; i < 203; i++) await SeedCleanupEntry(Entry(i, SiloStatus.Dead));
        await CleanupWithConcurrentReads(publishedHeartbeats, ct);
        await SeedCleanupEntry(Entry(220, SiloStatus.Dead));
        await CleanupFromBothHandles(publishedHeartbeats, ct);
        Equal(other, await Read(Other, ct));

        async Task SeedCleanupEntry(MembershipEntry entry)
        {
            var live = Copy(entry);
            if (live.Status == SiloStatus.Dead) live.Status = SiloStatus.Active;
            live.SuspectTimes = [];
            await Insert(A, live, ct);
            await Heartbeat(A, live, entry.IAmAliveTime, ct);
            publishedHeartbeats.Add(entry.SiloAddress.ToParsableString(), entry.IAmAliveTime);
            if (entry.Status == SiloStatus.Dead) await Update(A, entry, ct);
        }
    }, cancellationToken);

    /// <summary>G26：在释放所有者之前，通过原生验证确认删除，同时另一集群保持完整。</summary>
    public Task DeleteMembershipTableEntries_DeletesOwnClusterAndPreservesOtherCluster(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var other = await Insert(Other, Entry(1), ct);
        await _fixture.DeleteClusterAsync(A, _fixture.ClusterId, allowRetained: false, ct);
        await _fixture.AssertHistoryPresentAsync(_fixture.OtherClusterId, ct);
        Equal(other, await Read(Other, ct));
    }, cancellationToken);

    /// <summary>G27：删除另一个已有数据的集群，绝不会删除配置的集群。</summary>
    public Task DeleteMembershipTableEntries_DifferentClusterId_NeverDeletesConfiguredCluster(CancellationToken cancellationToken = default) => Run(async ct =>
    {
        await Seed(ct);
        var baseline = await SameHandles(ct);
        var otherBefore = await Insert(Other, Entry(1), ct);
        await _fixture.AssertHistoryPresentAsync(_fixture.ClusterId, ct);
        await _fixture.AssertHistoryPresentAsync(_fixture.OtherClusterId, ct);
        Equal(baseline, await SameHandles(ct));
        Equal(otherBefore, await Read(Other, ct));
        var deleted = await _fixture.DeleteClusterAsync(A, _fixture.OtherClusterId, allowRetained: true, ct);
        await _fixture.AssertHistoryPresentAsync(_fixture.ClusterId, ct);
        if (!deleted)
        {
            Equal(otherBefore, await Read(Other, ct));
            await _fixture.DeleteClusterAsync(Other, _fixture.OtherClusterId, allowRetained: false, ct);
            await _fixture.AssertHistoryPresentAsync(_fixture.ClusterId, ct);
        }
        Equal(baseline, await SameHandles(ct));

    }, cancellationToken);

    private MembershipEntry Entry(int index, SiloStatus status = SiloStatus.Created) => CreateInitialEntry(index, _seed, status);

    private async Task Run(Func<CancellationToken, Task> action, CancellationToken cancellationToken, [CallerMemberName] string guarantee = "")
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await action(timeout.Token).WaitAsync(timeout.Token); }
        catch (ClusteringConformanceException exception)
        {
            var failure = ClusteringTestKitDiagnostics.CreateFailure(_fixture.ProviderName, guarantee, _fixture.ClusterId, "A1/A2/B1", _seed, exception.Message, exception);
            _output?.Invoke(failure.Message);
            throw failure;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw ClusteringTestKitDiagnostics.CreateFailure(_fixture.ProviderName, guarantee, _fixture.ClusterId, "A1/A2/B1", _seed,
                "bounded scenario timed out; required operation/readiness/completion evidence is missing");
        }
    }

    private static void Check(bool condition, string detail) => ClusteringTestKitDiagnostics.Require(condition, detail);
    internal static void Equal(ClusteringMembershipSnapshot expected, ClusteringMembershipSnapshot observed, bool complete = false)
    {
        var difference = complete ? expected.CompareComplete(observed) : expected.CompareCanonical(observed);
        Check(difference is null, difference ?? "equal");
    }
    internal static void EqualRow(MembershipEntrySnapshot expected, MembershipEntrySnapshot observed, bool complete = false)
        => Check(expected.Difference(observed, complete) is null, expected.Difference(observed, complete) ?? "equal");
    internal static async Task<ClusteringMembershipSnapshot> Read(IMembershipTable table, CancellationToken ct)
        => ClusteringMembershipSnapshot.Capture(await table.ReadAllAsync(ct));

    private async Task Seed(CancellationToken ct)
    {
        await Insert(A, Entry(1), ct);
        await Insert(B, Entry(2), ct);
    }

    private async Task<ClusteringMembershipSnapshot> SameHandles(CancellationToken ct)
    {
        var first = await Read(A, ct);
        Equal(first, await Read(B, ct));
        return first;
    }

    internal static async Task<ClusteringMembershipSnapshot> Insert(IMembershipTable table, MembershipEntry input, CancellationToken ct)
    {
        var before = await Read(table, ct);
        var detached = Copy(input);
        Check(await table.InsertRowAsync(input, before.Next(), ct), $"current-table insert returned false; table={before.Next()}, identity={input.SiloAddress}");
        var after = await Read(table, ct);
        AssertCommit(before, after, detached);
        return after;
    }

    internal static async Task<ClusteringMembershipSnapshot> Update(IMembershipTable table, MembershipEntry input, CancellationToken ct)
    {
        var before = await Read(table, ct);
        var detached = Copy(input);
        var rowEtag = before.Row(input.SiloAddress).Etag;
        Check(await table.UpdateRowAsync(input, rowEtag, before.Next(), ct),
            $"fresh-token update returned false; table={before.Next()}, row ETag={rowEtag}, identity={input.SiloAddress}");
        var after = await Read(table, ct);
        AssertCommit(before, after, detached);
        return after;
    }

    internal static void AssertCommit(ClusteringMembershipSnapshot before, ClusteringMembershipSnapshot after, MembershipEntry input)
    {
        var id = input.SiloAddress.ToParsableString();
        Check(after.Version == before.Version + 1, $"commit integer: expected={before.Version + 1}, observed={after.Version}");
        Check(!string.IsNullOrEmpty(after.TableEtag) && after.TableEtag != before.TableEtag,
            $"commit table ETag must change: old={before.TableEtag}, observed={after.TableEtag}");
        Check(after.Rows.TryGetValue(id, out var row), $"committed identity missing={id}");
        var expectedEntry = MembershipEntrySnapshot.Capture(input);
        Equal(before with
        {
            Version = before.Version + 1,
            TableEtag = after.TableEtag,
            Rows = before.Rows.SetItem(id, new(expectedEntry, row!.Etag))
        }, after);
    }

    private static bool IsEligibleForCleanup(
        MembershipEntrySnapshot entry, DateTime cutoff, IReadOnlyDictionary<string, DateTime>? publishedHeartbeats = null)
    {
        // 受控清理历史使用所属 Silo 发布的信息；原始心跳观测结果可能滞后。
        var expected = publishedHeartbeats is null ? entry : entry with { IAmAliveTime = publishedHeartbeats[entry.Identity] };
        return expected.Status == SiloStatus.Dead && expected.GetEffectiveUpdateTime() < cutoff;
    }

    internal static void AssertCleanup(
        ClusteringMembershipSnapshot before,
        ClusteringMembershipSnapshot after,
        DateTime cutoff,
        bool requireAllEligible = true,
        IReadOnlyDictionary<string, DateTime>? publishedHeartbeats = null)
    {
        var eligible = before.Rows.Where(pair => IsEligibleForCleanup(pair.Value.Entry, cutoff, publishedHeartbeats))
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var removed = before.Rows.Keys.Except(after.Rows.Keys).ToArray();
        Check(removed.All(eligible.Contains), "cleanup removed an ineligible identity");
        Check(after.Rows.Keys.All(before.Rows.ContainsKey), "cleanup added an identity");
        if (requireAllEligible)
            Check(eligible.SetEquals(removed), "cleanup left eligible Dead rows in the table");
        if (removed.Length == 0)
        {
            Equal(before, after);
            return;
        }

        var delta = (long)after.Version - before.Version;
        Check(delta >= 0 && delta <= removed.Length,
            $"cleanup version: expected delta in [0,{removed.Length}], observed={delta}");
        Check(!string.IsNullOrEmpty(after.TableEtag)
            && (delta == 0 ? after.TableEtag == before.TableEtag : after.TableEtag != before.TableEtag),
            $"cleanup table ETag must match version progress: delta={delta}, old={before.TableEtag}, observed={after.TableEtag}");
        var expected = before with
        {
            Version = after.Version,
            TableEtag = after.TableEtag,
            Rows = before.Rows.RemoveRange(removed)
        };
        Equal(expected, after);
    }

    private async Task CleanupWithConcurrentReads(IReadOnlyDictionary<string, DateTime> publishedHeartbeats, CancellationToken ct)
    {
        var before = await SameHandles(ct);
        Check(before.Rows.Values.Count(row => IsEligibleForCleanup(row.Entry, T1, publishedHeartbeats)) == 3,
            "batched cleanup requires exactly three eligible Dead rows");
        var start = Gate();
        var ready = Enumerable.Range(0, 3).Select(_ => Gate()).ToArray();
        var observations = new System.Collections.Concurrent.ConcurrentQueue<ClusteringMembershipSnapshot>();
        async Task Cleanup()
        {
            ready[0].SetResult();
            await start.Task.WaitAsync(ct);
            await B.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        }

        async Task Reader(int index)
        {
            ready[index].SetResult();
            await start.Task.WaitAsync(ct);
            var previous = before;
            for (var i = 0; i < 8; i++)
            {
                var sample = await Read(index == 1 ? A : B, ct);
                AssertCleanup(previous, sample, T1, requireAllEligible: false, publishedHeartbeats: publishedHeartbeats);
                observations.Enqueue(sample);
                previous = sample;
            }
        }

        var workers = new[] { Cleanup(), Reader(1), Reader(2) };
        await Task.WhenAll(ready.Select(gate => gate.Task)).WaitAsync(ct);
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(ct);
        var after = await SameHandles(ct);
        AssertCleanup(before, after, T1, publishedHeartbeats: publishedHeartbeats);
        observations.Enqueue(before);
        observations.Enqueue(after);
        var history = new MembershipHistory();
        var previous = before;
        foreach (var observed in observations.OrderBy(snapshot => snapshot.Version).ThenByDescending(snapshot => snapshot.Rows.Count))
        {
            AssertCleanup(previous, observed, T1, requireAllEligible: false, publishedHeartbeats: publishedHeartbeats);
            history.Observe(observed);
            previous = observed;
        }

        await A.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        Equal(after, await SameHandles(ct));
    }

    private async Task CleanupFromBothHandles(IReadOnlyDictionary<string, DateTime> publishedHeartbeats, CancellationToken ct)
    {
        var before = await SameHandles(ct);
        Check(before.Rows.Values.Count(row => IsEligibleForCleanup(row.Entry, T1, publishedHeartbeats)) == 1,
            "cross-handle cleanup must have one eligible Dead row");
        await A.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        var after = await SameHandles(ct);
        AssertCleanup(before, after, T1, publishedHeartbeats: publishedHeartbeats);
        await B.CleanupDefunctSiloEntriesAsync(new(T1), ct);
        Equal(after, await SameHandles(ct));
    }

    internal static async Task Heartbeat(IMembershipTable table, MembershipEntry entry, DateTime time, CancellationToken ct)
    {
        var before = await Read(table, ct);
        var payload = new MembershipEntry { SiloAddress = entry.SiloAddress, IAmAliveTime = time };
        await table.UpdateIAmAliveAsync(payload, ct);
        var after = await Read(table, ct);
        AssertHeartbeat(before, after);
    }

    internal static void AssertHeartbeat(ClusteringMembershipSnapshot before, ClusteringMembershipSnapshot after)
        => Equal(before, after);

    private async Task Reject(Func<Task<bool>> write, CancellationToken ct)
    {
        var before = await SameHandles(ct);
        Check(!await write(), "conditional write unexpectedly succeeded");
        Equal(before, await SameHandles(ct));
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal async Task SeedConcurrentRows(CancellationToken ct)
    {
        var current = ClusteringMembershipSnapshot.Capture(await A.ReadAllAsync(ct));
        Check(current.Rows.Count == 0, "concurrent-read setup requires an empty cluster");
        var expectedRows = current.Rows.ToBuilder();
        // 每次插入都需要新的不透明表 ETag，因此设置阶段在每次插入记录后执行范围不断扩大的完整读取。
        for (var i = 1; i <= _concurrencyRowCount; i++)
        {
            var input = Entry(i);
            if (_concurrencyRowPadding > 0)
            {
                input.HostName += new string('x', _concurrencyRowPadding);
            }
            var expectedEntry = MembershipEntrySnapshot.Capture(input);
            var writer = i % 2 == 0 ? B : A;
            var reader = i % 2 == 0 ? A : B;
            Check(await writer.InsertRowAsync(input, current.Next(), ct),
                $"concurrent-read setup insert failed: index={i}, table={current.Next()}");
            var observed = await reader.ReadAllAsync(ct);
            Check(observed.Version.Version == current.Version + 1, $"setup commit integer: expected={current.Version + 1}, observed={observed.Version.Version}");
            Check(!string.IsNullOrEmpty(observed.Version.VersionEtag) && observed.Version.VersionEtag != current.TableEtag,
                $"setup commit table ETag did not advance: index={i}");
            var row = observed.TryGet(input.SiloAddress);
            Check(row is not null, $"setup insert omitted identity={expectedEntry.Identity}");
            EqualRow(expectedEntry, MembershipEntrySnapshot.Capture(row!.Item1));
            expectedRows.Add(expectedEntry.Identity, new(expectedEntry, row.Item2));
            current = current with { Version = observed.Version.Version, TableEtag = observed.Version.VersionEtag };
        }
        Equal(current with { Rows = expectedRows.ToImmutable() }, await SameHandles(ct));
    }

    private async Task ConcurrentReads(CancellationToken ct)
    {
        await SeedConcurrentRows(ct);
        for (var round = 0; round < 6; round++)
        {
            var before = await SameHandles(ct);
            var cleanup = round >= 4;
            var target = before.Row(Entry(1 + round % 2).SiloAddress).Entry.ToEntry();
            if (cleanup)
            {
                target.Status = SiloStatus.Dead;
                await Update(A, target, ct);
                before = await SameHandles(ct);
            }
            else
            {
                target = Forward(target);
            }
            var start = Gate();
            var ready = Enumerable.Range(0, 4).Select(_ => Gate()).ToArray();
            var observations = new System.Collections.Concurrent.ConcurrentQueue<ClusteringMembershipSnapshot>();
            async Task Writer()
            {
                ready[0].SetResult();
                await start.Task.WaitAsync(ct);
                if (cleanup)
                    await B.CleanupDefunctSiloEntriesAsync(new(T1), ct);
                else
                    Check(await B.UpdateRowAsync(target, before.Row(target.SiloAddress).Etag, before.Next(), ct), "controlled atomic-read writer failed");
            }

            async Task Reader(int index)
            {
                ready[index].SetResult();
                await start.Task.WaitAsync(ct);
                var table = index % 2 == 0 ? B : A;
                var sample = ClusteringMembershipSnapshot.Capture(await table.ReadAllAsync(ct));
                var expectedBefore = before;
                var id = target.SiloAddress.ToParsableString();
                if (sample.Version == before.Version)
                {
                    if (cleanup && !sample.Rows.ContainsKey(id))
                        Equal(expectedBefore with { Rows = expectedBefore.Rows.Remove(id) }, sample);
                    else
                        Equal(expectedBefore, sample);
                }
                else
                {
                    Check(sample.Version == before.Version + 1 && sample.TableEtag != before.TableEtag && !string.IsNullOrEmpty(sample.TableEtag),
                        $"round={round}, reader={index}: invalid atomic version/token pair, expected={before.Version} or {before.Version + 1}, observed={sample.Version}/{sample.TableEtag}");
                    var expectedAfter = before with
                    {
                        Version = before.Version + 1,
                        TableEtag = sample.TableEtag,
                        Rows = cleanup ? before.Rows.Remove(id)
                            : before.Rows.SetItem(id, new(MembershipEntrySnapshot.Capture(target), before.Rows[id].Etag))
                    };
                    if (cleanup)
                    {
                        Check(!sample.Rows.ContainsKey(id), "atomic cleanup still returned the removed identity");
                    }
                    Equal(expectedAfter, sample);
                }
                observations.Enqueue(sample);
            }

            var workers = new[] { Writer(), Reader(1), Reader(2), Reader(3) };
            await Task.WhenAll(ready.Select(g => g.Task)).WaitAsync(ct);
            start.SetResult();
            await Task.WhenAll(workers).WaitAsync(ct);
            var committed = await SameHandles(ct);
            if (cleanup) AssertCleanup(before, committed, T1);
            else AssertCommit(before, committed, target);
            // 写入方完成后，确定已提交的表令牌及其规范字段。
            foreach (var sample in observations)
            {
                var expectedBefore = before;
                var expectedAfter = committed;
                Check(expectedBefore.CompareCanonical(sample) is null || expectedAfter.CompareCanonical(sample) is null,
                    $"atomic read matches neither committed view: before={expectedBefore.CompareCanonical(sample)}; after={expectedAfter.CompareCanonical(sample)}");
            }
        }
    }
}
