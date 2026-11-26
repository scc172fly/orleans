using System.Diagnostics;

namespace Orleans.Durable.Journaling;

internal partial class JournaledStateManager
{
    private JournaledStateObservers? _observers;

    // 记录到的日记写稿数。 版本零比经理第一次写入提前 。
    // 每一个州每个州都有笔记记录, 写作记录被一个一个地捕获和确认, 所以最多一个地捕获
    // 等待确认
    private long _capturedWriteVersion;
    private long _committedWriteVersion;

    /// <summary>
    /// 获取该经理的观察中心,或者在未监视状态时获取 <see langword="null"/> 。
    /// </summary>
    internal JournaledStateObservers? Observers => _observers;

    /// <summary>
    /// 获取最新获封的版本。
    /// </summary>
    internal long CapturedWriteVersion => _capturedWriteVersion;

    /// <summary>
    /// 获取最新确认的版本。
    /// </summary>
    internal long CommittedWriteVersion => _committedWriteVersion;

    /// <summary>
    /// 获取将显示变化的文字版本, 并进行演示。
    /// </summary>
    internal long StagingWriteVersion => _capturedWriteVersion + 1;

    /// <summary>
    /// 获得观察中心,为第一站的守卫者创建它。
    /// </summary>
    /// <remarks>监视需要一个被回收的经理, 这样第一个快照就能反映被回收的状况。</remarks>
    /// <exception cref="InvalidOperationException">经理没有初始化,或者一个失败的行动给它围上了栅栏。</exception>
    internal JournaledStateObservers GetOrCreateObservers()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            ThrowIfStateOperationsUnavailable();
            return _observers ??= new JournaledStateObservers(this, _committedWriteVersion);
        }
    }

    // 在每个州存有的条目被俘获后, 被调用到管理器锁下。 返回写入版本 。
    private long OnWriteCaptured()
    {
        Debug.Assert(_capturedWriteVersion == _committedWriteVersion, "Each captured write is acknowledged before the next capture.");
        return ++_capturedWriteVersion;
    }

    // 在每个州都看到写作确认后 被调用到经理锁下
    private void OnWriteAcknowledged(long writeVersion)
    {
        Debug.Assert(writeVersion > 0 && writeVersion == _capturedWriteVersion);
        _committedWriteVersion = writeVersion;
        _observers?.OnWriteCommitted(writeVersion);
    }

    private void CompleteObservers(Exception? failure) => _observers?.Complete(failure);
}
