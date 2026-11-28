using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Journaling;

namespace Orleans.Durable.Messaging;

internal sealed partial class Inbox
{
    private readonly Dictionary<MessageId, InboxEntry> _messages = [];
    private readonly Dictionary<MessageId, InboxProcessedEntry> _processed = [];
    private readonly Dictionary<MessageId, InboxDeadLetterRecord> _deadLetters = [];
    private JournalStreamWriter _writer;

    // 遗忘一个标识需要有持久化的下限，使其发送时间被排除在准入范围之外。
    private DateTimeOffset _admissionFloor = DateTimeOffset.MinValue;
    private DateTimeOffset _capturedAdmissionFloor = DateTimeOffset.MinValue;
    private DateTimeOffset _durableAdmissionFloor = DateTimeOffset.MinValue;

    // 接纳与完成操作在已完成的写入捕获其序列号后即已持久化。
    private long _lastSequence;
    private long _capturedSequence;
    private long _durableSequence;
    private long _lastCompletionSequence;
    private bool _capturedSnapshot;

    // 日志中的最后一条收件箱命令是否记录了激活实例在没有中断尝试的情况下停止。
    private bool _stoppedCleanly;

    // 此激活实例是否完成恢复记账，从而允许将其停止记录为干净停止。
    private bool _recovered;

    // 自上次捕获以来暂存的已接纳或正在重试的消息中，按 Durable Jobs 时钟计算的最早到期时间。
    private DateTimeOffset? _stagedWakeup;

    private bool IsDurable(InboxEntry entry) => entry.Sequence <= _durableSequence;

    // 去重所依赖的每条已处理记录是否都已持久化。
    private bool AreCompletionsDurable => _lastCompletionSequence <= _durableSequence;

    // 每次日志写入都会准备所有状态，因此使消息或已注册工作进入待处理状态的写入会在其持久化之前
    // 确保相应唤醒。消息传递失败后，内存状态可能包含不得
    // 提交的变更，例如失败处理器的变更，因此之后的每次写入均失败，并从日志开始恢复。
    ValueTask IStateMachine.PrepareWriteAsync(CancellationToken cancellationToken)
    {
        if (_pump.IsFailed)
        {
            return ValueTask.FromException(new InvalidOperationException(
                "Messaging failed for this activation, so its uncommitted changes are discarded by recovery from the journal."));
        }

        var dueTime = _stagedWakeup;
        if (GetRegisteredWakeupDelay() is { } delay)
        {
            var workDueTime = MessagingTime.AddClamped(Shared.JobTimeProvider.GetUtcNow(), delay);
            if (dueTime is null || workDueTime < dueTime) dueTime = workDueTime;
        }

        return dueTime is { } due ? _pump.EnsureWakeupAsync(due, cancellationToken) : ValueTask.CompletedTask;
    }

    private void OnWriteCaptured(bool snapshot = false)
    {
        _capturedSequence = _lastSequence;
        _capturedAdmissionFloor = _admissionFloor;
        _capturedSnapshot = snapshot;
        _stagedWakeup = null;
    }

    void IStateMachine.OnWriteCompleted()
    {
        _durableSequence = _capturedSequence;
        _durableAdmissionFloor = _capturedAdmissionFloor;
        // 仅追加下限的操作保持常数时间；快照在其准入下限持久化之后回收历史记录。
        if (_capturedSnapshot) CompactProcessed();
        _pump.Signal();
    }

    private DateTimeOffset GetAdmissionFloor(DateTimeOffset now)
    {
        var nowTicks = now.UtcTicks;
        var candidate = new DateTimeOffset(Math.Max(0, nowTicks - Options.DeduplicationWindow.Ticks), TimeSpan.Zero);
        return candidate > _admissionFloor ? candidate : _admissionFloor;
    }

    private static bool IsAtOrBelowFloor(InboxProcessedEntry entry, DateTimeOffset floor) =>
        entry.SentAt <= floor;

    private void CompactProcessed()
    {
        foreach (var (key, entry) in _processed)
        {
            if (entry.Sequence <= _durableSequence && IsAtOrBelowFloor(entry, _durableAdmissionFloor)) _processed.Remove(key);
        }
    }

    private void Emit(InboxCommand command)
    {
        Shared.InboxCodec.WriteSet(command, _writer);
        Apply(command);
        var dueTime = command switch
        {
            InboxCommand.Accept accept => accept.Message.Envelope.DeliverAt ?? DateTimeOffset.MinValue,
            InboxCommand.Retry retry => retry.State.NextAttemptAt,
            _ => null
        };
        if (dueTime is { } due && (_stagedWakeup is null || due < _stagedWakeup)) _stagedWakeup = due;
    }

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
        ReportDepth(-_messages.Count);
        _writer = writer;
        _messages.Clear();
        _processed.Clear();
        _deadLetters.Clear();
        _admissionFloor = _capturedAdmissionFloor = _durableAdmissionFloor = DateTimeOffset.MinValue;
        _stagedWakeup = null;
        _capturedSnapshot = false;
        _stoppedCleanly = false;
        _lastSequence = _capturedSequence = _durableSequence = _lastCompletionSequence = 0;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, Shared.InboxCodec).Apply(entry.Reader, this);

    void IStateMachine.OnRecoveryCompleted()
    {
        // 准入已验证每条消息，重放使用各条目的消息 ID 作为键。
        Debug.Assert(_messages.All(static pair => pair.Key == pair.Value.Message.Envelope.MessageId));
        Debug.Assert(_deadLetters.All(static pair => pair.Key == pair.Value.Message.Envelope.MessageId));

        _capturedSequence = _durableSequence = _lastSequence;
        _capturedAdmissionFloor = _durableAdmissionFloor = _admissionFloor;
        CompactProcessed();
    }

    void IStateMachine.WritePendingEntries(JournalStreamWriter writer) => OnWriteCaptured();

    void IStateMachine.WriteSnapshot(JournalStreamWriter writer)
    {
        // 下限与压实后的历史记录原子提交。确认前保留内存中的记录，
        // 以便在快照的准入下限尚未持久化时，交错执行的投递仍可去重。
        var floor = GetAdmissionFloor(Shared.JobTimeProvider.GetUtcNow());
        Shared.InboxCodec.WriteSet(new InboxCommand.Snapshot(
            _messages.Values.ToArray(),
            _processed.Values.Where(entry => !IsAtOrBelowFloor(entry, floor)).ToArray(),
            _deadLetters.Values.ToArray(),
            floor,
            _stoppedCleanly), writer);
        _admissionFloor = floor;
        OnWriteCaptured(snapshot: true);
    }

    void IDurableValueCommandHandler<InboxCommand>.ApplySet(InboxCommand command) => Apply(command);

    private void Apply(InboxCommand command)
    {
        switch (command)
        {
            case InboxCommand.Accept { Message: var message }:
                _messages.Add(message.Envelope.MessageId, new InboxEntry { Message = message, State = new(), Sequence = ++_lastSequence });
                // 复用的标识开始一个新命令，并替换先前命令的记录。
                _processed.Remove(message.Envelope.MessageId);
                _deadLetters.Remove(message.Envelope.MessageId);
                ReportDepth(1);
                break;
            case InboxCommand.Retry { MessageId: var key, State: var state }:
                _messages[key].State = state;
                break;
            case InboxCommand.Complete { MessageId: var key, SentAt: var sentAt, DeadLetter: var deadLetter, Rejection: var rejection }:
                if (_messages.Remove(key)) ReportDepth(-1);

                _lastCompletionSequence = ++_lastSequence;
                _processed[key] = new() { MessageId = key, SentAt = sentAt, Rejection = rejection, Sequence = _lastSequence };
                if (deadLetter is not null) _deadLetters[key] = deadLetter;
                else _deadLetters.Remove(key);
                break;
            case InboxCommand.AdvanceAdmissionFloor { Floor: var floor }:
                AdvanceAdmissionFloor(floor);
                break;
            case InboxCommand.RemoveDeadLetters { Keys: var keys }:
                foreach (var key in keys) _deadLetters.Remove(key);
                break;
            case InboxCommand.CleanStop or InboxCommand.Resume:
                break;
            case InboxCommand.Snapshot snapshot:
                AdvanceAdmissionFloor(snapshot.AdmissionFloor);
                var previousCount = _messages.Count;
                _messages.Clear();
                _processed.Clear();
                _deadLetters.Clear();
                foreach (var restored in snapshot.Messages) _messages.Add(restored.Message.Envelope.MessageId, restored);

                ReportDepth(_messages.Count - previousCount);

                foreach (var processed in snapshot.Processed) _processed.Add(processed.MessageId, processed);
                foreach (var restored in snapshot.DeadLetters) _deadLetters.Add(restored.Message.Envelope.MessageId, restored);
                break;
        }

        _stoppedCleanly = command switch
        {
            InboxCommand.Snapshot snapshot => snapshot.StoppedCleanly,
            InboxCommand.CleanStop => true,
            _ => false
        };
    }
    private void AdvanceAdmissionFloor(DateTimeOffset floor)
    {
        if (floor < _admissionFloor) throw new InvalidOperationException("The inbox admission floor cannot move backward.");
        _admissionFloor = floor;
    }

}

/// <summary>收件箱日志状态的一项变更。</summary>
/// <remarks>
/// 分支标识符遵循声明顺序，是日志格式的一部分。JSON 中的每个分支均以其
/// <c>kind</c> 开头，<see cref="InboxCommandClassifier"/> 读取该值以选择分支。
/// </remarks>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand")]
[JsonUnion(TypeClassifier = typeof(InboxCommandClassifier))]
internal union InboxCommand(
    InboxCommand.Accept,
    InboxCommand.Retry,
    InboxCommand.Complete,
    InboxCommand.RemoveDeadLetters,
    InboxCommand.Snapshot,
    InboxCommand.AdvanceAdmissionFloor,
    InboxCommand.CleanStop,
    InboxCommand.Resume)
{
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.Accept")]
    internal sealed class Accept(InboxMessage message)
    {
        [Id(0)] public InboxMessage Message { get; } = message;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "accept";
    }

    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.Retry")]
    internal sealed class Retry(MessageId messageId, InboxMessageState state)
    {
        [Id(0)] public MessageId MessageId { get; } = messageId;
        [Id(1)] public InboxMessageState State { get; } = state;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "retry";
    }

    /// <summary>记录消息的已处理结果，替换其此前的记录及死信。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.Complete")]
    internal sealed class Complete(MessageId messageId, DateTimeOffset sentAt, InboxDeadLetterRecord? deadLetter, InboxRejection? rejection)
    {
        [Id(0)] public MessageId MessageId { get; } = messageId;
        [Id(1)] public DateTimeOffset SentAt { get; } = sentAt;
        [Id(2)] public InboxDeadLetterRecord? DeadLetter { get; } = deadLetter;
        [Id(3)] public InboxRejection? Rejection { get; } = rejection;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "complete";
    }

    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.RemoveDeadLetters")]
    internal sealed class RemoveDeadLetters(MessageId[] keys)
    {
        [Id(0)] public MessageId[] Keys { get; } = keys;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "removeDeadLetters";
    }

    /// <summary>替换收件箱的状态。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.Snapshot")]
    internal sealed class Snapshot(InboxEntry[] messages, InboxProcessedEntry[] processed, InboxDeadLetterRecord[] deadLetters, DateTimeOffset admissionFloor, bool stoppedCleanly)
    {
        [Id(0)] public InboxEntry[] Messages { get; } = messages;
        [Id(1)] public InboxProcessedEntry[] Processed { get; } = processed;
        [Id(2)] public InboxDeadLetterRecord[] DeadLetters { get; } = deadLetters;
        [Id(3)] public DateTimeOffset AdmissionFloor { get; } = admissionFloor;
        [Id(4)] public bool StoppedCleanly { get; } = stoppedCleanly;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "snapshot";
    }

    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.AdvanceAdmissionFloor")]
    internal sealed class AdvanceAdmissionFloor(DateTimeOffset floor)
    {
        [Id(0)] public DateTimeOffset Floor { get; } = floor;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "advanceAdmissionFloor";
    }

    /// <summary>记录激活实例停止时未中断任何处理器尝试，因此恢复时不计入失败尝试。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.CleanStop")]
    internal sealed class CleanStop
    {
        public static CleanStop Instance { get; } = new();

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "cleanStop";
    }

    /// <summary>记录已恢复的激活实例消耗了干净停止记录，以便后续非干净停止计入失败尝试。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.Resume")]
    internal sealed class Resume
    {
        public static Resume Instance { get; } = new();

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "resume";
    }
}

/// <summary>选择一个 <see cref="InboxCommand"/> 分支，依据是每个 JSON 分支开头的 <c>kind</c>。</summary>
internal sealed class InboxCommandClassifier : JsonTypeClassifierFactory
{
    public override bool CanClassify(JsonTypeClassifierContext context) => context.DeclaringType == typeof(InboxCommand);

    public override JsonTypeClassifier CreateJsonClassifier(JsonTypeClassifierContext context, JsonSerializerOptions options) => Classify;

    private static Type? Classify(ref Utf8JsonReader reader)
    {
        var lookahead = reader;
        if (lookahead.TokenType != JsonTokenType.StartObject) return null;
        while (lookahead.Read() && lookahead.TokenType == JsonTokenType.PropertyName)
        {
            var isKind = lookahead.ValueTextEquals("kind"u8);
            lookahead.Read();
            if (isKind)
            {
                return lookahead.GetString() switch
                {
                    "accept" => typeof(InboxCommand.Accept),
                    "retry" => typeof(InboxCommand.Retry),
                    "complete" => typeof(InboxCommand.Complete),
                    "removeDeadLetters" => typeof(InboxCommand.RemoveDeadLetters),
                    "snapshot" => typeof(InboxCommand.Snapshot),
                    "advanceAdmissionFloor" => typeof(InboxCommand.AdvanceAdmissionFloor),
                    "cleanStop" => typeof(InboxCommand.CleanStop),
                    "resume" => typeof(InboxCommand.Resume),
                    _ => null
                };
            }

            lookahead.Skip();
        }

        return null;
    }
}

[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxProcessedEntry")]
internal sealed class InboxProcessedEntry
{
    [Id(0)] public MessageId MessageId { get; init; }
    [Id(2)] public InboxRejection? Rejection { get; init; }
    [Id(3)] public required DateTimeOffset SentAt { get; init; }
    [field: NonSerialized]
    [JsonIgnore] public long Sequence { get; init; }
}

/// <summary>收件箱接收器在投递时拒绝消息所返回的答复。</summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxRejection")]
internal sealed class InboxRejection
{
    [Id(0)] public required string Reason { get; init; }
    [Id(1)] public string? Code { get; init; }
}

[GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxEntry")]
internal sealed class InboxEntry
{
    [Id(0)] public required InboxMessage Message { get; init; }
    [Id(1)] public required InboxMessageState State { get; set; }
    [field: NonSerialized]
    [JsonIgnore] public long Sequence { get; set; }
}

[JsonSerializable(typeof(InboxCommand))]
internal sealed partial class InboxJsonContext : JsonSerializerContext;
