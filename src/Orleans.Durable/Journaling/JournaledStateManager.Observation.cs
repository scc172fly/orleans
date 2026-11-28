using System.Diagnostics;

namespace Orleans.Durable.Journaling;

internal partial class JournaledStateManager
{
    private JournaledStateObservers? _observers;

    // 写入版本为已捕获的日志写入编号。版本零早于此管理器的首次写入。
    // 每次写入捕获所有状态，写入逐一捕获并确认，因此最多有一个已捕获的
    // 版本等待确认。
    private long _capturedWriteVersion;
    private long _committedWriteVersion;

    /// <summary>
    /// 获取此管理器的观察者中心；尚未观察任何状态时为 <see langword="null"/>。
    /// </summary>
    internal JournaledStateObservers? Observers => _observers;

    /// <summary>
    /// 获取最近一次已捕获写入的版本。
    /// </summary>
    internal long CapturedWriteVersion => _capturedWriteVersion;

    /// <summary>
    /// 获取最近一次已确认写入的版本。
    /// </summary>
    internal long CommittedWriteVersion => _committedWriteVersion;

    /// <summary>
    /// 获取将捕获当前暂存变更的写入版本。
    /// </summary>
    internal long StagingWriteVersion => _capturedWriteVersion + 1;

    /// <summary>
    /// 获取观察者中心，在首个观察者出现时创建它。
    /// </summary>
    /// <remarks>观察要求管理器已恢复，以确保首个快照反映已恢复状态。</remarks>
    /// <exception cref="InvalidOperationException">管理器未初始化，或失败操作已隔离管理器。</exception>
    internal JournaledStateObservers GetOrCreateObservers()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            ThrowIfStateOperationsUnavailable();
            return _observers ??= new JournaledStateObservers(this, _committedWriteVersion);
        }
    }

    // 捕获每个状态的待提交条目后，在持有管理器锁的情况下调用。返回写入版本。
    private long OnWriteCaptured()
    {
        Debug.Assert(_capturedWriteVersion == _committedWriteVersion, "Each captured write is acknowledged before the next capture.");
        return ++_capturedWriteVersion;
    }

    // 每个状态均观察到写入确认后，在持有管理器锁的情况下调用。
    private void OnWriteAcknowledged(long writeVersion)
    {
        Debug.Assert(writeVersion > 0 && writeVersion == _capturedWriteVersion);
        _committedWriteVersion = writeVersion;
        _observers?.OnWriteCommitted(writeVersion);
    }

    private void CompleteObservers(Exception? failure) => _observers?.Complete(failure);
}
