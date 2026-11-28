using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 将一个日志状态管理器的已提交变更投递给其观察者。
/// </summary>
/// <remarks>
/// <para>
/// 每个管理器最多创建一个中心，在首次观察其状态时创建。观察者以状态机
/// 实例为键，因此无人观察的状态不会发布任何内容。每次变更都带有将捕获它的
/// 日志写入的版本标记。每次已确认写入发布一个水位标记，提交所有
/// 标记版本不高于该版本的变更，观察者只投递已提交变更。
/// </para>
/// <para>
/// 中心及其观察者局限于创建该中心的调度器，即所属激活实例的调度器；
/// Grain 代码、管理器的工作循环和观察枚举在其上轮流执行，因此其状态使用普通字段。
/// 其他线程仅从取消回调调用 <see cref="StateWatcher.Signal"/> 这一个成员。
/// </para>
/// </remarks>
internal sealed class JournaledStateObservers
{
    private readonly Dictionary<IStateMachine, StateTopic> _topics = new(ReferenceEqualityComparer.Instance);
    private readonly TaskScheduler _scheduler = TaskScheduler.Current;
    private long _committedVersion;
    private Exception? _failure;
    private bool _isCompleted;

    public JournaledStateObservers(JournaledStateManager manager, long committedVersion)
    {
        Manager = manager;
        _committedVersion = committedVersion;
    }

    public JournaledStateManager Manager { get; }

    [Conditional("DEBUG")]
    public void AssertConfined() =>
        Debug.Assert(TaskScheduler.Current == _scheduler, "Observation state is confined to the scheduler which created it.");

    /// <summary>
    /// 获取 <paramref name="state"/> 的观察者（如果它正在被观察）。
    /// </summary>
    public bool TryGetTopic(IStateMachine state, [NotNullWhen(true)] out StateTopic? topic)
    {
        AssertConfined();
        return _topics.TryGetValue(state, out topic);
    }

    public void Add(StateWatcher watcher)
    {
        AssertConfined();
        if (_isCompleted)
        {
            watcher.Attach(_committedVersion);
            watcher.Complete(_failure);
            return;
        }

        if (!_topics.TryGetValue(watcher.State, out var topic))
        {
            topic = new StateTopic(this);
            _topics.Add(watcher.State, topic);
        }

        topic.Add(watcher);
        watcher.Attach(_committedVersion);
    }

    public void Remove(StateWatcher watcher)
    {
        AssertConfined();
        if (_topics.TryGetValue(watcher.State, out var topic) && topic.Remove(watcher) && topic.IsEmpty)
        {
            _topics.Remove(watcher.State);
        }
    }

    public void OnWriteCommitted(long version)
    {
        AssertConfined();
        Debug.Assert(version > _committedVersion);
        _committedVersion = version;
        foreach (var topic in _topics.Values)
        {
            topic.OnWatermark(version);
        }
    }

    public void Complete(Exception? failure)
    {
        AssertConfined();
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        _failure = failure;
        foreach (var topic in _topics.Values)
        {
            topic.Complete(failure);
        }
    }
}

/// <summary>
/// 单个状态机实例的观察者。
/// </summary>
internal sealed class StateTopic(JournaledStateObservers hub)
{
    private readonly List<StateWatcher> _watchers = [];

    public bool IsEmpty => _watchers.Count == 0;

    /// <summary>
    /// 在捕获某项变更的下一次日志写入得到确认后，投递该变更。
    /// </summary>
    public void PublishStaged<TChange>(TChange change) => Publish(change, hub.Manager.StagingWriteVersion);

    /// <summary>
    /// 投递反映持久化状态的变更，例如已恢复或已删除的状态。
    /// </summary>
    /// <remarks>
    /// 持久化变更在没有已捕获写入等待确认时发布，因此它们已提交。
    /// </remarks>
    public void PublishDurable<TChange>(TChange change)
    {
        Debug.Assert(hub.Manager.CapturedWriteVersion == hub.Manager.CommittedWriteVersion, "Durable changes are published while no write is in flight.");
        Publish(change, hub.Manager.CapturedWriteVersion);
    }

    private void Publish<TChange>(TChange change, long version)
    {
        hub.AssertConfined();
        foreach (var watcher in _watchers)
        {
            ((IChangeSink<TChange>)watcher).OnChange(change, version);
        }
    }

    /// <summary>
    /// 唤醒每个直接读取状态的观察者。
    /// </summary>
    public void SignalAll()
    {
        hub.AssertConfined();
        foreach (var watcher in _watchers)
        {
            watcher.Signal();
        }
    }

    /// <summary>
    /// 唤醒直接读取状态并投递易失性草稿内容的观察者。
    /// </summary>
    public void SignalDraftReaders()
    {
        hub.AssertConfined();
        foreach (var watcher in _watchers)
        {
            if (watcher.IncludesDrafts)
            {
                watcher.Signal();
            }
        }
    }

    internal void Add(StateWatcher watcher) => _watchers.Add(watcher);

    internal bool Remove(StateWatcher watcher) => _watchers.Remove(watcher);

    internal void OnWatermark(long version)
    {
        foreach (var watcher in _watchers)
        {
            watcher.OnWatermark(version);
        }
    }

    internal void Complete(Exception? failure)
    {
        foreach (var watcher in _watchers)
        {
            watcher.Complete(failure);
        }
    }
}

/// <summary>
/// 接收发布到 <see cref="StateTopic"/> 的变更。
/// </summary>
internal interface IChangeSink<TChange>
{
    void OnChange(TChange change, long version);
}

/// <summary>
/// 被观察状态机的一个读取方。
/// </summary>
/// <remarks>除 <see cref="Signal"/> 以外的成员均局限于中心的调度器。</remarks>
internal abstract class StateWatcher(JournaledStateObservers hub, IStateMachine state)
{
    private readonly SingleWaiterAutoResetEvent _signal = new() { RunContinuationsAsynchronously = true };
    private Exception? _failure;
    private bool _isCompleted;

    public JournaledStateObservers Hub { get; } = hub;

    public IStateMachine State { get; } = state;

    /// <summary>
    /// 获取一个值，指示观察者是否投递易失性草稿内容。
    /// </summary>
    public virtual bool IncludesDrafts => false;

    protected long CommittedVersion { get; set; }

    public void Signal() => _signal.Signal();

    public ValueTask WaitAsync() => _signal.WaitAsync();

    /// <summary>
    /// 中心完成时返回 <see langword="true"/>；若中心因失败而完成，则抛出异常。
    /// </summary>
    public bool CheckCompleted()
    {
        Hub.AssertConfined();
        if (!_isCompleted)
        {
            return false;
        }

        if (_failure is { } failure)
        {
            throw new InvalidOperationException(
                "The journaled state manager stopped after a failed operation. A new activation recovers the durable state.",
                failure);
        }

        return true;
    }

    internal void Attach(long committedVersion)
    {
        CommittedVersion = committedVersion;
        OnAttached();
    }

    internal virtual void OnWatermark(long version)
    {
        CommittedVersion = version;
        Signal();
    }

    internal void Complete(Exception? failure)
    {
        _isCompleted = true;
        _failure = failure;
        Signal();
    }

    protected virtual void OnAttached()
    {
    }
}

/// <summary>
/// 投递 <typeparamref name="TEvent"/> 类型事件的观察者。
/// </summary>
internal abstract class StateWatcher<TEvent>(JournaledStateObservers hub, IStateMachine state) : StateWatcher(hub, state)
{
    /// <summary>
    /// 读取下一个可投递事件。
    /// </summary>
    public abstract bool TryRead(out TEvent value);
}

/// <summary>
/// 持有值观察的最新已提交值，最多保留两个等待确认的值。
/// </summary>
internal sealed class ValueWatcher<T> : StateWatcher<ValueChange<T>>, IChangeSink<T>
{
    private T? _value;
    private long _version;
    private bool _hasUnreadValue;

    // 等待各自写入确认的值。写入按顺序逐一捕获、逐一确认，因此
    // 待提交值最多跨越在途版本和下一版本。
    private T? _pendingValue0;
    private long _pendingVersion0;
    private T? _pendingValue1;
    private long _pendingVersion1;
    private int _pendingCount;

    public ValueWatcher(JournaledStateObservers hub, IStateMachine state, T? initialValue, long initialVersion)
        : base(hub, state)
    {
        _pendingValue0 = initialValue;
        _pendingVersion0 = initialVersion;
    }

    protected override void OnAttached()
    {
        var initialValue = _pendingValue0;
        var initialVersion = _pendingVersion0;
        _pendingValue0 = default;
        OnChange(initialValue!, initialVersion);
    }

    public void OnChange(T value, long version)
    {
        if (version <= CommittedVersion)
        {
            // 已持久化值取代等待确认的值。
            _pendingCount = 0;
            (_pendingValue0, _pendingValue1) = (default, default);
            SetLatest(value, version);
            return;
        }

        if (_pendingCount > 0 && (_pendingCount == 1 ? _pendingVersion0 : _pendingVersion1) == version)
        {
            if (_pendingCount == 1)
            {
                _pendingValue0 = value;
            }
            else
            {
                _pendingValue1 = value;
            }

            return;
        }

        Debug.Assert(_pendingCount < 2, "Pending values span at most two write versions.");
        if (_pendingCount == 0)
        {
            (_pendingValue0, _pendingVersion0) = (value, version);
        }
        else
        {
            (_pendingValue1, _pendingVersion1) = (value, version);
        }

        _pendingCount++;
    }

    internal override void OnWatermark(long version)
    {
        CommittedVersion = version;
        while (_pendingCount > 0 && _pendingVersion0 <= version)
        {
            SetLatest(_pendingValue0!, _pendingVersion0);
            (_pendingValue0, _pendingVersion0) = (_pendingValue1, _pendingVersion1);
            (_pendingValue1, _pendingVersion1) = (default, 0);
            _pendingCount--;
        }
    }

    public override bool TryRead(out ValueChange<T> change)
    {
        Hub.AssertConfined();
        if (!_hasUnreadValue)
        {
            change = default;
            return false;
        }

        Debug.Assert(_version <= CommittedVersion, "Value watches deliver only committed values.");
        _hasUnreadValue = false;
        change = new() { Value = _value, Version = _version };
        return true;
    }

    private void SetLatest(T value, long version)
    {
        _value = value;
        _version = version;
        _hasUnreadValue = true;
        Signal();
    }
}

/// <summary>
/// 将持久化状态实现连接到所属管理器的观察者中心。
/// </summary>
internal static class DurableStateObservation
{
    /// <summary>
    /// 获取 <paramref name="state"/> 的观察者。未被观察时返回 <see langword="false"/>，且不分配内存。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetTopic(JournaledStateManager? manager, IStateMachine state, [NotNullWhen(true)] out StateTopic? topic)
    {
        if (manager?.Observers is { } observers)
        {
            return observers.TryGetTopic(state, out topic);
        }

        topic = null;
        return false;
    }

    public static JournaledStateManager GetRequiredManager(JournaledStateManager? manager) =>
        manager ?? throw new NotSupportedException("Watching requires state registered with the built-in journaled state manager.");

    public static IAsyncEnumerable<ValueChange<T>> WatchValue<T>(
        JournaledStateManager? manager,
        DurableValue<T> state,
        CancellationToken cancellationToken) =>
        WatchAsync(
            GetRequiredManager(manager),
            state,
            static (hub, state) =>
            {
                // 脏值由下一次写入捕获；在途写入捕获的值在该次
                // 写入确认后持久化。除此之外，该值已经持久化，无论是否有不相关的在途写入。
                var manager = hub.Manager;
                var initialVersion = state.IsDirty
                    ? manager.StagingWriteVersion
                    : state.IsValueCaptured ? manager.CapturedWriteVersion : manager.CommittedWriteVersion;
                return new ValueWatcher<T>(hub, state, state.Value, initialVersion);
            },
            cancellationToken);

    /// <summary>
    /// 在枚举开始时创建观察者，并投递其事件，直到中心完成或令牌取消。
    /// </summary>
    public static async IAsyncEnumerable<TEvent> WatchAsync<TArg, TEvent>(
        JournaledStateManager manager,
        TArg arg,
        Func<JournaledStateObservers, TArg, StateWatcher<TEvent>> createWatcher,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hub = manager.GetOrCreateObservers();
        var watcher = createWatcher(hub, arg);
        hub.Add(watcher);
        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.UnsafeRegister(static state => ((StateWatcher)state!).Signal(), watcher)
            : default;
        try
        {
            while (true)
            {
                while (watcher.TryRead(out var value))
                {
                    yield return value;
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (watcher.CheckCompleted())
                {
                    yield break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await watcher.WaitAsync();
            }
        }
        finally
        {
            hub.Remove(watcher);
        }
    }
}
