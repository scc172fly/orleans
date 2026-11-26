using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 交付承诺从一名日报国家经理改为其观察者。
/// </summary>
/// <remarks>
/// <para>
/// 管理员在状态首次被监视时最多创建一个枢纽。 监视器由国家机器键制
/// 以“用户”的版本标记每个修改
/// 将记录它。 每个确认的书写都公布一个水印,
/// 以等或低版本标记的修改,观察者只提供承诺的更改。
/// </para>
/// <para>
/// 中心站及其观察者仅限于建立中心的调度器: 启动自控的调度器,
/// 代码 grain 、 经理的工作环和观察点数轮流。 因此, 他们的状况使用普通字段 。
/// <see cref="StateWatcher.Signal"/> 是唯一一个其他线条从取消回调中呼叫的成员。
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
    /// 吸引观察者 <paramref name="state"/>,如果它被监视。
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
/// 观察一个州立机器事件的人
/// </summary>
internal sealed class StateTopic(JournaledStateObservers hub)
{
    private readonly List<StateWatcher> _watchers = [];

    public bool IsEmpty => _watchers.Count == 0;

    /// <summary>
    /// 显示一个变化, 下一期日记一旦确认该写作后, 就会写入抓取 。
    /// </summary>
    public void PublishStaged<TChange>(TChange change) => Publish(change, hub.Manager.StagingWriteVersion);

    /// <summary>
    /// 带来反映持久状态的改变,如被恢复或删除状态。
    /// </summary>
    /// <remarks>
    /// 持久变化公布,而没有记录下来的文字等待确认,因此就承诺了。
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
    /// 所有的观察者都直接读国家
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
    /// 直接读出国家并传达不稳定的内容草案的观察者们都惊醒了。
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
/// 接收发布到 <see cref="StateTopic"/>的更改。
/// </summary>
internal interface IChangeSink<TChange>
{
    void OnChange(TChange change, long version);
}

/// <summary>
/// 一位看电视的州立机器的读者
/// </summary>
/// <remarks>成员以外的成员国 <see cref="Signal"/> 限于中心调度器内。</remarks>
internal abstract class StateWatcher(JournaledStateObservers hub, IStateMachine state)
{
    private readonly SingleWaiterAutoResetEvent _signal = new() { RunContinuationsAsynchronously = true };
    private Exception? _failure;
    private bool _isCompleted;

    public JournaledStateObservers Hub { get; } = hub;

    public IStateMachine State { get; } = state;

    /// <summary>
    /// 获得一个值, 显示观察者是否提供不稳定的内容草稿 。
    /// </summary>
    public virtual bool IncludesDrafts => false;

    protected long CommittedVersion { get; set; }

    public void Signal() => _signal.Signal();

    public ValueTask WaitAsync() => _signal.WaitAsync();

    /// <summary>
    /// 当中心站完成后返回 <see langword="true"/> ;如果失败完成,则丢弃。
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
/// 提供类型 <typeparamref name="TEvent"/>的事件的监视器。
/// </summary>
internal abstract class StateWatcher<TEvent>(JournaledStateObservers hub, IStateMachine state) : StateWatcher(hub, state)
{
    /// <summary>
    /// 阅读下个交付品事件。
    /// </summary>
    public abstract bool TryRead(out TEvent value);
}

/// <summary>
/// 持有价值表的最新承诺值,最多保留两个等待确认的值。
/// </summary>
internal sealed class ValueWatcher<T> : StateWatcher<ValueChange<T>>, IChangeSink<T>
{
    private T? _value;
    private long _version;
    private bool _hasUnreadValue;

    // 等待他们写作确认的值。 信件被确认为有序, 一次捕获, 所以
    // 待处理值最多包括飞行中版本和下一版本。
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
            // 持久价值取代等待确认的价值。
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
/// 将国家执行与管理人员的观察员中心连接。
/// </summary>
internal static class DurableStateObservation
{
    /// <summary>
    /// 获取 <paramref name="state"/>的监视者。返回 <see langword="false"/> ,在未观察时不分配。
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
                // 下一篇作品捕捉到肮脏价值, 飞行中记录的价值一旦被持久
                // 已经确认写作。 否则, 无论是否在运行中, 该值已经是耐久的了 。
                var manager = hub.Manager;
                var initialVersion = state.IsDirty
                    ? manager.StagingWriteVersion
                    : state.IsValueCaptured ? manager.CapturedWriteVersion : manager.CommittedWriteVersion;
                return new ValueWatcher<T>(hub, state, state.Value, initialVersion);
            },
            cancellationToken);

    /// <summary>
    /// 当查点开始并交付其活动时, 创建一个监视器, 直到中心完成或标记取消 。
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
