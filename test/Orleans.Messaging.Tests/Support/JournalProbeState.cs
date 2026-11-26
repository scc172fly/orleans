using Orleans.Durable.Journaling;

namespace Orleans.Durable.Messaging.Tests.Support;

// 空日记状态, 该状态允许测试失败的日记状态, 它让测试失败写入准备或观察写确认。 它从不分阶段
// 更改, 所以它不会隐藏处理器的改变 从检测失败。
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
