using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

[DebuggerDisplay("Count = {Count}, NextSequence = {NextSequence}")]
internal sealed class DurableLog<T> : IDurableLog<T>, IStateMachine, IDurableLogCommandHandler<T>
{
    private readonly IDurableLogCommandCodec<T> _codec;
    private readonly JournaledStateManager? _manager;
    private readonly List<T> _items = [];
    private JournalStreamWriter _writer;

    // 标识历史。首次变更之前为零，因此没有世代标识的日志不包含任何条目，也未封存。
    private long _epoch;
    private long _firstSequence;

    // 最近一次清空之后的首个序列号。位于它之前的读取方观察过清空操作所替换的条目。
    private long _resetSequence;
    private bool _isSealed;

    // 最近一次写入捕获时以及最近一次已确认写入捕获时的日志边界。
    private Boundary _captured;
    private Boundary _committed;

    // 已提交条目的起始序列号不早于首个保留序列号的草稿，按序列顺序排列。首次提交时分配。
    private List<DraftCommitRecord>? _draftCommits;

    // 为此实例的草稿提交记录编号。边界中的计数显示某次写入捕获了哪些提交记录，而
    // 空提交的条目范围无法体现这些信息。
    private long _draftCommitCount;

    // 首次创建草稿时分配。
    private DraftTable? _drafts;

    public DurableLog(
        [ServiceKey] string key,
        IJournaledStateManager manager,
        JournaledStateManagerShared shared,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = JournalFormatServices.GetRequiredCommandCodec<IDurableLogCommandCodec<T>>(serviceProvider, shared.JournalFormatKey);
        _manager = manager as JournaledStateManager;
        manager.RegisterStateMachine(key, this);
    }

    internal DurableLog(string key, IJournaledStateManager manager, IDurableLogCommandCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(key);
        _codec = codec;
        _manager = manager as JournaledStateManager;
        manager.RegisterStateMachine(key, this);
    }

    public int Count => _items.Count;

    public long Epoch => _epoch;

    public long FirstSequence => _firstSequence;

    public long NextSequence => _firstSequence + _items.Count;

    public long CommittedSequence => _committed.NextSequence;

    public bool IsSealed => _isSealed;

    public T this[long sequence] => TryGetIndex(sequence, out var index)
        ? _items[index]
        : throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "The log does not retain an entry with this sequence number.");

    private Boundary Current => new(NextSequence, _firstSequence, _resetSequence, _isSealed, _draftCommitCount);

    public bool TryGet(long sequence, [MaybeNullWhen(false)] out T item)
    {
        if (TryGetIndex(sequence, out var index))
        {
            item = _items[index];
            return true;
        }

        item = default;
        return false;
    }

    public long Append(T item)
    {
        ThrowIfSealed();
        var sequence = NextSequence;
        if (!TryBeginHistory([item], firstSequence: 0, isSealed: false, draftCommits: []))
        {
            _codec.WriteAppend(new ReadOnlySpan<T>(in item), GetWriter());
        }

        _items.Add(item);
        return sequence;
    }

    public void TrimBefore(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, NextSequence);
        if (sequence <= _firstSequence)
        {
            return;
        }

        Debug.Assert(_epoch != 0, "A log with entries has an epoch.");
        _codec.WriteTrimBefore(sequence, GetWriter());
        ApplyTrimBefore(sequence);
    }

    public void Clear()
    {
        ThrowIfSealed();

        // 清空操作消耗由它开启的历史中的首个序列号。
        if (!TryBeginHistory([], firstSequence: 1, isSealed: false, draftCommits: []))
        {
            _codec.WriteClear(GetWriter());
        }

        ApplyClear();
    }

    public void Seal()
    {
        if (_isSealed)
        {
            return;
        }

        if (!TryBeginHistory([], firstSequence: 0, isSealed: true, draftCommits: []))
        {
            _codec.WriteSeal(GetWriter());
        }

        _isSealed = true;
        if (_drafts is { } drafts)
        {
            while (drafts.Open.Count > 0)
            {
                CloseDraft(drafts.Open[^1], DraftOutcome.Abandoned(LogDraftAbandonReason.Sealed, message: null));
            }
        }
    }

    public IDurableLogDraft<T> BeginDraft()
    {
        ThrowIfSealed();
        var drafts = _drafts ??= new DraftTable();
        var draft = new Draft(this, new LogDraftId(drafts.InstanceId, ++drafts.LastNumber));
        drafts.Open.Add(draft);
        return draft;
    }

    public IAsyncEnumerable<LogEvent<T>> ReadAsync(
        long fromSequence = 0,
        long epoch = 0,
        bool includeDrafts = false,
        LogDraftId? resumeDraftId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(epoch);
        return DurableStateObservation.WatchAsync(
            DurableStateObservation.GetRequiredManager(_manager),
            (Log: this, FromSequence: fromSequence, Epoch: epoch, IncludeDrafts: includeDrafts, ResumeDraftId: resumeDraftId),
            static (hub, arg) => arg.Log.CreateReader(hub, arg.FromSequence, arg.Epoch, arg.IncludeDrafts, arg.ResumeDraftId),
            cancellationToken);
    }

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, _codec).Apply(entry.Reader, this);

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
        _items.Clear();
        _epoch = 0;
        _firstSequence = 0;
        _resetSequence = 0;
        _isSealed = false;
        _captured = _committed = default;
        _draftCommits?.Clear();
        _writer = writer;
        if (_drafts is { } drafts)
        {
            while (drafts.Open.Count > 0)
            {
                CloseDraft(drafts.Open[^1], DraftOutcome.Abandoned(LogDraftAbandonReason.JournalDeleted, message: null));
            }
        }

        SignalAllReaders();
    }

    void IStateMachine.OnRecoveryCompleted()
    {
        _captured = _committed = Current;
        SignalAllReaders();
    }

    // 条目和命令在生成时记入日志。捕获操作记录本次写入将其中哪些内容持久化。
    void IStateMachine.WritePendingEntries(JournalStreamWriter writer) => _captured = Current;

    void IStateMachine.WriteSnapshot(JournalStreamWriter writer)
    {
        _codec.WriteSnapshot(
            _epoch,
            _firstSequence,
            _resetSequence,
            _isSealed,
            _items,
            _draftCommits is { Count: > 0 } commits ? commits.ConvertAll(static record => record.Commit) : [],
            writer);
        _captured = Current;
    }

    // 每次写入均在确认之前捕获此日志。管理器在此回调之后发布写入的水位标记，
    // 从而唤醒读取方。
    void IStateMachine.OnWriteCompleted() => _committed = _captured;

    void IDurableLogCommandHandler<T>.ApplyAppend(T item)
    {
        ThrowIfMalformedChange("append");
        _items.Add(item);
    }

    void IDurableLogCommandHandler<T>.ApplyDraftCommit(LogDraftCommit commit)
    {
        // 已记录的提交结束于下一个序列号或之前，且每个提交的起点不早于前一个提交的终点，
        // 因此比较提交的条目数量与其起点之后的条目数不会溢出，而直接计算提交终点则可能溢出。
        var previousEnd = _draftCommits is { Count: > 0 } commits ? commits[^1].Commit.Sequence + commits[^1].Commit.Count : _firstSequence;
        if (commit.Count < 0 || commit.Sequence < previousEnd || commit.Count > NextSequence - commit.Sequence)
        {
            throw new InvalidOperationException(
                $"Malformed durable log entry: a draft commit of {commit.Count} entries at sequence {commit.Sequence} is outside "
                + $"the uncommitted entries from sequence {previousEnd} to {NextSequence}.");
        }

        (_draftCommits ??= []).Add(new(commit, ++_draftCommitCount));
    }

    void IDurableLogCommandHandler<T>.ApplyTrimBefore(long sequence)
    {
        if (sequence < 0 || sequence > NextSequence)
        {
            throw new InvalidOperationException(
                $"Malformed durable log entry: cannot trim before sequence {sequence} when the next sequence is {NextSequence}.");
        }

        ApplyTrimBefore(sequence);
    }

    void IDurableLogCommandHandler<T>.ApplyClear()
    {
        ThrowIfMalformedChange("clear");
        ApplyClear();
    }

    void IDurableLogCommandHandler<T>.ApplySeal()
    {
        if (_epoch == 0)
        {
            throw new InvalidOperationException("Malformed durable log entry: a seal command precedes the log's epoch.");
        }

        _isSealed = true;
    }

    void IDurableLogCommandHandler<T>.Reset(long epoch, long firstSequence, long resetSequence, int capacityHint)
    {
        if (epoch < 0 || resetSequence < 0 || firstSequence < resetSequence || (epoch == 0 && firstSequence != 0))
        {
            throw new InvalidOperationException(
                $"Malformed durable log snapshot: epoch {epoch}, first sequence {firstSequence}, and reset sequence {resetSequence} are inconsistent.");
        }

        _items.Clear();
        _items.EnsureCapacity(capacityHint);
        _draftCommits?.Clear();
        _epoch = epoch;
        _firstSequence = firstSequence;
        _resetSequence = resetSequence;
        _isSealed = false;
    }

    // 重放强制执行写入器强制执行的不变量：变更属于一段历史，已封存的日志不接受
    // 条目追加或清空，每次追加或清空消耗序列号，该序列号不能超过最大值。
    private void ThrowIfMalformedChange(string command)
    {
        if (_epoch == 0 || _isSealed)
        {
            throw new InvalidOperationException(
                $"Malformed durable log entry: {command} command for a log which {(_isSealed ? "is sealed" : "has no epoch")}.");
        }

        if (NextSequence == long.MaxValue)
        {
            throw new InvalidOperationException($"Malformed durable log entry: {command} command for a log whose next sequence is the maximum sequence.");
        }
    }

    // 一段历史以其世代标识开始。没有世代标识的日志为空，因此开启历史的命令被记入日志，
    // 形式为该命令产生的状态的快照，并携带新的世代标识。开启历史和执行命令
    // 由此构成单条日志条目；编码失败会完整丢弃它，使日志仍然没有世代标识。
    private bool TryBeginHistory(IReadOnlyCollection<T> items, long firstSequence, bool isSealed, IReadOnlyCollection<LogDraftCommit> draftCommits)
    {
        if (_epoch != 0)
        {
            return false;
        }

        Debug.Assert(NextSequence == 0 && !_isSealed && _draftCommits is not { Count: > 0 }, "A log without an epoch has no history.");
        var epoch = Random.Shared.NextInt64(1, long.MaxValue);
        _codec.WriteSnapshot(epoch, firstSequence, resetSequence: firstSequence, isSealed, items, draftCommits, GetWriter());
        _epoch = epoch;
        return true;
    }

    // 将草稿条目及结果记为单条日志条目。
    private DraftOutcome CommitDraft(LogDraftId draftId, ReadOnlySpan<T> items)
    {
        Debug.Assert(!_isSealed, "Sealing closes open drafts.");
        var sequence = NextSequence;
        var commit = new LogDraftCommit(draftId, sequence, items.Length);
        if (!TryBeginHistory(items.ToArray(), firstSequence: 0, isSealed: false, draftCommits: [commit]))
        {
            _codec.WriteDraftCommit(draftId, sequence, items, GetWriter());
        }

        _items.AddRange(items);
        var record = new DraftCommitRecord(commit, ++_draftCommitCount);
        (_draftCommits ??= []).Add(record);
        return DraftOutcome.Committed(_epoch, record);
    }

    // 清空消耗一个序列号，因此读取方位置能够表明它是否观察到被替换的条目。
    private void ApplyClear()
    {
        var sequence = NextSequence + 1;
        _items.Clear();
        _draftCommits?.Clear();
        _firstSequence = sequence;
        _resetSequence = sequence;
    }

    private void ThrowIfSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("The log is sealed. A sealed log accepts no entries, clears, or drafts.");
        }
    }

    private void ApplyTrimBefore(long sequence)
    {
        if (sequence <= _firstSequence)
        {
            return;
        }

        _items.RemoveRange(0, checked((int)(sequence - _firstSequence)));
        _firstSequence = sequence;
        if (_draftCommits is { } commits)
        {
            var retained = commits.FindIndex(record => record.Commit.Sequence >= sequence);
            commits.RemoveRange(0, retained < 0 ? commits.Count : retained);
        }
    }

    private bool TryGetIndex(long sequence, out int index)
    {
        if (sequence >= _firstSequence && sequence < NextSequence)
        {
            index = (int)(sequence - _firstSequence);
            return true;
        }

        index = -1;
        return false;
    }

    private JournalStreamWriter GetWriter()
    {
        Debug.Assert(_writer.IsInitialized);
        return _writer;
    }

    // 已提交条目通过写入水位标记变为可见，并唤醒每个读取方。草稿内容在
    // 写入后立即可见。
    private void SignalDraftReaders()
    {
        if (DurableStateObservation.TryGetTopic(_manager, this, out var topic))
        {
            topic.SignalDraftReaders();
        }
    }

    private void SignalAllReaders()
    {
        if (DurableStateObservation.TryGetTopic(_manager, this, out var topic))
        {
            topic.SignalAll();
        }
    }

    private void CloseDraft(Draft draft, DraftOutcome outcome)
    {
        Debug.Assert(draft.IsOpen);
        var drafts = _drafts!;
        var removed = drafts.Open.Remove(draft);
        Debug.Assert(removed);
        draft.Close(outcome);
        if (!outcome.IsCommitted)
        {
            drafts.RecordAbandonment(draft.Id.Number, outcome);
        }

        SignalDraftReaders();
    }

    // 跟踪重新连接的读取方此前正在查看的草稿：如果仍打开则跟踪其各项，否则跟踪结果。日志是
    // 提交记录的权威来源，跨激活实例保留提交及对应条目。放弃操作与草稿项一样是易失性的。
    private FollowedDraft ResumeDraft(LogDraftId id)
    {
        var drafts = _drafts is { } table && table.InstanceId == id.InstanceId ? table : null;
        if (drafts is not null)
        {
            foreach (var draft in drafts.Open)
            {
                if (draft.Id == id)
                {
                    return new(draft);
                }
            }
        }

        if (_draftCommits is { } commits)
        {
            for (var i = commits.Count - 1; i >= 0; i--)
            {
                if (commits[i].Commit.DraftId == id)
                {
                    return new(id, DraftOutcome.Committed(_epoch, commits[i]));
                }
            }
        }

        if (drafts is null)
        {
            return new(id, DraftOutcome.Abandoned(LogDraftAbandonReason.PreviousActivation, message: null));
        }

        return new(id, drafts.TryGetAbandonment(id.Number, out var outcome)
            ? outcome
            : DraftOutcome.Abandoned(LogDraftAbandonReason.Unknown, message: null));
    }

    private Reader CreateReader(JournaledStateObservers hub, long fromSequence, long epoch, bool includeDrafts, LogDraftId? resumeDraftId)
    {
        var reader = new Reader(hub, this, includeDrafts, fromSequence, epoch is 0 ? _epoch : epoch, catchUpSequence: _committed.NextSequence);
        reader.KnownFirstSequence = Math.Min(fromSequence, _committed.FirstSequence);
        if (reader.IncludesDrafts && resumeDraftId is { } draftId)
        {
            reader.FollowedDrafts = [ResumeDraft(draftId)];
        }

        return reader;
    }

    // 根据当前日志状态计算读取方的下一事件。在两次 yield 之间调用，因此每次调用均观察到读取方
    // 挂起期间进行的变更。按以下优先级执行检查：
    // 1. 读取方跟踪的草稿结果，使读取方在历史变更之前了解其关闭方式。对于
    // 历史已经删除的提交，因其条目不复存在，将其报告为已放弃。
    // 2. 历史变更：不同的世代标识、超出末尾的位置或清空操作。每种情况均通过 Reset 重新定位读取方。
    // 3. 已持久化的裁剪。
    // 4. CaughtUp，然后是下一条已提交条目。
    // 5. Sealed，在全部条目均已投递之后。
    // 6. 草稿项。
    // 读取方在裁剪、清空和封存持久化后观察到它们，与观察条目的方式相同。
    private bool TryGetNextEvent(Reader reader, out LogEvent<T> logEvent)
    {
        if (reader.Epoch == 0)
        {
            // 尚未观察到历史的读取方采用日志的历史，该历史从序列号零开始。
            reader.Epoch = _epoch;
        }

        if (reader.FollowedDrafts is { Count: > 0 } followed && TryGetDraftOutcomeEvent(reader, followed, out logEvent))
        {
            return true;
        }

        return TryGetResetEvent(reader, out logEvent)
            || TryGetTrimEvent(reader, out logEvent)
            || TryGetEntryEvent(reader, out logEvent)
            || TryGetSealedEvent(reader, out logEvent)
            || (reader.IncludesDrafts && _drafts is { Open.Count: > 0 } drafts && TryGetDraftItemEvent(reader, drafts, out logEvent));
    }

    private bool TryGetResetEvent(Reader reader, out LogEvent<T> logEvent)
    {
        var isOtherHistory = reader.Epoch != _epoch && reader.Epoch != 0;
        reader.Epoch = _epoch;
        if (isOtherHistory)
        {
            // 读取期间如果历史发生变更，读取开始时已提交的条目便不再存在。
            if (_epoch != reader.CatchUpEpoch)
            {
                reader.CatchUpSequence = 0;
            }

            logEvent = ResetReader(reader);
            return true;
        }

        if (reader.Cursor > NextSequence || reader.Cursor < _committed.ResetSequence)
        {
            logEvent = ResetReader(reader);
            return true;
        }

        logEvent = default;
        return false;
    }

    private bool TryGetTrimEvent(Reader reader, out LogEvent<T> logEvent)
    {
        var firstSequence = _committed.FirstSequence;
        if (firstSequence > reader.KnownFirstSequence)
        {
            reader.KnownFirstSequence = firstSequence;
            reader.Cursor = Math.Max(reader.Cursor, firstSequence);
            logEvent = new() { Kind = LogEventKind.Trimmed, Sequence = firstSequence };
            return true;
        }

        logEvent = default;
        return false;
    }

    private bool TryGetEntryEvent(Reader reader, out LogEvent<T> logEvent)
    {
        if (!reader.IsCaughtUp && reader.Cursor >= reader.CatchUpSequence)
        {
            reader.IsCaughtUp = true;
            logEvent = new() { Kind = LogEventKind.CaughtUp, Sequence = reader.Cursor };
            return true;
        }

        // 位于尚未提交的裁剪位置之前的读取方等待裁剪写入完成。
        if (reader.Cursor >= _firstSequence && reader.Cursor < _committed.NextSequence)
        {
            var sequence = reader.Cursor++;
            logEvent = new() { Kind = LogEventKind.Entry, Sequence = sequence, Item = _items[(int)(sequence - _firstSequence)] };
            return true;
        }

        logEvent = default;
        return false;
    }

    private bool TryGetSealedEvent(Reader reader, out LogEvent<T> logEvent)
    {
        if (_committed.IsSealed && reader.Cursor >= _committed.NextSequence)
        {
            Debug.Assert(reader.IsCaughtUp && _drafts is not { Open.Count: > 0 });
            logEvent = new() { Kind = LogEventKind.Sealed, Sequence = _committed.NextSequence };
            return true;
        }

        logEvent = default;
        return false;
    }

    // 将读取方重新定位到日志历史的首个已提交条目。
    private LogEvent<T> ResetReader(Reader reader)
    {
        reader.Cursor = _committed.FirstSequence;
        reader.KnownFirstSequence = _committed.FirstSequence;
        return new() { Kind = LogEventKind.Reset, Sequence = _committed.FirstSequence };
    }

    private bool TryGetDraftOutcomeEvent(Reader reader, List<FollowedDraft> followed, out LogEvent<T> logEvent)
    {
        for (var i = 0; i < followed.Count; i++)
        {
            var followedDraft = followed[i];
            if (!followedDraft.TryGetOutcome(out var outcome))
            {
                continue;
            }

            if (outcome.IsCommitted && outcome.Epoch != _epoch)
            {
                // 删除持久化日志已丢弃已提交的条目及其历史中的其他内容。
                outcome = DraftOutcome.Abandoned(LogDraftAbandonReason.JournalDeleted, message: null);
            }

            if (outcome.IsCommitted)
            {
                // 一旦包含条目的提交记录已持久化，读取方便会得知该提交。
                if (outcome.CommitIndex > _committed.DraftCommitCount)
                {
                    continue;
                }

                followed.RemoveAt(i);
                logEvent = new()
                {
                    Kind = LogEventKind.DraftCommitted,
                    Epoch = outcome.Epoch,
                    Sequence = outcome.Sequence,
                    Count = outcome.Count,
                    DraftId = followedDraft.Id,
                };
                return true;
            }

            followed.RemoveAt(i);
            logEvent = new()
            {
                Kind = LogEventKind.DraftAbandoned,
                DraftId = followedDraft.Id,
                AbandonReason = outcome.AbandonReason,
                Message = outcome.Message,
            };
            return true;
        }

        logEvent = default;
        return false;
    }

    private static bool TryGetDraftItemEvent(Reader reader, DraftTable drafts, out LogEvent<T> logEvent)
    {
        var followed = reader.FollowedDrafts ??= [];
        foreach (var draft in drafts.Open)
        {
            if (!IsFollowed(followed, draft))
            {
                followed.Add(new FollowedDraft(draft));
            }
        }

        foreach (var followedDraft in followed)
        {
            if (followedDraft.Draft is { IsOpen: true } draft && followedDraft.Position < draft.Count)
            {
                var position = followedDraft.Position++;
                logEvent = new()
                {
                    Kind = LogEventKind.DraftItem,
                    Sequence = position,
                    Item = draft.GetItem(position),
                    DraftId = draft.Id,
                };
                return true;
            }
        }

        logEvent = default;
        return false;

        static bool IsFollowed(List<FollowedDraft> followed, Draft draft)
        {
            foreach (var followedDraft in followed)
            {
                if (ReferenceEquals(followedDraft.Draft, draft))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 某一时刻的日志边界：条目的结束与起始位置、最近一次清空的起始位置、日志是否
    /// 已封存，以及其持有的草稿提交记录数量。
    /// </summary>
    private readonly record struct Boundary(long NextSequence, long FirstSequence, long ResetSequence, bool IsSealed, long DraftCommitCount);

    /// <summary>
    /// 日志保留的草稿提交，以及它在此实例的提交记录中的编号。
    /// </summary>
    private readonly record struct DraftCommitRecord(LogDraftCommit Commit, long Index);

    /// <summary>
    /// 草稿的关闭方式：用于替换它的条目范围，或其被放弃的原因。
    /// </summary>
    private readonly record struct DraftOutcome(
        long Epoch,
        long Sequence,
        int Count,
        long CommitIndex,
        LogDraftAbandonReason AbandonReason,
        string? Message)
    {
        public bool IsCommitted => AbandonReason is LogDraftAbandonReason.None;

        public static DraftOutcome Committed(long epoch, DraftCommitRecord record) =>
            new(epoch, record.Commit.Sequence, record.Commit.Count, record.Index, LogDraftAbandonReason.None, Message: null);

        public static DraftOutcome Abandoned(LogDraftAbandonReason reason, string? message) => new(0, 0, 0, 0, reason, message);
    }

    /// <summary>
    /// 一个日志实例中打开的草稿，以及最近被放弃的草稿的关闭原因。
    /// </summary>
    private sealed class DraftTable
    {
        // 重新连接的读取方可获知此数量的近期已放弃草稿的关闭原因。
        private const int MaxRecentAbandonments = 16;

        private readonly Queue<(long Number, DraftOutcome Outcome)> _recentAbandonments = new();

        public Guid InstanceId { get; } = Guid.NewGuid();

        public long LastNumber { get; set; }

        public List<Draft> Open { get; } = [];

        public void RecordAbandonment(long number, DraftOutcome outcome)
        {
            if (_recentAbandonments.Count == MaxRecentAbandonments)
            {
                _recentAbandonments.Dequeue();
            }

            _recentAbandonments.Enqueue((number, outcome));
        }

        public bool TryGetAbandonment(long number, out DraftOutcome outcome)
        {
            foreach (var (abandonedNumber, abandonedOutcome) in _recentAbandonments)
            {
                if (abandonedNumber == number)
                {
                    outcome = abandonedOutcome;
                    return true;
                }
            }

            outcome = default;
            return false;
        }
    }

    private sealed class Draft(DurableLog<T> log, LogDraftId id) : IDurableLogDraft<T>
    {
        private List<T>? _items = [];

        public LogDraftId Id { get; } = id;

        public bool IsOpen => _items is not null;

        public int Count => _items?.Count ?? 0;

        public DraftOutcome Outcome { get; private set; }

        public T GetItem(int position) => _items![position];

        public void Write(T item)
        {
            ThrowIfClosed();
            _items!.Add(item);
            log.SignalDraftReaders();
        }

        public long Commit(params ReadOnlySpan<T> entries)
        {
            ThrowIfClosed();
            var outcome = log.CommitDraft(Id, entries);
            log.CloseDraft(this, outcome);
            return outcome.Sequence;
        }

        public void Abandon(string? message = null)
        {
            if (IsOpen)
            {
                log.CloseDraft(this, DraftOutcome.Abandoned(LogDraftAbandonReason.Abandoned, message));
            }
        }

        public void Dispose()
        {
            if (IsOpen)
            {
                log.CloseDraft(this, DraftOutcome.Abandoned(LogDraftAbandonReason.Disposed, message: null));
            }
        }

        internal void Close(DraftOutcome outcome)
        {
            Outcome = outcome;
            _items = null;
        }

        private void ThrowIfClosed()
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException(Outcome.AbandonReason switch
                {
                    LogDraftAbandonReason.None => $"Draft {Id} is closed because it was committed.",
                    LogDraftAbandonReason.Abandoned => $"Draft {Id} is closed because it was abandoned.",
                    LogDraftAbandonReason.Disposed => $"Draft {Id} is closed because it was disposed.",
                    _ => $"Draft {Id} was closed by the log because {DescribeLogClosure(Outcome.AbandonReason)}. "
                        + "Check IsOpen before using a draft which the log can close.",
                });
            }
        }

        private static string DescribeLogClosure(LogDraftAbandonReason reason) => reason switch
        {
            LogDraftAbandonReason.JournalDeleted => "the log's journal was deleted",
            LogDraftAbandonReason.Sealed => "the log was sealed",
            _ => reason.ToString(),
        };
    }

    private sealed class Reader(JournaledStateObservers hub, DurableLog<T> log, bool includeDrafts, long cursor, long epoch, long catchUpSequence)
        : StateWatcher<LogEvent<T>>(hub, log)
    {
        private bool _isEnded;

        public override bool IncludesDrafts => includeDrafts;

        public long Cursor { get; set; } = cursor;

        // 读取方位置所属历史的世代标识；若读取方未观察到历史，则为零。
        public long Epoch { get; set; } = epoch;

        // 已通知读取方的首个保留序列号。更大的首个序列号作为裁剪报告。
        public long KnownFirstSequence { get; set; }

        // 读取开始时已提交条目之后的位置，以及这些条目所属历史的世代标识。
        public long CatchUpSequence { get; set; } = catchUpSequence;

        public long CatchUpEpoch { get; } = log._epoch;

        public bool IsCaughtUp { get; set; }

        public List<FollowedDraft>? FollowedDrafts { get; set; }

        public override bool TryRead(out LogEvent<T> value)
        {
            Hub.AssertConfined();
            if (_isEnded || !log.TryGetNextEvent(this, out value))
            {
                value = default;
                return false;
            }

            // 提交范围属于该提交的历史；提交事件可能先于读取方进入该历史的 Reset 事件。
            if (value.Kind is not LogEventKind.DraftCommitted)
            {
                value = value with { Epoch = Epoch };
            }
            if (value.Kind is LogEventKind.Sealed)
            {
                // Sealed 是流的最终事件。
                _isEnded = true;
                Complete(failure: null);
            }

            return true;
        }
    }

    /// <summary>
    /// 读取方跟踪的草稿：打开的草稿，或读取方恢复跟踪之前已关闭草稿的结果。
    /// </summary>
    private sealed class FollowedDraft
    {
        private readonly DraftOutcome _outcome;

        public FollowedDraft(Draft draft)
        {
            Draft = draft;
            Id = draft.Id;
        }

        public FollowedDraft(LogDraftId id, DraftOutcome outcome)
        {
            Id = id;
            _outcome = outcome;
        }

        public LogDraftId Id { get; }

        public Draft? Draft { get; }

        public int Position { get; set; }

        public bool TryGetOutcome(out DraftOutcome outcome)
        {
            if (Draft is { } draft)
            {
                outcome = draft.Outcome;
                return !draft.IsOpen;
            }

            outcome = _outcome;
            return true;
        }
    }
}
