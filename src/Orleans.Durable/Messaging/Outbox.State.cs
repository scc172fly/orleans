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

internal sealed partial class Outbox
{
    // 条目在已完成的写入捕获其序列号后即已持久化。
    private long _lastSequence;
    private long _capturedSequence;
    private long _durableSequence;

    ValueTask IStateMachine.PrepareWriteAsync(CancellationToken cancellationToken) =>
        _lastSequence != _capturedSequence
            ? _pump.EnsureWakeupAsync(Shared.JobTimeProvider.GetUtcNow(), cancellationToken)
            : ValueTask.CompletedTask;

    private void OnWriteCaptured() => _capturedSequence = _lastSequence;

    void IStateMachine.OnWriteCompleted()
    {
        _durableSequence = _capturedSequence;
        _pump.Signal();
    }

    private OutboxCommand CreateSnapshot() => new OutboxCommand.Snapshot(_messages.Values.ToArray(), _deadLetters.Values.ToArray());

    private void Emit(OutboxCommand command)
    {
        Shared.OutboxCodec.WriteSet(command, _writer);
        Apply(command);
    }

    void IStateMachine.Reset(JournalStreamWriter writer)
    {
        _writer = writer;
        ResetState();
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, Shared.OutboxCodec).Apply(entry.Reader, this);

    void IStateMachine.OnRecoveryCompleted() => OnRecoveryCompleted();
    void IStateMachine.WritePendingEntries(JournalStreamWriter writer) => OnWriteCaptured();

    void IStateMachine.WriteSnapshot(JournalStreamWriter writer)
    {
        Shared.OutboxCodec.WriteSet(CreateSnapshot(), writer);
        OnWriteCaptured();
    }

    void IDurableValueCommandHandler<OutboxCommand>.ApplySet(OutboxCommand command) => Apply(command);

    private void Apply(OutboxCommand command)
    {
        switch (command)
        {
            case OutboxCommand.Enqueue { Message: var message, State: var state }:
                Add(new OutboxEntry { Message = message, State = state, Sequence = ++_lastSequence });
                break;
            case OutboxCommand.EnqueueReference { Reference: var reference, State: var state }:
                Add(new OutboxEntry { Reference = reference, State = state, Sequence = ++_lastSequence });
                break;
            case OutboxCommand.Retry { MessageId: var key, State: var state }:
                _messages[key].State = state;
                break;
            case OutboxCommand.Retire { MessageId: var key, DeadLetter: var deadLetter }:
                if (_messages.Remove(key)) ReportDepth(-1);
                if (deadLetter is not null) _deadLetters[key] = deadLetter;
                break;
            case OutboxCommand.RemoveDeadLetters { Keys: var keys }:
                foreach (var key in keys) _deadLetters.Remove(key);
                break;
            case OutboxCommand.Snapshot snapshot:
                var previousCount = _messages.Count;
                _messages.Clear();
                _deadLetters.Clear();
                foreach (var restored in snapshot.Messages) _messages.Add(restored.MessageId, restored);
                foreach (var restored in snapshot.DeadLetters) _deadLetters.Add(restored.Message.Envelope.MessageId, restored);
                ReportDepth(_messages.Count - previousCount);
                break;
        }
    }

    private void Add(OutboxEntry entry)
    {
        // 发送操作为每个条目标记发送时间，以此限定其重试期限。
        Debug.Assert(entry.State.SentAt is not null);
        _messages.Add(entry.MessageId, entry);
        ReportDepth(1);
    }

    internal bool RemoveDeadLetter(MessageId id)
    {
        _pump.ShutdownToken.ThrowIfCancellationRequested();
        if (!_deadLetters.ContainsKey(id)) return false;
        Emit(new OutboxCommand.RemoveDeadLetters([id]));
        return true;
    }
}

/// <summary>发件箱日志状态的一项变更。</summary>
/// <remarks>
/// 分支标识符遵循声明顺序，是日志格式的一部分。JSON 中的每个分支均以其
/// <c>kind</c> 开头，<see cref="OutboxCommandClassifier"/> 读取该值以选择分支。
/// </remarks>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxCommand")]
[JsonUnion(TypeClassifier = typeof(OutboxCommandClassifier))]
internal union OutboxCommand(
    OutboxCommand.Enqueue,
    OutboxCommand.EnqueueReference,
    OutboxCommand.Retry,
    OutboxCommand.Retire,
    OutboxCommand.RemoveDeadLetters,
    OutboxCommand.Snapshot)
{
    /// <summary>暂存一条消息，发件箱将其完整内容记入日志。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxCommand.Enqueue")]
    internal sealed class Enqueue(OutboxMessage message, OutboxMessageState state)
    {
        [Id(0)] public OutboxMessage Message { get; } = message;
        [Id(1)] public OutboxMessageState State { get; } = state;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "enqueue";
    }

    /// <summary>暂存由 <see cref="IOutboxMessageSource"/> 提供的消息，仅将其标识记入日志。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxCommand.EnqueueReference")]
    internal sealed class EnqueueReference(OutboxMessageReference reference, OutboxMessageState state)
    {
        [Id(0)] public OutboxMessageReference Reference { get; } = reference;
        [Id(1)] public OutboxMessageState State { get; } = state;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "enqueueReference";
    }

    [GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxCommand.Retry")]
    internal sealed class Retry(MessageId messageId, OutboxMessageState state)
    {
        [Id(0)] public MessageId MessageId { get; } = messageId;
        [Id(1)] public OutboxMessageState State { get; } = state;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "retry";
    }

    /// <summary>移除待处理消息；如果发件箱已将消息转为死信，则记录其死信。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxCommand.Retire")]
    internal sealed class Retire(MessageId messageId, OutboxDeadLetterRecord? deadLetter = null)
    {
        [Id(0)] public MessageId MessageId { get; } = messageId;
        [Id(1)] public OutboxDeadLetterRecord? DeadLetter { get; } = deadLetter;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "retire";
    }

    [GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxCommand.RemoveDeadLetters")]
    internal sealed class RemoveDeadLetters(MessageId[] keys)
    {
        [Id(0)] public MessageId[] Keys { get; } = keys;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "removeDeadLetters";
    }

    /// <summary>替换发件箱的状态。</summary>
    [GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxCommand.Snapshot")]
    internal sealed class Snapshot(OutboxEntry[] messages, OutboxDeadLetterRecord[] deadLetters)
    {
        [Id(0)] public OutboxEntry[] Messages { get; } = messages;
        [Id(1)] public OutboxDeadLetterRecord[] DeadLetters { get; } = deadLetters;

        [JsonPropertyName("kind"), JsonPropertyOrder(-1)]
        public string Kind => "snapshot";
    }
}

/// <summary>选择一个 <see cref="OutboxCommand"/> 分支，依据是每个 JSON 分支开头的 <c>kind</c>。</summary>
internal sealed class OutboxCommandClassifier : JsonTypeClassifierFactory
{
    public override bool CanClassify(JsonTypeClassifierContext context) => context.DeclaringType == typeof(OutboxCommand);

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
                    "enqueue" => typeof(OutboxCommand.Enqueue),
                    "enqueueReference" => typeof(OutboxCommand.EnqueueReference),
                    "retry" => typeof(OutboxCommand.Retry),
                    "retire" => typeof(OutboxCommand.Retire),
                    "removeDeadLetters" => typeof(OutboxCommand.RemoveDeadLetters),
                    "snapshot" => typeof(OutboxCommand.Snapshot),
                    _ => null
                };
            }

            lookahead.Skip();
        }

        return null;
    }
}

/// <summary>标识一条由 <see cref="IOutboxMessageSource"/> 提供的消息。</summary>
[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxMessageReference")]
internal readonly struct OutboxMessageReference
{
    [Id(0)] public required MessageId MessageId { get; init; }
}

[GenerateSerializer, Alias("Orleans.Durable.Messaging.OutboxEntry")]
internal sealed class OutboxEntry
{
    // Message 与 Reference 恰好设置其中之一。引用条目不将有效载荷或接收方记入日志：其来源提供消息。
    [Id(0)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OutboxMessage? Message { get; init; }

    [Id(1)] public required OutboxMessageState State { get; set; }

    [Id(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OutboxMessageReference? Reference { get; init; }

    [field: NonSerialized]
    [JsonIgnore] public long Sequence { get; init; }

    /// <summary>
    /// 获取拥有引用条目消息的来源，在发送或恢复后首次实体化消息时解析；
    /// 对普通条目或没有来源提供的已恢复引用，返回 <see langword="null"/>。
    /// </summary>
    [field: NonSerialized]
    [JsonIgnore] public IOutboxMessageSource? Source { get; set; }

    [JsonIgnore]
    public MessageId MessageId => Message is { } message ? message.Envelope.MessageId
        : Reference is { } reference ? reference.MessageId
        : throw new InvalidOperationException("An outbox entry requires its message or message reference.");
}

[JsonSerializable(typeof(OutboxCommand))]
internal sealed partial class OutboxJsonContext : JsonSerializerContext;
