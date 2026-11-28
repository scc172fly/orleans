using Orleans.Durable.Journaling;

namespace Orleans.Durable.Messaging.Tests.Support;

// 空日志状态，使测试能够令写入准备失败或观察写入确认。该状态从不暂存
// 变更，因此不会使故障检测遗漏处理程序的变更。
internal sealed class JournalProbeState : IStateMachine
{
    public const string Name = "test-journal-probe";

    public JournalProbeState(IJournaledStateManager manager) => manager.RegisterStateMachine(Name, this);

    public Exception? PreparationFailure { get; set; }
    public Action? WriteCompleted { get; set; }

    public ValueTask PrepareWriteAsync(CancellationToken cancellationToken = default) =>
        PreparationFailure is { } failure ? ValueTask.FromException(failure) : default;

    public void OnWriteCompleted() => WriteCompleted?.Invoke();
    public void ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        throw new NotSupportedException("The journal probe has no entries.");
    public void Reset(JournalStreamWriter writer) { }
    public void WritePendingEntries(JournalStreamWriter writer) { }
    public void WriteSnapshot(JournalStreamWriter writer) { }
}
