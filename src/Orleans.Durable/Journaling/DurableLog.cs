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

    // 指定历史。 第一次修改前为零, 所以没有时代的日志没有条目也没有密封 。
    private long _epoch;
    private long _firstSequence;

    // 最清楚的后第一个序列。 在它之前的位置上, 读者会看到被替换的条目 。
    private long _resetSequence;
    private bool _isSealed;

    // 记录日志的边界 当最近写作被捕获, 当最新的确认的写作被捕获。
    private Boundary _captured;
    private Boundary _committed;

    // 承诺条目的草稿在第一个保留序列前后开始,按顺序排列。
    private List<DraftCommitRecord>? _draftCommits;

    // 此例的草稿记录记录。 边界的计数显示哪项记录记录了所抓到的笔记, 而
    // 无法显示空当量的条目区域 。
    private long _draftCommitCount;

    // 由第一稿分配。
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

        // 清晰的消耗了它开始的历史的最初序列。
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

    // 条目和命令在制作时会被日记。 获取其中哪个记录可以保持耐用性 。
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

    // 每一个写入记录都捕捉到这个日志, 之后才确认。 经理在此之后公布该写作的水印
    // 召回,它唤醒读者。
    void IStateMachine.OnWriteCompleted() => _committed = _captured;

    void IDurableLogCommandHandler<T>.ApplyAppend(T item)
    {
        ThrowIfMalformedChange("append");
        _items.Add(item);
    }

    void IDurableLogCommandHandler<T>.ApplyDraftCommit(LogDraftCommit commit)
    {
        // 记录的承诺在下一个序列时或之前结束,承诺在上一个序列结束时或之后开始,
        // 所以将承诺的计算和开始后的项目项进行比较, 无法溢出, 因为其结束可以。
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

    // 执行作者强制实施的变数, 执行重放执行: 更改属于历史, 密封日志接受不
    // 条目或清除,而每个附件或清除都消耗一个序列编号,该序列数字不能超过最大序列。
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

    // 历史从历史的时代开始。 没有时代的日志是空的, 所以开始历史的命令会被编辑
    // 以快速显示命令产生状态, 携带新的时代。 从历史和命令开始
    // 然后形成一个日记条目, 编码失败将完全丢弃, 使日志没有时间间隔 。
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

    // 将草稿条目和结果作为一份日刊条目予以日报。
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

    // 清晰使用一个序列编号, 所以读者的位置显示是否观察到被替换的条目 。
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

    // 承诺条目通过写作的水印可见, 使每个读者都醒了。 内容草稿是可见的
    // 写完后马上
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

    // 跟随草案,一个重新连接的读者查看了:项目在开放时打开,否则结果。
    // 承诺授权, 它保留了这些权限, 并同时在激活之间进行输入。 放弃像项目草稿一样是挥发性的 。
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

    // 计算读者从当前日志状态的下一个事件。 在产量之间调用, 所以每次调用都观察到突变
    // 校对按优先顺序进行:
    // 1. 读者随后阅读草稿的结果,以便读者了解他们在历史改变前如何关闭。
    //    其历史被删除后,由于条目已不复存在,被报告为废弃。
    // 2. 历史变化:另一个时代、一个超越终点的位置或清晰,每个方向都以重置重新定位读者。
    // 3. 耐久修补。
    // 4. CaughtUp,然后是下一个承诺输入。
    // 5. 每次入境都密封,完成后即封存。
    // 6. 项目草案。
    // 读者观察分线、清扫和封条,一旦它们耐久,即观察条目。
    private bool TryGetNextEvent(Reader reader, out LogEvent<T> logEvent)
    {
        if (reader.Epoch == 0)
        {
            // 读者没有看到历史, 就会通过日志的历史, 以零顺序开始。
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
            // 当读取时历史发生变化,当读取开始时所执行的条目就不复存在。
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

        // 一位在未完成的剪辑前 被定位的读者等待剪辑稿
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

    // 将读者重新定位到日志历史的第一个承诺条目。
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
                // 将承诺的条目和历史遗迹一起丢弃。
                outcome = DraftOutcome.Abandoned(LogDraftAbandonReason.JournalDeleted, message: null);
            }

            if (outcome.IsCommitted)
            {
                // 读者一旦得知一份包含其条目的记录,即了解承诺,这是持久的。
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
    /// 日志的边界点是:它的条目从哪条条目结束和开始,最近一个清楚的条目从哪条条目开始,它是否在
    /// 并保存多少记录
    /// </summary>
    private readonly record struct Boundary(long NextSequence, long FirstSequence, long ResetSequence, bool IsSealed, long DraftCommitCount);

    /// <summary>
    /// 日志保留的承诺草案及其数量,属于此例承诺记录。
    /// </summary>
    private readonly record struct DraftCommitRecord(LogDraftCommit Commit, long Index);

    /// <summary>
    /// 草案如何结束:取代该草案的条目范围,或为什么放弃草案。
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
    /// 一个日志实例的开放式草稿及其最近废弃的草稿为何关闭。
    /// </summary>
    private sealed class DraftTable
    {
        // 读者重新连结,
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

        // 读者所处位置的历史时代,当读者没有看到历史时,即为零。
        public long Epoch { get; set; } = epoch;

        // 读者被告知第一个保留序列。第一个较高序列作为剪辑报告。
        public long KnownFirstSequence { get; set; }

        // 条目后的位置, 当读取开始时, 以及它们历史的时代 。
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

            // 承诺的范围属于承诺的历史,它可以在读者重置之前。
            if (value.Kind is not LogEventKind.DraftCommitted)
            {
                value = value with { Epoch = Epoch };
            }
            if (value.Kind is LogEventKind.Sealed)
            {
                // 密封是溪流的最后一件事
                _isEnded = true;
                Complete(failure: null);
            }

            return true;
        }
    }

    /// <summary>
    /// 草案由读者编写:开放式草案,或草案在读者恢复之前结束的结果。
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
