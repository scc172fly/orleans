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
/// 向接收器的收件箱发送日记认可的信息。
/// </summary>
internal sealed partial class Outbox : IStateMachine, IDurableValueCommandHandler<OutboxCommand>, IOutbox
{
    private readonly MessagingPump _pump;
    private readonly Inbox _inbox;
    private JournalStreamWriter _writer;
    private readonly Dictionary<MessageId, OutboxEntry> _messages = [];
    private readonly Dictionary<MessageId, OutboxDeadLetterRecord> _deadLetters = [];
    private readonly List<IOutboxMessageSource> _sources = [];

    // 尚未适用交付尝试结果的条目、已完成尝试的结果以及
    // 输入在飞行中一直停留到结果应用为止,所以它从来没有两次尝试。
    private readonly HashSet<OutboxEntry> _inFlight = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeliveryOutcome> _outcomes = [];
    private int _running;
    private TaskCompletionSource? _drained;

    // A grain给自己发的信息会转到自己的收件箱,所以解决发件箱可以解答收件箱。
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

    /// <summary>了解运送尝试是否在飞行中或等待泵应用。</summary>
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
            // grain自己的收件箱接受调用器日记中的信件,所以发件箱永远不能保存它。
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
            // 来源对待处理的参考条目信息具有权威性,因此重复发送联结。
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

    // 发送写入为每次尝试和恢复修正逻辑发送时间 。
    private OutboxMessageState CreateState() =>
        new() { SentAt = JobTimeProvider.GetUtcNow() };

    private DateTimeOffset GetExpirationTime(OutboxMessageState state) =>
        MessagingTime.AddClamped(state.SentAt!.Value, Options.OutboxRetryTimeout);

    /// <summary>返回第一个已注册的电文源及其有效电文。</summary>
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
    /// 返回条目的电文,从自有来源实现引用条目的电文或返回
    /// <see langword="false"/> 用于一个回收的条目,该条目没有注册来源提供。当拥有源时丢弃
    /// 丢弃、 返回无效的信件, 或者不再提供信件 。
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

            // 电文发回时,所有者提供了电文,撤回经过 IOutbox.Withdraw。
            throw new InvalidOperationException(
                $"The outbox message source which owns message '{entry.MessageId}' no longer provides it; withdraw a message with IOutbox.Withdraw.");
        }

        // 回收的入口物主是第一个没有拒绝的来源,包括一个没有提供源头的来源
        // 以便当条目用无字母编号时通知该来源。
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
            // grain自己的收件箱将持有有效载荷,因此源不能保留该有效载荷作为单份副本。
            throw new ArgumentException(
                $"The outbox message source addressed message '{messageId}' to the sending grain; send its message with Send(OutboxMessage) instead.",
                nameof(messageId));
        }

        return true;
    }

    /// <summary>通知退役参考条目的来源,因此释放该条目即意味着删除该条目。</summary>
    private void Retire(OutboxEntry entry, OutboxMessageRetirement retirement)
    {
        // 回收的引用, 没有源码提供, 没有所有人可以通知 。
        entry.Source?.OnRetired(entry.MessageId, retirement);
        MessagingEvents.EmitOutboxMessageRetired(_pump.GrainContext, entry.MessageId, retirement);
    }

    /// <summary>
    /// 创建来源未提供其消息的死信的电文:带空有效载荷的信息代号,
    /// 默认接收器, 没有有效的发送信件存在 。
    /// </summary>
    private static OutboxMessage CreateUnmaterializedMessage(MessageId messageId) =>
        new() { Envelope = new() { MessageId = messageId, Payload = [] }, ReceiverId = default };

    /// <summary>复制源拥有的电文的有效载荷和信头值。</summary>
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

        // 正在验证发送所有信件, 并用自己的邮件代号重放每个条目的密钥 。
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
    /// 选择最多不超过 <paramref name="capacity"/> 的耐久条目,这些条目到期且在飞行中没有尝试:
    /// 并输入死信条目,
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

            // 仅被删除的条目也计算到容量,所以通行证最多可以删除一批条目。
            if (++selected > capacity) break;
            OutboxMessage? message = null;
            Exception? error = null;
            try
            {
                if (TryGetMessage(entry, out var provided)) message = provided;
            }
            catch (Exception exception)
            {
                // 条目是耐久的, 所以一个源无法提供有效的信息 无效的邮件 仅该条目。
                error = exception;
            }

            // 后压和失败的尝试共享发送书写所设定的绝对重试预算。
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
            // 保留和接纳使用原发送时间,包括延迟尝试和收回条目。
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
    /// 应用完成的传送尝试和开始使用应发信息,以跟上
    /// <see cref="DurableMessagingOptions.OutboxBatchSize"/> 飞行尝试,并返回是否改变状态。
    /// </summary>
    /// <remarks>
    /// 每次尝试都是自己完成的, 所以一个慢速接收器有一个位置 而不延迟给其他接收器的信息。
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

        // 尝试在等待任何消息之前就开始尝试,所以不撤回或源代码改变信息实现之间
        // 并呼叫接收器。
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
                // 一项从尝试开始就撤回或删除并再次发送的条目,其结果已经过时。
                var id = outcome.Entry.MessageId;
                if (!_messages.TryGetValue(id, out var current) || !ReferenceEquals(current, outcome.Entry)) continue;

                switch (outcome.Answer)
                {
                    case DeliveryAnswer.Answered { Result: DeliveryResult.Expired }:
                        // 过期是指发送时间在或低于入场层;但仍未确定是否接受先前的接收。
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
                        // 后压、缺少的处理器和失败的通话结果未知,所以留言重试。
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
                            // 来源在文件留存时可发布参考条目的有效载荷和信头,因此,死信保留副本。
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
    /// 返回一次尝试失败后的重试状态,或返回在重试超时过后返回 <see langword="null"/> 。
    /// </summary>
    private OutboxMessageState? PrepareRetry(DeliveryOutcome outcome, int attemptCount, string error)
    {
        var now = JobTimeProvider.GetUtcNow();
        var expirationTime = GetExpirationTime(outcome.Entry.State);
        if (expirationTime <= now) return null;

        // 下一次尝试的进行时间不晚于超时, 当发件箱用死信来代替信息。
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
    /// 无意地在无企图的死信条目中输入:那些重新尝试超时过期者、那些其自有来源没有提供
    /// 提供有效信息,并收回未登记来源提供的参考资料。
    /// </summary>
    private async ValueTask DeadLetterUnavailableAsync(List<(OutboxEntry Entry, Unavailable Reason)> unavailable)
    {
        try
        {
            foreach (var (entry, reason) in unavailable)
            {
                // 早先在这一循环中进行的退休回召可能已经撤回了条目,但结果没有结果。
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
    /// 拆除 <paramref name="entry"/> 的废纸,并用自己的来源(如果有的话)将其退款。
    /// 在同一日记的写上。
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
    /// 返回延迟到下一次抓获的运送尝试到期时,或在无剩余 <see langword="null"/> 时返回。
    /// </summary>
    /// <param name="now">当前可持久工作的时间。</param>
    internal TimeSpan? GetNextWakeupDelay(DateTimeOffset now)
    {
        TimeSpan? next = null;
        foreach (var entry in _messages.Values)
        {
            // 抓捕是在同步准备之后进行的;它的醒悟覆盖了写作直到确认。
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

    /// <summary>等待飞行中运送, 观察泵的关闭标志。</summary>
    internal Task StopAsync() => _running == 0 ? Task.CompletedTask : (_drained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    /// <summary>一旦启动的信号状态被丢弃, 报告的深度就会释放。</summary>
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

    /// <summary>接收者对运送企图的回答 或者说没有成功 导致尝试结果不明</summary>
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

    /// <summary>为什么一个应得条目是没有企图的写字。</summary>
    private union Unavailable(Unavailable.Expired, Unavailable.Failed, Unavailable.Unclaimed)
    {
        /// <summary>重试超时时间已过;源信息提供后,其信息被保存在死信里。</summary>
        public sealed class Expired(OutboxMessage? message)
        {
            public OutboxMessage? Message { get; } = message;
        }

        /// <summary>所属来源未能提供有效电文。</summary>
        public sealed class Failed(Exception error)
        {
            public Exception Error { get; } = error;
        }

        /// <summary>没有注册来源提供已找到的参考文献。</summary>
        public sealed class Unclaimed
        {
            public static Unclaimed Instance { get; } = new();
        }
    }
}
