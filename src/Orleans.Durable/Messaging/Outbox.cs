using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Diagnostics;
using Orleans.Durable.Journaling;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 将已获得日志写入确认的消息投递至接收方收件箱。
/// </summary>
internal sealed partial class Outbox : IStateMachine, IDurableValueCommandHandler<OutboxCommand>, IOutbox
{
    private readonly MessagingPump _pump;
    private readonly Inbox _inbox;
    private JournalStreamWriter _writer;
    private readonly Dictionary<MessageId, OutboxEntry> _messages = [];
    private readonly Dictionary<MessageId, OutboxDeadLetterRecord> _deadLetters = [];
    private readonly List<IOutboxMessageSource> _sources = [];

    // 记录投递尝试结果尚未应用的条目、已完成尝试的结果，以及
    // 仍在运行的尝试数量。条目在应用结果前保持在途状态，因此不会同时有两次尝试。
    private readonly HashSet<OutboxEntry> _inFlight = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeliveryOutcome> _outcomes = [];
    private int _running;
    private TaskCompletionSource? _drained;

    // Grain 发送给自身的消息直接进入自身的收件箱，因此解析发件箱时也会解析收件箱。
    public Outbox(MessagingPump pump, Inbox inbox)
    {
        _pump = pump;
        _inbox = inbox;
        pump.StateManager.RegisterStateMachine(MessagingStateNames.Outbox, this);
        pump.Register(this);
    }

    private MessagingShared Shared => _pump.Shared;
    private DurableMessagingOptions Options => _pump.Shared.Options;
    private ILogger Logger => _pump.Shared.OutboxLogger;
    private TimeProvider TimeProvider => Shared.TimeProvider;
    private TimeProvider JobTimeProvider => Shared.JobTimeProvider;
    private GrainId GrainId => _pump.GrainContext.GrainId;

    internal IReadOnlyDictionary<MessageId, OutboxEntry> MessageState => _messages;
    internal IReadOnlyDictionary<MessageId, OutboxDeadLetterRecord> DeadLetterState => _deadLetters;
    internal int Count => _messages.Count;

    /// <summary>获取投递尝试是否处于在途状态或等待消息泵应用结果。</summary>
    internal bool IsDelivering => _inFlight.Count > 0;

    public IOutbox Register(IOutboxMessageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        return this;
    }

    public void Send(OutboxMessage message)
    {
        _pump.ShutdownToken.ThrowIfCancellationRequested();
        EnvelopeValidation.Validate(message);
        if (_messages.TryGetValue(message.Envelope.MessageId, out var existing))
        {
            if (existing.Message is not { } pending || !EnvelopeEquivalence.AreEquivalent(pending, message))
            {
                throw new InvalidOperationException(
                    $"The outbox already contains a different envelope with message ID '{message.Envelope.MessageId}'.");
            }
            return;
        }

        if (message.ReceiverId == GrainId)
        {
            // Grain 自身的收件箱在调用方的日志写入中接纳消息，因此发件箱从不持有该消息。
            _inbox.StageLocal(message.ToInboxMessage(JobTimeProvider.GetUtcNow()));
            Shared.Instruments.OnOutboxMessageSent(_pump.GrainTypeTag);
            return;
        }

        try
        {
            Emit(new OutboxCommand.Enqueue(message, CreateState()));
            Shared.Instruments.OnOutboxMessageSent(_pump.GrainTypeTag);
        }
        catch (Exception exception)
        {
            FailStaging(exception);
        }
    }

    public void Send(MessageId messageId)
    {
        _pump.ShutdownToken.ThrowIfCancellationRequested();
        EnvelopeValidation.ValidateMessageId(messageId);
        if (_messages.TryGetValue(messageId, out var existing))
        {
            // 消息来源是待处理引用条目消息的权威来源，因此重复发送会合并。
            if (existing.Reference is null)
            {
                throw new InvalidOperationException(
                    $"The outbox already contains a different message with message ID '{messageId}'.");
            }
            return;
        }

        if (FindSource(messageId, out _) is not { } source)
        {
            throw new InvalidOperationException($"No registered outbox message source provides message '{messageId}'.");
        }

        try
        {
            Emit(new OutboxCommand.EnqueueReference(new() { MessageId = messageId }, CreateState()));
            _messages[messageId].Source = source;
            Shared.Instruments.OnOutboxMessageSent(_pump.GrainTypeTag);
        }
        catch (Exception exception)
        {
            FailStaging(exception);
        }
    }

    public bool Withdraw(MessageId messageId)
    {
        _pump.ShutdownToken.ThrowIfCancellationRequested();
        if (!_messages.TryGetValue(messageId, out var entry)) return false;
        if (entry.Reference is null)
        {
            throw new InvalidOperationException($"Message '{messageId}' is pending as an ordinary outbox message, which can't be withdrawn.");
        }

        try
        {
            Emit(new OutboxCommand.Retire(messageId));
        }
        catch (Exception exception)
        {
            FailStaging(exception);
        }

        Shared.Instruments.OnOutboxMessageDelivered(_pump.GrainTypeTag, "withdrawn");
        LogMessageWithdrawn(Logger, messageId);
        return true;
    }

    // 发送时的写入固定逻辑发送时间，适用于每次尝试和恢复。
    private OutboxMessageState CreateState() =>
        new() { SentAt = JobTimeProvider.GetUtcNow() };

    private DateTimeOffset GetExpirationTime(OutboxMessageState state) =>
        MessagingTime.AddClamped(state.SentAt!.Value, Options.OutboxRetryTimeout);

    /// <summary>返回首个提供该消息的已注册来源，以及该来源经过验证的消息。</summary>
    private IOutboxMessageSource? FindSource(MessageId messageId, out OutboxMessage message)
    {
        foreach (var source in _sources)
        {
            if (TryGetSourceMessage(source, messageId, out message)) return source;
        }

        message = default;
        return null;
    }

    /// <summary>
    /// 返回条目的消息；对于引用条目，从拥有它的来源实体化消息；若已恢复条目
    /// 没有任何已注册来源提供，则返回 <see langword="false"/>。如果拥有它的来源
    /// 抛出异常、返回无效消息或不再提供该消息，则抛出异常。
    /// </summary>
    private bool TryGetMessage(OutboxEntry entry, out OutboxMessage message)
    {
        if (entry.Message is { } ordinary)
        {
            message = ordinary;
            return true;
        }

        if (entry.Source is { } owner)
        {
            if (TryGetSourceMessage(owner, entry.MessageId, out message)) return true;

            // 拥有者在发送时提供消息，撤回操作通过 IOutbox.Withdraw 执行。
            throw new InvalidOperationException(
                $"The outbox message source which owns message '{entry.MessageId}' no longer provides it; withdraw a message with IOutbox.Withdraw.");
        }

        // 已恢复条目的拥有者是第一个没有拒绝接手的来源，包括尝试提供消息但失败的
        // 来源，因此该条目转入死信时会通知该来源。
        foreach (var source in _sources)
        {
            entry.Source = source;
            if (TryGetSourceMessage(source, entry.MessageId, out message)) return true;
        }

        entry.Source = null;
        message = default;
        return false;
    }

    private bool TryGetSourceMessage(IOutboxMessageSource source, MessageId messageId, out OutboxMessage message)
    {
        if (!source.TryGetMessage(messageId, out message)) return false;
        EnvelopeValidation.Validate(message);
        if (message.Envelope.MessageId != messageId)
        {
            throw new InvalidOperationException(
                $"The outbox message source returned message ID '{message.Envelope.MessageId}' for message '{messageId}'.");
        }
        if (message.ReceiverId == GrainId)
        {
            // Grain 自身的收件箱会持有有效载荷，因此来源不能保留唯一副本。
            throw new ArgumentException(
                $"The outbox message source addressed message '{messageId}' to the sending grain; send its message with Send(OutboxMessage) instead.",
                nameof(messageId));
        }

        return true;
    }

    /// <summary>通知已退出发件箱的引用条目的来源，使其释放操作随条目移除一起提交。</summary>
    private void Retire(OutboxEntry entry, OutboxMessageRetirement retirement)
    {
        // 无来源提供的已恢复引用没有需要通知的拥有者。
        entry.Source?.OnRetired(entry.MessageId, retirement);
        MessagingEvents.EmitOutboxMessageRetired(_pump.GrainContext, entry.MessageId, retirement);
    }

    /// <summary>
    /// 为来源未能提供消息的死信创建消息：保留消息 ID，使用空有效载荷及
    /// 默认接收方；有效出站消息不允许使用此接收方。
    /// </summary>
    private static OutboxMessage CreateUnmaterializedMessage(MessageId messageId) =>
        new() { Envelope = new() { MessageId = messageId, Payload = [] }, ReceiverId = default };

    /// <summary>复制由来源拥有的消息的有效载荷和标头值。</summary>
    private static OutboxMessage CopySourceMessage(OutboxMessage message)
    {
        var envelope = message.Envelope;
        Dictionary<string, byte[]>? headers = null;
        if (envelope.Headers is { } sourceHeaders)
        {
            headers = new(sourceHeaders.Count, sourceHeaders.Comparer);
            foreach (var (name, value) in sourceHeaders) headers.Add(name, value.AsSpan().ToArray());
        }

        return message with { Envelope = envelope with { Payload = envelope.Payload.AsSpan().ToArray(), Headers = headers } };
    }

    private void OnRecoveryCompleted()
    {
        if (_sources.Count == 0 && _messages.Values.Any(static entry => entry.Reference is not null))
        {
            throw new InvalidOperationException(
                "The outbox recovered messages which a source provides, but no outbox message source is registered. Register outbox message sources during activation setup, before journal recovery.");
        }

        // 发送时已验证每条消息，重放使用各条目的消息 ID 作为键。
        Debug.Assert(_messages.All(pair => pair.Key == pair.Value.MessageId
            && (pair.Value.Message is null) != (pair.Value.Reference is null)
            && pair.Value.Message?.ReceiverId != GrainId));
        Debug.Assert(_deadLetters.All(static pair => pair.Key == pair.Value.Message.Envelope.MessageId));
        _capturedSequence = _durableSequence = _lastSequence;
    }

    private void ResetState()
    {
        ReportDepth(-_messages.Count);
        _messages.Clear();
        _deadLetters.Clear();
        _lastSequence = _capturedSequence = _durableSequence = 0;
    }

    [DoesNotReturn]
    private void FailStaging(Exception exception)
    {
        LogStagingFailed(Logger, exception);
        _pump.Fail(exception, "Outbox staging failed.");
        ExceptionDispatchInfo.Throw(exception);
    }

    private async ValueTask PersistAsync()
    {
        try
        {
            await _pump.StateManager.WriteStateAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _pump.Fail(exception, "Outbox persistence failed.");
            throw;
        }
    }

    /// <summary>
    /// 最多选择 <paramref name="capacity"/> 个已持久化、已到期且无在途尝试的条目：包括
    /// 需要尝试投递的消息和无需尝试即转入死信的条目。
    /// </summary>
    private (List<(OutboxEntry Entry, OutboxMessage Message)>? Candidates, List<(OutboxEntry Entry, Unavailable Reason)>? Unavailable) SelectMessages(int capacity)
    {
        var now = JobTimeProvider.GetUtcNow();
        List<(OutboxEntry Entry, OutboxMessage Message)>? candidates = null;
        List<(OutboxEntry Entry, Unavailable Reason)>? unavailable = null;
        var selected = 0;
        foreach (var entry in _messages.Values)
        {
            if (entry.Sequence > _durableSequence || _inFlight.Contains(entry) || entry.State.NextAttemptAt > now) continue;

            // 仅需移除的条目也计入容量，因此单轮最多移除一个批次的条目。
            if (++selected > capacity) break;
            OutboxMessage? message = null;
            Exception? error = null;
            try
            {
                if (TryGetMessage(entry, out var provided)) message = provided;
            }
            catch (Exception exception)
            {
                // 条目已持久化，因此无法提供有效消息的来源只会导致该条目转入死信。
                error = exception;
            }

            // 背压与失败尝试共享发送时写入所确立的绝对重试预算。
            if (GetExpirationTime(entry.State) <= now) (unavailable ??= []).Add((entry, new Unavailable.Expired(message)));
            else if (error is not null) (unavailable ??= []).Add((entry, new Unavailable.Failed(error)));
            else if (message is { } candidate) (candidates ??= []).Add((entry, candidate));
            else (unavailable ??= []).Add((entry, Unavailable.Unclaimed.Instance));
        }

        if (candidates is null) LogNoMessages(Logger, Count);
        else LogDeliveringMessages(Logger, candidates.Count);
        return (candidates, unavailable);
    }

    private async Task<DeliveryOutcome> DeliverAsync(OutboxEntry entry, OutboxMessage message, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            // 保留及准入采用原始发送时间，包括延迟尝试与已恢复条目。
            var delivery = new InboxMessage
            {
                Envelope = message.Envelope,
                SentAt = entry.State.SentAt!.Value
            };
            var result = await Shared.Transport.DeliverAsync(message.ReceiverId, delivery, cancellationToken).ConfigureAwait(true);
            return new(entry, message, new DeliveryAnswer.Answered(result), Stopwatch.GetElapsedTime(start));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(entry, message, new DeliveryAnswer.Failed("Delivery was canceled."), Stopwatch.GetElapsedTime(start));
        }
        catch (Exception exception)
        {
            if (Logger.IsEnabled(LogLevel.Error))
            {
                LogDeliveryError(Logger, exception, message.Envelope.MessageId, message.ReceiverId);
            }
            return new(entry, message, new DeliveryAnswer.Failed(exception.ToString()), Stopwatch.GetElapsedTime(start));
        }
    }

    /// <summary>
    /// 应用已完成投递尝试的结果，并为到期消息启动尝试，最多保持
    /// <see cref="DurableMessagingOptions.OutboxBatchSize"/> 次在途尝试，同时返回状态是否发生变化。
    /// </summary>
    /// <remarks>
    /// 每次尝试独立完成，因此较慢的接收方只占用一个槽位，不会延迟发往其他接收方的消息。
    /// </remarks>
    internal async ValueTask<bool> AdvanceAsync(CancellationToken cancellationToken)
    {
        var progress = false;
        if (_outcomes.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcomes = _outcomes.ToArray();
            _outcomes.Clear();
            foreach (var outcome in outcomes) _inFlight.Remove(outcome.Entry);
            await ApplyDeliveriesAsync(outcomes).ConfigureAwait(true);
            progress = true;
        }

        var capacity = Options.OutboxBatchSize - _inFlight.Count;
        if (capacity <= 0) return progress;
        var (candidates, unavailable) = SelectMessages(capacity);

        // 尝试在任何 await 之前启动，因此在实体化消息与
        // 调用其接收方之间，不会交错发生撤回操作或来源变更。
        if (candidates is not null)
        {
            foreach (var (entry, message) in candidates) RunAttemptAsync(entry, message).Ignore();
        }

        if (unavailable is not null)
        {
            await DeadLetterUnavailableAsync(unavailable).ConfigureAwait(true);
            progress = true;
        }

        return progress;
    }

    private async Task RunAttemptAsync(OutboxEntry entry, OutboxMessage message)
    {
        _inFlight.Add(entry);
        _running++;
        var outcome = await DeliverAsync(entry, message, _pump.ShutdownToken).ConfigureAwait(true);
        _outcomes.Add(outcome);
        if (--_running == 0 && _drained is { } drained)
        {
            _drained = null;
            drained.TrySetResult();
        }

        _pump.Signal();
    }

    private async ValueTask ApplyDeliveriesAsync(DeliveryOutcome[] outcomes)
    {
        int applied = 0, delivered = 0, backpressured = 0;
        try
        {
            foreach (var outcome in outcomes)
            {
                // 如果条目在尝试开始后被撤回，或被移除后重新发送，则其尝试结果已过时。
                var id = outcome.Entry.MessageId;
                if (!_messages.TryGetValue(id, out var current) || !ReferenceEquals(current, outcome.Entry)) continue;

                switch (outcome.Answer)
                {
                    case DeliveryAnswer.Answered { Result: DeliveryResult.Expired }:
                        // 过期意味着此次发送不晚于准入下限；此前是否被接纳仍未知。
                        StageDeadLetter(
                            current,
                            current.Reference is null ? outcome.Message : CopySourceMessage(outcome.Message),
                            "The message was sent before the receiver's deduplication window; its earlier delivery outcome is unknown.",
                            checked(current.State.AttemptCount + 1));
                        break;
                    case DeliveryAnswer.Answered { Result: DeliveryResult.Accepted or DeliveryResult.Duplicate }:
                        Emit(new OutboxCommand.Retire(id));
                        Retire(current, OutboxMessageRetirement.Delivered.Instance);
                        delivered++;
                        break;
                    case DeliveryAnswer.Answered { Result: DeliveryResult.DeadLettered { Reason: var reason, Code: var code } }:
                        Emit(new OutboxCommand.Retire(id));
                        Retire(current, new OutboxMessageRetirement.Rejected(reason, code));
                        delivered++;
                        break;
                    default:
                        // 背压、缺少处理器和调用失败均使结果处于未知状态，因此重试消息。
                        var attemptCount = checked(current.State.AttemptCount + 1);
                        var error = outcome.Answer switch
                        {
                            DeliveryAnswer.Failed { Error: var failure } => failure,
                            DeliveryAnswer.Answered { Result: DeliveryResult.Backpressured } => "The receiver is backpressured.",
                            _ => "The receiver has no registered inbox handler."
                        };
                        if (outcome.Answer is DeliveryAnswer.Answered { Result: DeliveryResult.Backpressured }) backpressured++;
                        if (PrepareRetry(outcome, attemptCount, error) is { } retry)
                        {
                            Emit(new OutboxCommand.Retry(id, retry));
                            MessagingEvents.EmitOutboxRetryScheduled(
                                _pump.GrainContext, id, outcome.Message.ReceiverId, attemptCount, retry.NextAttemptAt!.Value, error);
                        }
                        else
                        {
                            // 引用条目退出时，来源可能释放其有效载荷和标头，因此死信保留一个副本。
                            StageDeadLetter(current, current.Reference is null ? outcome.Message : CopySourceMessage(outcome.Message), error, attemptCount);
                        }
                        break;
                }

                RecordDeliveryMetrics(outcome);
                applied++;
            }
        }
        catch (Exception exception)
        {
            FailStaging(exception);
        }

        if (applied == 0) return;
        await PersistAsync().ConfigureAwait(true);
        LogDeliveryComplete(Logger, delivered, backpressured, applied - delivered - backpressured, Count);
    }

    /// <summary>
    /// 返回失败尝试后的重试状态；若重试超时已到期，则返回 <see langword="null"/>。
    /// </summary>
    private OutboxMessageState? PrepareRetry(DeliveryOutcome outcome, int attemptCount, string error)
    {
        var now = JobTimeProvider.GetUtcNow();
        var expirationTime = GetExpirationTime(outcome.Entry.State);
        if (expirationTime <= now) return null;

        // 下一次尝试最迟在超时时间运行；届时若已超时，发件箱会改为将消息转入死信。
        var retryAt = MessagingTime.AddClamped(now, MessagingTime.GetBackoff(Options.RetryInitialInterval, attemptCount));
        return new OutboxMessageState
        {
            AttemptCount = attemptCount,
            LastError = error,
            SentAt = outcome.Entry.State.SentAt,
            NextAttemptAt = retryAt < expirationTime ? retryAt : expirationTime
        };
    }

    /// <summary>
    /// 无需尝试即将条目转入死信：包括重试超时已到期的条目、其所属来源未能
    /// 提供有效消息的条目，以及没有任何已注册来源提供的已恢复引用。
    /// </summary>
    private async ValueTask DeadLetterUnavailableAsync(List<(OutboxEntry Entry, Unavailable Reason)> unavailable)
    {
        try
        {
            foreach (var (entry, reason) in unavailable)
            {
                // 本循环中较早运行的退出回调可能已撤回此条目，此时条目不再有结果。
                var id = entry.MessageId;
                if (!_messages.TryGetValue(id, out var current) || !ReferenceEquals(current, entry)) continue;

                switch (reason)
                {
                    case Unavailable.Expired { Message: var message }:
                        LogMessageExpired(Logger, id);
                        StageDeadLetter(
                            entry,
                            message is not { } expired ? CreateUnmaterializedMessage(id) : entry.Reference is null ? expired : CopySourceMessage(expired),
                            entry.State.LastError is { } last
                                ? $"The outbox retry timeout elapsed after the last attempt failed: {last}"
                                : "The outbox retry timeout elapsed before the message was delivered.",
                            entry.State.AttemptCount);
                        Shared.Instruments.OnOutboxMessageDelivered(_pump.GrainTypeTag, "expired");
                        break;
                    case Unavailable.Failed { Error: var error }:
                        LogSourceFailed(Logger, error, id);
                        StageDeadLetter(entry, CreateUnmaterializedMessage(id),
                            $"The outbox message source failed to provide the message: {error}", checked(entry.State.AttemptCount + 1));
                        Shared.Instruments.OnOutboxMessageDelivered(_pump.GrainTypeTag, "source_error");
                        break;
                    case Unavailable.Unclaimed:
                        LogMessageUnprovided(Logger, id);
                        StageDeadLetter(entry, CreateUnmaterializedMessage(id),
                            "No registered outbox message source provides the message.", entry.State.AttemptCount);
                        Shared.Instruments.OnOutboxMessageDelivered(_pump.GrainTypeTag, "unprovided");
                        break;
                }
            }
        }
        catch (Exception exception)
        {
            FailStaging(exception);
        }

        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 暂存 <paramref name="entry"/> 的移除操作及其死信，并在
    /// 同一次日志写入中通知其所属来源（如有）该条目已退出。
    /// </summary>
    private void StageDeadLetter(
        OutboxEntry entry, OutboxMessage message, string reason, int attemptCount, OutboxMessageRetirement? retirement = null)
    {
        var now = TimeProvider.GetUtcNow();
        var id = entry.MessageId;
        CompactDeadLetters(now, reservedCapacity: _deadLetters.ContainsKey(id) ? 0 : 1);
        Emit(new OutboxCommand.Retire(id, new() { Message = message, DeadLetteredAt = now, Reason = reason, AttemptCount = attemptCount }));
        Retire(entry, retirement ?? new OutboxMessageRetirement.DeadLettered(reason));
    }

    private bool CompactDeadLetters(DateTimeOffset now, int reservedCapacity = 0)
    {
        if (_deadLetters.Count == 0) return false;
        var retained = new Dictionary<MessageId, OutboxDeadLetterRecord>(_deadLetters);
        if (!DeadLetterRetention.Compact(retained, now, Options.DeadLetterRetentionPeriod, Options.MaxRetainedDeadLetters,
            static entry => entry.DeadLetteredAt, reservedCapacity))
        {
            return false;
        }

        Emit(new OutboxCommand.RemoveDeadLetters(_deadLetters.Keys.Where(key => !retained.ContainsKey(key)).ToArray()));
        return true;
    }
    private void RecordDeliveryMetrics(DeliveryOutcome outcome)
    {
        var message = outcome.Message;
        if (outcome.Answer is not DeliveryAnswer.Answered { Result: var result })
        {
            Shared.Instruments.OnOutboxMessageDelivered(_pump.GrainTypeTag, "error");
            Shared.Instruments.OnOutboxDeliveryDuration(outcome.Duration, _pump.GrainTypeTag);
            return;
        }

        var metricStatus = result switch
        {
            DeliveryResult.Accepted => "accepted",
            DeliveryResult.Duplicate => "duplicate",
            DeliveryResult.DeadLettered => "deadlettered",
            DeliveryResult.Backpressured => "backpressured",
            DeliveryResult.HandlerNotFound => "handler_not_found",
            DeliveryResult.Expired => "expired"
        };
        Shared.Instruments.OnOutboxMessageDelivered(_pump.GrainTypeTag, metricStatus);
        Shared.Instruments.OnOutboxDeliveryDuration(outcome.Duration, _pump.GrainTypeTag);
        switch (result)
        {
            case DeliveryResult.Accepted or DeliveryResult.Duplicate or DeliveryResult.DeadLettered or DeliveryResult.Expired:
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    LogMessageDelivered(Logger, message.Envelope.MessageId, message.ReceiverId, metricStatus);
                }
                break;
            case DeliveryResult.Backpressured:
                LogDeliveryBackpressured(Logger, message.Envelope.MessageId, message.ReceiverId);
                break;
            case DeliveryResult.HandlerNotFound:
                LogDeliveryHandlerNotFound(Logger, message.Envelope.MessageId, message.ReceiverId);
                break;
        }
    }

    /// <summary>
    /// 返回距下一次已捕获的投递尝试到期的延迟；如果没有剩余尝试，则返回 <see langword="null"/>。
    /// </summary>
    /// <param name="now">当前 Durable Jobs 时间。</param>
    internal TimeSpan? GetNextWakeupDelay(DateTimeOffset now)
    {
        TimeSpan? next = null;
        foreach (var entry in _messages.Values)
        {
            // 捕获紧随同步准备之后；相应唤醒保障此次写入，直至收到确认。
            if (entry.Sequence > _capturedSequence) continue;
            var at = entry.State.NextAttemptAt ?? now;
            var delay = at > now ? at - now : TimeSpan.Zero;
            if (delay == TimeSpan.Zero) return delay;
            if (next is null || delay < next) next = delay;
        }
        return next;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!CompactDeadLetters(TimeProvider.GetUtcNow())) return;
        }
        catch (Exception exception)
        {
            FailStaging(exception);
        }

        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>等待在途投递完成，这些投递会观察消息泵的关闭令牌。</summary>
    internal Task StopAsync() => _running == 0 ? Task.CompletedTask : (_drained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    /// <summary>在丢弃激活实例的消息传递状态后，释放已报告的队列深度指标。</summary>
    internal void ReleaseDepth() => ReportDepth(-_messages.Count);

    private void ReportDepth(int delta) => Shared.Instruments.OnOutboxDepthChanged(_pump.GrainTypeTag, delta);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox command staging failed.")]
    private static partial void LogStagingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No durable messages to deliver (all {Count} messages are still pending)")]
    private static partial void LogNoMessages(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delivering {Count} durable messages from outbox")]
    private static partial void LogDeliveringMessages(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delivered message {MessageId} to {ReceiverId} (Status: {Status})")]
    private static partial void LogMessageDelivered(ILogger logger, MessageId messageId, GrainId receiverId, string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "No inbox handler for message {MessageId} to {ReceiverId}")]
    private static partial void LogDeliveryHandlerNotFound(ILogger logger, MessageId messageId, GrainId receiverId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Backpressured delivering message {MessageId} to {ReceiverId}, will retry later")]
    private static partial void LogDeliveryBackpressured(ILogger logger, MessageId messageId, GrainId receiverId);


    [LoggerMessage(Level = LogLevel.Error, Message = "Error delivering message {MessageId} to {ReceiverId}")]
    private static partial void LogDeliveryError(ILogger logger, Exception exception, MessageId messageId, GrainId receiverId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed outbox message {MessageId}, which its source withdrew")]
    private static partial void LogMessageWithdrawn(ILogger logger, MessageId messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dead-lettered recovered outbox message {MessageId}, which no registered outbox message source provides")]
    private static partial void LogMessageUnprovided(ILogger logger, MessageId messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dead-lettered outbox message {MessageId}, whose retry timeout elapsed")]
    private static partial void LogMessageExpired(ILogger logger, MessageId messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox message source failed to provide message {MessageId}; the message is dead-lettered")]
    private static partial void LogSourceFailed(ILogger logger, Exception exception, MessageId messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox delivery complete: {DeliveredCount} delivered, {BackpressuredCount} backpressured, {FailedCount} failed, {RemainingCount} remaining")]
    private static partial void LogDeliveryComplete(ILogger logger, int deliveredCount, int backpressuredCount, int failedCount, int remainingCount);

    private readonly record struct DeliveryOutcome(OutboxEntry Entry, OutboxMessage Message, DeliveryAnswer Answer, TimeSpan Duration);

    /// <summary>接收方对投递尝试的响应，或者使此次尝试结果未知的失败信息。</summary>
    private union DeliveryAnswer(DeliveryAnswer.Answered, DeliveryAnswer.Failed)
    {
        public sealed class Answered(DeliveryResult result)
        {
            public DeliveryResult Result { get; } = result;
        }

        public sealed class Failed(string error)
        {
            public string Error { get; } = error;
        }
    }

    /// <summary>已到期条目无需尝试即转入死信的原因。</summary>
    private union Unavailable(Unavailable.Expired, Unavailable.Failed, Unavailable.Unclaimed)
    {
        /// <summary>重试超时已到期；若来源提供了消息，则将该消息保留在死信中。</summary>
        public sealed class Expired(OutboxMessage? message)
        {
            public OutboxMessage? Message { get; } = message;
        }

        /// <summary>所属来源未能提供有效消息。</summary>
        public sealed class Failed(Exception error)
        {
            public Exception Error { get; } = error;
        }

        /// <summary>没有已注册来源提供已恢复的引用。</summary>
        public sealed class Unclaimed
        {
            public static Unclaimed Instance { get; } = new();
        }
    }
}
