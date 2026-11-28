using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Messaging.Diagnostics;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>
/// 接纳、去重并处理单个 Grain 的持久化入站消息。
/// </summary>
/// <remarks>
/// 所有状态转换均在激活实例调度器上运行，因此每个先检查再发出变更的序列都是原子的。
/// 已接纳的消息仅在接纳已持久化且其 <see cref="Envelope.DeliverAt"/>（如有）
/// 已到达后才会被处理。
/// </remarks>
internal sealed partial class Inbox : IInbox, IMessagingExtension, IStateMachine, IDurableValueCommandHandler<InboxCommand>
{
    private readonly MessagingPump _pump;
    private readonly InboxDispatcher _handlers = new([]);
    private readonly List<IInboxMessageSink> _sinks = [];
    private readonly List<Func<TimeSpan?>> _wakeupSources = [];

    public Inbox(MessagingPump pump)
    {
        _pump = pump;
        pump.StateManager.RegisterStateMachine(MessagingStateNames.Inbox, this);
        pump.Register(this);

        // 投递只会到达随日志一起恢复的收件箱，因此从未解析其
        // 收件箱的激活实例没有消息传递扩展。
        pump.GrainContext.SetComponent<IMessagingExtension>(this);
    }

    private MessagingShared Shared => _pump.Shared;
    private DurableMessagingOptions Options => _pump.Shared.Options;
    private ILogger Logger => _pump.Shared.InboxLogger;
    private TimeProvider TimeProvider => _pump.Shared.TimeProvider;
    private IGrainContext GrainContext => _pump.GrainContext;

    internal int Count => _messages.Count;

    internal IReadOnlyDictionary<MessageId, InboxEntry> MessageState => _messages;

    internal IReadOnlyDictionary<MessageId, InboxDeadLetterRecord> DeadLetterState => _deadLetters;

    internal InboxDispatcher Handlers => _handlers;

    public IInbox Register(IInboxHandler handler)
    {
        _handlers.Add(handler);

        // 待处理消息等待处理器时不占用唤醒，因此后续注册处理器会恢复对它们的处理。
        if (_messages.Count > 0) _pump.RequestWakeup();
        return this;
    }

    public IInbox Register(IInboxMessageSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Add(sink);
        return this;
    }

    public IInbox RegisterWakeup(Func<TimeSpan?> getWakeupDelay)
    {
        ArgumentNullException.ThrowIfNull(getWakeupDelay);
        _wakeupSources.Add(getWakeupDelay);
        return this;
    }

    public async ValueTask<DeliveryResult> DeliverAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pump.ShutdownToken.ThrowIfCancellationRequested();
        EnvelopeValidation.Validate(message);

        var key = message.Envelope.MessageId;
        var receiverTime = Shared.JobTimeProvider.GetUtcNow();
        // 已记录的结果和过期判断在时钟回拨后仍然有效，因此仅对收件箱原本会准入的发送时间施加上限。
        if (message.SentAt > MessagingTime.AddClamped(receiverTime, Options.DeduplicationWindow)
            && !_processed.ContainsKey(key)
            && !_messages.ContainsKey(key)
            && message.SentAt > GetAdmissionFloor(receiverTime))
        {
            LogSentAtTooFarAhead(Logger, key, GrainContext.GrainId, message.SentAt, receiverTime);
            throw new ArgumentOutOfRangeException(nameof(message), message.SentAt,
                "SentAt must not be later than the receiver's clock plus DeduplicationWindow.");
        }

        // 投递属于 Grain 调用，因此激活实例正在处理请求。
        _pump.EnsureStarted();
        if (IsDuplicate(message, out var pending))
        {
            var rejection = pending is null ? _processed[key].Rejection : null;
            // 此调用返回后发送方即可移除其副本，因此接纳记录或已处理记录必须
            // 已持久化。处理器或接收器暂存的完成操作可能仍在进行中。
            if (pending is null ? !AreCompletionsDurable : !IsDurable(pending)) await PersistForCallerAsync(cancellationToken).ConfigureAwait(true);
            if (pending is not null) _pump.Signal();

            Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "duplicate");

            // 响应丢失的发送方会重新投递，因此它将收到接收器最初返回的拒绝结果。
            return rejection is not null
                ? new DeliveryResult.DeadLettered(rejection.Reason, rejection.Code)
                : DeliveryResult.Duplicate.Instance;
        }

        // 已保留的标识优先于过期判断：丢失的确认可能对应待处理或已完成的工作。
        // 只有持久化且单调不减的下限才允许遗忘此证据，不受后续时钟变化的影响。
        var floor = GetAdmissionFloor(receiverTime);
        if (message.SentAt <= floor)
        {
            if (message.SentAt > _admissionFloor) Emit(new InboxCommand.AdvanceAdmissionFloor(floor));
            if (_admissionFloor > _durableAdmissionFloor) await PersistForCallerAsync(cancellationToken).ConfigureAwait(true);
            Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "expired");
            LogMessageExpired(Logger, key, GrainContext.GrainId, message.SentAt, _admissionFloor);
            return DeliveryResult.Expired.Instance;
        }

        MessagingEvents.EmitInboxMessageReceiving(GrainContext, message);

        // 由接收器拥有的消息永不占用收件箱容量，也不需要处理器。接收器的重复结果仍可能处于暂存状态，
        // 因此除背压以外的所有结果均在写入已持久化后报告。
        if (StageSinkAcceptance(message) is { } sinkResult)
        {
            if (sinkResult is not DeliveryResult.Backpressured) await PersistForCallerAsync(cancellationToken).ConfigureAwait(true);
            return sinkResult;
        }

        // 每条待处理消息，无论是否已安排处理时间，都占用容量直到完成。
        if (_messages.Count >= Options.MaxCapacity)
        {
            Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "backpressured");
            return DeliveryResult.Backpressured.Instance;
        }

        if (_handlers.IsEmpty)
        {
            Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "handler_not_found");
            LogHandlerNotFound(Logger, key, GrainContext.GrainId);
            return DeliveryResult.HandlerNotFound.Instance;
        }

        Emit(new InboxCommand.Accept(message));
        await PersistForCallerAsync(cancellationToken).ConfigureAwait(true);
        Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "accepted");
        LogMessageAccepted(Logger, key, GrainContext.GrainId);
        return DeliveryResult.Accepted.Instance;
    }

    /// <summary>
    /// 暂存此 Grain 发送给自身的消息的接纳操作，供调用方的下一次日志写入提交。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同步应用 <see cref="DeliverAsync"/> 的标识规则且不进行写入：已完成的消息或
    /// 等价的待处理消息属于重复消息，而同一 ID 下不同的待处理命令构成冲突。
    /// 接纳操作与调用方其他暂存状态原子持久化，之后收件箱照常处理
    /// 消息。若消息泵尚未运行，写入为新接纳消息确保的持久化唤醒
    /// 会在激活实例开始处理调用时启动消息泵，因此处理器绝不会在激活期间运行。
    /// </para>
    /// <para>
    /// 同步发送无法报告背压或缺少处理器，因此无论
    /// <see cref="DurableMessagingOptions.MaxCapacity"/> 或处理器注册情况如何都会准入消息；若接收器对其施加背压，
    /// 则发送失败。在消息完成前，它仍占用来自其他 Grain 的投递容量，且
    /// 在处理器注册后才开始处理。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// 收件箱持有相同 ID 的不同待处理命令，或收件箱接收器对该消息施加背压，或
    /// 报告了不受支持的结果。
    /// </exception>
    internal void StageLocal(InboxMessage message)
    {
        // 复用发件箱的时钟观测值：再次读取可能观测到另一次回拨。
        var now = message.SentAt;
        // 本地发送随收件箱一起提交，因此可使用时钟回拨后首个允许准入的时间。
        if (message.SentAt <= _admissionFloor)
        {
            if (_admissionFloor == DateTimeOffset.MaxValue) throw new InvalidOperationException("The inbox admission floor leaves no representable send time for a new local message.");
            message = message with { SentAt = _admissionFloor.AddTicks(1) };
        }
        Debug.Assert(message.SentAt <= MessagingTime.AddClamped(now, Options.DeduplicationWindow)
            || message.SentAt <= _admissionFloor.AddTicks(1));
        if (IsDuplicate(message, out _))
        {
            Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "duplicate");
            return;
        }

        if (StageSinkAcceptance(message) is { } sinkResult)
        {
            if (sinkResult is DeliveryResult.Backpressured)
            {
                throw new InvalidOperationException(
                    $"An inbox message sink backpressured message '{message.Envelope.MessageId}', which the grain sent to itself.");
            }

            return;
        }

        try
        {
            Emit(new InboxCommand.Accept(message));
        }
        catch (Exception exception)
        {
            LogStagingFailed(Logger, exception);
            _pump.Fail(exception, "Inbox staging failed.");
            throw;
        }

        Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "accepted");
        LogMessageAccepted(Logger, message.Envelope.MessageId, GrainContext.GrainId);
    }

    /// <summary>
    /// 将消息提供给已注册的接收器，并暂存第一个接收该消息的接收器所报告的结果。
    /// </summary>
    /// <returns>投递结果，若每个接收器均拒绝接手该消息则为 <see langword="null"/>。</returns>
    /// <remarks>
    /// 已接纳消息只将其已处理记录记入日志，并与接收器暂存的变更在同一次写入中提交。
    /// 重复或受背压的消息不暂存任何内容。拒绝消息的接收器本身不会暂存内容，因此
    /// 收件箱将拒绝记录为消息的已处理结果。接收器异常或不受支持的
    /// 结果使处理结果处于未知状态：异常传播至调用方，不暂存任何内容，因此发送方重试。
    /// </remarks>
    /// <exception cref="InvalidOperationException">接收器报告了不受支持的投递结果。</exception>
    private DeliveryResult? StageSinkAcceptance(InboxMessage message)
    {
        var key = message.Envelope.MessageId;
        foreach (var sink in _sinks)
        {
            if (!sink.TryAccept(message, out var result)) continue;
            DeliveryResult.DeadLettered? rejection = null;
            switch (result)
            {
                case DeliveryResult.Duplicate or DeliveryResult.Backpressured:
                    Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, result is DeliveryResult.Duplicate ? "duplicate" : "backpressured");
                    return result;
                case DeliveryResult.DeadLettered deadLettered:
                    rejection = deadLettered;
                    break;
                case DeliveryResult.Accepted:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The inbox message sink reported the unsupported delivery result '{result.Value?.GetType().Name ?? "none"}'.");
            }

            var accepted = rejection is null;
            try
            {
                if (rejection is null)
                {
                    StageCompletion(message, deadLetter: null);
                }
                else
                {
                    LogSinkRejected(Logger, key, GrainContext.GrainId, rejection.Reason, rejection.Code);
                    StageCompletion(message, deadLetter: null, new() { Reason = rejection.Reason, Code = rejection.Code });
                }
            }
            catch (Exception exception)
            {
                // 接收器暂存的变更无法在没有已处理记录的情况下提交。
                LogStagingFailed(Logger, exception);
                _pump.Fail(exception, "Inbox staging failed.");
                throw;
            }

            Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, accepted ? "accepted" : "dead_lettered");
            Shared.Instruments.OnInboxMessageProcessed(_pump.GrainTypeTag, accepted ? "success" : "dead_lettered");
            if (rejection is not null) return rejection;
            LogMessageAccepted(Logger, key, GrainContext.GrainId);
            return DeliveryResult.Accepted.Instance;
        }

        return null;
    }

    /// <summary>
    /// 识别保留或待处理的标识；不论本次投递的发送时间为何，均保留其已记录的发送时间。
    /// </summary>
    /// <param name="message">接收的消息。</param>
    /// <param name="pending">待处理条目；若消息已完成或是新消息，则为 <see langword="null"/>。</param>
    private bool IsDuplicate(InboxMessage message, out InboxEntry? pending)
    {
        var key = message.Envelope.MessageId;
        if (_processed.ContainsKey(key))
        {
            pending = null;
            return true;
        }

        if (_messages.TryGetValue(key, out pending))
        {
            if (!EnvelopeEquivalence.AreSameCommand(pending.Message, message))
            {
                throw new InvalidOperationException(
                    $"The inbox already contains a different command with message ID '{key}'.");
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 处理一批已到期的持久化消息，并返回是否处理了任何消息。
    /// </summary>
    internal async ValueTask<bool> ProcessReadyAsync(CancellationToken cancellationToken)
    {
        if (_handlers.IsEmpty || _messages.Count == 0)
        {
            return false;
        }

        var jobNow = Shared.JobTimeProvider.GetUtcNow();
        var batch = new List<InboxEntry>(Math.Min(Options.InboxBatchSize, _messages.Count));
        foreach (var entry in _messages.Values)
        {
            if (IsDurable(entry) && GetDelay(entry, jobNow) == TimeSpan.Zero)
            {
                batch.Add(entry);
                if (batch.Count == Options.InboxBatchSize) break;
            }
        }

        if (batch.Count == 0)
        {
            return false;
        }

        foreach (var entry in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessAsync(entry, cancellationToken).ConfigureAwait(true);
        }

        await PersistAsync().ConfigureAwait(true);
        return true;
    }

    private async ValueTask ProcessAsync(InboxEntry entry, CancellationToken cancellationToken)
    {
        if (!IsCurrent(entry))
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var execution = new HandlerExecution(this, entry) { Active = true };
        try
        {
            await _handlers.HandleAsync(new InboxHandlerContext(entry.Message, execution), cancellationToken).ConfigureAwait(true);
            execution.ThrowIfRejected();
            if (!execution.Completed)
            {
                throw new InvalidOperationException("Inbox handlers must call Complete or Fail before returning successfully.");
            }
        }
        catch (Exception) when (execution.Rejection is { } rejection)
        {
            _pump.Fail(rejection.SourceException, "Inbox operation failed.");
            rejection.Throw();
        }
        catch (Exception exception) when (execution.Completed)
        {
            LogHandlerException(Logger, exception, entry.Message.Envelope.MessageId, GrainContext.GrainId);
        }
        catch (Exception exception)
        {
            // 此次尝试结束时没有得到结果。待提交的变更共用同一个日志缓冲区，因此任何写入都会提交
            // 该尝试暂存的变更；使激活实例失败可丢弃这些变更，恢复时将该尝试计入失败并再次运行
            // 该消息的处理过程。
            var failure = new InvalidOperationException(
                $"The inbox handler for message '{entry.Message.Envelope.MessageId}' ended without an outcome, so the activation discards its changes.",
                exception);
            _pump.Fail(failure, "An inbox handler ended without an outcome.");
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            throw failure;
        }
        finally
        {
            execution.Active = false;
        }

        var status = execution.DeadLetterReason is null ? "success" : "dead_lettered";
        Shared.Instruments.OnInboxMessageProcessed(_pump.GrainTypeTag, status);
        Shared.Instruments.OnInboxProcessingDuration(Stopwatch.GetElapsedTime(start), _pump.GrainTypeTag);
    }

    /// <summary>
    /// 暂存中断尝试的结果：采用退避策略重试，或者在自消息首次失败起经过 <c>InboxRetryTimeout</c>
    /// 后转入死信。返回用于指标记录的处理状态。
    /// </summary>
    /// <remarks>
    /// 超时到期时运行最后一次尝试，<c>Fail(reason)</c> 可立即将消息转入死信。重试
    /// 截止时间遵循 Durable Jobs 时钟，由它安排下一次唤醒。恢复时无法区分失败的尝试与
    /// 尚未运行便因崩溃中断的尝试，因此仅当此前的失败计数已经启动消息的重试窗口时，
    /// 本次失败计数才可能使消息转入死信。
    /// </remarks>
    private string StageInterruptedAttempt(InboxEntry entry, DateTimeOffset now)
    {
        var attempts = checked(entry.State.AttemptCount + 1);
        var expirationTime = entry.State.ExpirationTime ?? MessagingTime.AddClamped(now, Options.InboxRetryTimeout);
        if (expirationTime <= now && entry.State.ExpirationTime is not null)
        {
            StageDeadLetter(entry.Message, InterruptedAttemptReason, attempts);
            return "dead_lettered";
        }

        var retryAt = MessagingTime.AddClamped(now, MessagingTime.GetBackoff(Options.RetryInitialInterval, attempts));
        Emit(new InboxCommand.Retry(entry.Message.Envelope.MessageId, new()
        {
            AttemptCount = attempts,
            NextAttemptAt = retryAt < expirationTime ? retryAt : expirationTime,
            ExpirationTime = expirationTime
        }));
        return "retry";
    }

    private bool IsCurrent(InboxEntry entry) =>
        _messages.TryGetValue(entry.Message.Envelope.MessageId, out var current) && ReferenceEquals(current, entry);

    private void StageCompletion(InboxMessage message, InboxDeadLetterRecord? deadLetter, InboxRejection? rejection = null) =>
        Emit(new InboxCommand.Complete(message.Envelope.MessageId, message.SentAt, deadLetter, rejection));

    private void StageDeadLetter(InboxMessage message, string reason, int attemptCount)
    {
        var now = TimeProvider.GetUtcNow();
        var key = message.Envelope.MessageId;
        CompactDeadLetters(now, reservedCapacity: _deadLetters.ContainsKey(key) ? 0 : 1);
        StageCompletion(message, new InboxDeadLetterRecord
        {
            Message = message,
            DeadLetteredAt = now,
            Reason = reason,
            AttemptCount = attemptCount
        });
    }

    private bool CompactDeadLetters(DateTimeOffset now, int reservedCapacity = 0)
    {
        if (_deadLetters.Count == 0) return false;
        var retained = new Dictionary<MessageId, InboxDeadLetterRecord>(_deadLetters);
        if (!DeadLetterRetention.Compact(retained, now, Options.DeadLetterRetentionPeriod, Options.MaxRetainedDeadLetters,
            static entry => entry.DeadLetteredAt, reservedCapacity))
        {
            return false;
        }

        Emit(new InboxCommand.RemoveDeadLetters(_deadLetters.Keys.Where(key => !retained.ContainsKey(key)).ToArray()));
        return true;
    }

    internal bool RemoveDeadLetter(MessageId messageId)
    {
        _pump.ShutdownToken.ThrowIfCancellationRequested();
        if (!_deadLetters.ContainsKey(messageId)) return false;
        Emit(new InboxCommand.RemoveDeadLetters([messageId]));
        return true;
    }

    /// <summary>
    /// 返回距下一条消息或已注册唤醒到期的延迟，或返回 <see langword="null"/>（如果收件箱
    /// 和所有唤醒回调均未报告当前激活实例可执行的工作）。消息在处理器
    /// 注册前等待，不安排唤醒。
    /// </summary>
    /// <param name="jobNow">当前 Durable Jobs 时间，调用方据此安排唤醒。</param>
    internal TimeSpan? GetNextWakeupDelay(DateTimeOffset jobNow)
    {
        var next = GetRegisteredWakeupDelay();
        if (_messages.Count == 0 || _handlers.IsEmpty || next <= TimeSpan.Zero) return next;
        foreach (var entry in _messages.Values)
        {
            // 捕获紧随同步准备之后；相应唤醒保障此次写入，直至收到确认。
            if (entry.Sequence > _capturedSequence) continue;
            var delay = GetDelay(entry, jobNow);
            if (delay == TimeSpan.Zero) return delay;
            if (next is null || delay < next) next = delay;
        }

        return next;
    }

    // 回调可报告任意非正延迟以请求尽快唤醒；将其规范化为零，以确保由此计算出的到期
    // 时间仍可表示。
    private TimeSpan? GetRegisteredWakeupDelay()
    {
        TimeSpan? next = null;
        foreach (var getWakeupDelay in _wakeupSources)
        {
            if (getWakeupDelay() is not { } delay) continue;
            if (delay <= TimeSpan.Zero) return TimeSpan.Zero;
            if (next is null || delay < next) next = delay;
        }

        return next;
    }

    // DeliverAt 和重试截止时间均处于 Durable Jobs 时间域内，由其安排唤醒。
    private static TimeSpan GetDelay(InboxEntry entry, DateTimeOffset jobNow)
    {
        var dueTime = GetDueTime(entry);
        return dueTime > jobNow ? dueTime - jobNow : TimeSpan.Zero;
    }

    private static DateTimeOffset GetDueTime(InboxEntry entry)
    {
        var dueTime = entry.Message.Envelope.DeliverAt ?? DateTimeOffset.MinValue;
        return entry.State.NextAttemptAt is { } retryAt && retryAt > dueTime ? retryAt : dueTime;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var compacted = CompactDeadLetters(TimeProvider.GetUtcNow());
        if (ChargeInterruptedAttempts() | compacted)
        {
            await PersistAsync().ConfigureAwait(true);
        }

        _recovered = true;
    }

    /// <summary>
    /// 除非前一个激活实例已将干净停止记入日志，否则为每条已到期消息计入一次失败尝试，并返回
    /// 是否暂存了任何变更。
    /// </summary>
    /// <remarks>
    /// 处理器结束时没有结果的激活实例，或其 Silo 发生崩溃的激活实例，会在未记录
    /// 尝试结果的情况下停止。恢复时计入失败尝试，使退避和 <c>InboxRetryTimeout</c> 能约束每条失败路径，
    /// 而正常成功路径无需额外日志写入。崩溃也会对尚未分派的到期消息计入失败，因此
    /// 每次非干净停止可能多计一次尝试；单独计入失败绝不会使尚未开始重试窗口的消息
    /// 转入死信。失败计数在消息泵运行任何处理器之前提交。
    /// </remarks>
    private bool ChargeInterruptedAttempts()
    {
        if (_messages.Count == 0) return false;
        if (_stoppedCleanly)
        {
            // 消耗该标记，以便此激活实例之后发生的非干净停止被计入失败尝试。
            Emit(InboxCommand.Resume.Instance);
            return true;
        }

        var now = Shared.JobTimeProvider.GetUtcNow();
        var charged = 0;
        foreach (var entry in _messages.Values.ToArray())
        {
            // 尚未到期的消息不可能正在进行处理尝试。
            if (GetDelay(entry, now) != TimeSpan.Zero) continue;
            var status = StageInterruptedAttempt(entry, now);
            Shared.Instruments.OnInboxMessageProcessed(_pump.GrainTypeTag, status);
            charged++;
        }

        if (charged > 0) LogInterruptedAttemptsCharged(Logger, GrainContext.GrainId, charged);
        return charged > 0;
    }

    private const string InterruptedAttemptReason = "The activation stopped without recording the outcome of an attempt.";

    /// <summary>
    /// 将此激活实例在没有中断处理器尝试的情况下停止记入日志，因此恢复时不计入失败尝试。
    /// </summary>
    /// <remarks>
    /// 消息泵在循环结束后调用此方法。消息传递失败（例如处理器结束时没有结果）
    /// 不会记录停止标记，因此恢复时会计入该尝试。写入失败会使此次停止成为非干净停止，
    /// 最多额外计入一次尝试。
    /// </remarks>
    internal async Task RecordCleanStopAsync()
    {
        if (!_recovered || _pump.IsFailed || _stoppedCleanly || _messages.Count == 0) return;
        try
        {
            Emit(InboxCommand.CleanStop.Instance);
            await _pump.StateManager.WriteStateAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            LogCleanStopFailed(Logger, exception, GrainContext.GrainId);
        }
    }

    /// <summary>在丢弃激活实例的消息传递状态后，释放已报告的队列深度指标。</summary>
    internal void ReleaseDepth() => ReportDepth(-_messages.Count);

    private void ReportDepth(int delta) => Shared.Instruments.OnInboxDepthChanged(_pump.GrainTypeTag, delta);

    /// <summary>持久化暂存的变更，其中 <paramref name="cancellationToken"/> 仅结束调用方的等待。</summary>
    private ValueTask PersistForCallerAsync(CancellationToken cancellationToken)
    {
        var write = PersistAsync();
        if (write.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            return write;
        }

        // 调用方停止等待后，写入继续直到产生结果；PersistAsync 通过消息泵报告失败。
        var task = write.AsTask();
        task.Ignore();
        return new(task.WaitAsync(cancellationToken));
    }

    private async ValueTask PersistAsync()
    {
        try
        {
            await _pump.StateManager.WriteStateAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _pump.Fail(exception, "Inbox persistence failed.");
            throw;
        }
    }

    /// <summary>
    /// 跟踪一次处理器尝试并暂存其最终结果。
    /// </summary>
    internal sealed class HandlerExecution(Inbox owner, InboxEntry entry)
    {
        public bool Active { get; set; }

        public bool Completed { get; private set; }

        public string? DeadLetterReason { get; private set; }

        public ExceptionDispatchInfo? Rejection { get; private set; }

        public void Complete()
        {
            ValidateAttempt();
            if (Completed)
            {
                return;
            }

            try
            {
                owner.StageCompletion(entry.Message, deadLetter: null);
                Completed = true;
            }
            catch (Exception exception)
            {
                Reject(exception);
            }
        }

        public void Fail(string reason)
        {
            ValidateAttempt();
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            if (Completed)
            {
                if (string.Equals(DeadLetterReason, reason, StringComparison.Ordinal))
                {
                    return;
                }

                Reject(new InvalidOperationException("The inbox handler has already staged a different terminal outcome."));
            }

            try
            {
                owner.StageDeadLetter(entry.Message, reason, checked(entry.State.AttemptCount + 1));
                DeadLetterReason = reason;
                Completed = true;
            }
            catch (Exception exception)
            {
                Reject(exception);
            }
        }

        public void ThrowIfRejected() => Rejection?.Throw();

        [DoesNotReturn]
        public void Reject(Exception exception)
        {
            Rejection ??= ExceptionDispatchInfo.Capture(exception);
            Rejection.Throw();
        }

        private void ValidateAttempt()
        {
            ThrowIfRejected();
            if (!Active)
            {
                Reject(new InvalidOperationException("The inbox handler context belongs to an inactive or different attempt."));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Inbox command staging failed.")]
    private static partial void LogStagingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox for {ReceiverId} charged an interrupted attempt to {Count} due messages: its previous activation stopped without recording their outcome.")]
    private static partial void LogInterruptedAttemptsCharged(ILogger logger, GrainId receiverId, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox for {ReceiverId} couldn't record a clean stop, so its next activation charges one attempt to each due message.")]
    private static partial void LogCleanStopFailed(ILogger logger, Exception exception, GrainId receiverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox for {ReceiverId} expired message {MessageId}: its send time {SentAt} is at or below the durable admission floor {AdmissionFloor}. Its earlier delivery outcome is unknown.")]
    private static partial void LogMessageExpired(ILogger logger, MessageId messageId, GrainId receiverId, DateTimeOffset sentAt, DateTimeOffset admissionFloor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox for {ReceiverId} rejected message {MessageId}: its send time {SentAt} is later than receiver time {ReceiverTime} plus DeduplicationWindow.")]
    private static partial void LogSentAtTooFarAhead(ILogger logger, MessageId messageId, GrainId receiverId, DateTimeOffset sentAt, DateTimeOffset receiverTime);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No inbox handler registered for message {MessageId} to {ReceiverId}")]
    private static partial void LogHandlerNotFound(ILogger logger, MessageId messageId, GrainId receiverId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Accepted message {MessageId} to {ReceiverId}")]
    private static partial void LogMessageAccepted(ILogger logger, MessageId messageId, GrainId receiverId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler threw exception for message {MessageId} to {ReceiverId}")]
    private static partial void LogHandlerException(ILogger logger, Exception exception, MessageId messageId, GrainId receiverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inbox message sink rejected message {MessageId} to {ReceiverId} with code {Code}: {Reason}")]
    private static partial void LogSinkRejected(ILogger logger, MessageId messageId, GrainId receiverId, string reason, string? code);
}
