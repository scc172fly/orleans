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

    // 忘记一个身份需要持久的底线,排除其发送时间。
    private DateTimeOffset _admissionFloor = DateTimeOffset.MinValue;
    private DateTimeOffset _capturedAdmissionFloor = DateTimeOffset.MinValue;
    private DateTimeOffset _durableAdmissionFloor = DateTimeOffset.MinValue;

    // 一旦完成的写作记录了其序列号,接受和完成是持久的。
    private long _lastSequence;
    private long _capturedSequence;
    private long _durableSequence;
    private long _lastCompletionSequence;
    private bool _capturedSnapshot;

    // 日记中最后一个收件箱命令是否记录激活停止但未中断尝试。
    private bool _stoppedCleanly;

    // 激活是否完成回收簿, 停止可以记录为干净 。
    private bool _recovered;

    // 上一次捕捉以来,在 " 持久工作钟 " 上,接收或重新输入的信息最早应到的时间。
    private DateTimeOffset? _stagedWakeup;

    private bool IsDurable(InboxEntry entry) => entry.Sequence <= _durableSequence;

    // 每一份重复依赖的经过处理的记录是否都具有耐久性。
    private bool AreCompletionsDurable => _lastCompletionSequence <= _durableSequence;

    // 每份期刊的写作都准备每个州,所以在发布信息或登记工作之后进行写作就能确保
    // 信息发送失败后,记忆状态可能引发一定不能改变的变化
    // 进行, 如处理失败的操作器, 所以每以后写出失败, 并从日记开始回收 。
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
        // 仅限楼层的附件保持固定时间;照片在入场底之后重现历史,是耐久的。
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
        // 接收确认所有信息, 并用自己的信息识别符重放每个条目的密钥 。
        Debug.Assert(_messages.All(static pair => pair.Key == pair.Value.Message.Envelope.MessageId));
        Debug.Assert(_deadLetters.All(static pair => pair.Key == pair.Value.Message.Envelope.MessageId));

        _capturedSequence = _durableSequence = _lastSequence;
        _capturedAdmissionFloor = _durableAdmissionFloor = _admissionFloor;
        CompactProcessed();
    }

    void IStateMachine.WritePendingEntries(JournalStreamWriter writer) => OnWriteCaptured();

    void IStateMachine.WriteSnapshot(JournalStreamWriter writer)
    {
        // 地板和紧凑历史以原子形式进行。保存记忆记录直到确认,所以
        // 交接交货仍然很复杂,
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
                // 重新使用身份后,将启动新的命令,取代以前的命令记录。
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

/// <summary>更改收件箱的日记状态。</summary>
/// <remarks>
/// 案件识别特征遵循申报顺序,是日记格式的一部分。 JSON,每个案件都与
/// <c>kind</c>,其 <see cref="InboxCommandClassifier"/> 读作选择案件。
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

    /// <summary>记录信息处理的结果 替换任何早期的记录和死亡的文字</summary>
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

    /// <summary>替换收件箱状态。</summary>
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

    /// <summary>记录显示启动停止 没有操作器中断 所以恢复费没有</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.CleanStop")]
    internal sealed class CleanStop
    {
        public static CleanStop Instance { get; } = new();

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "cleanStop";
    }

    /// <summary>记录显示,一个回收的活化装置 消耗了一个干净的站 所以一个后来不干净的站点被充电</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.InboxCommand.Resume")]
    internal sealed class Resume
    {
        public static Resume Instance { get; } = new();

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "resume";
    }
}

/// <summary>选择 <see cref="InboxCommand"/> 由 <c>kind</c> 中各 JSON 使用案件线索。</summary>
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

/// <summary>收件箱汇在发送时拒绝发来电时给出的答案。</summary>
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
