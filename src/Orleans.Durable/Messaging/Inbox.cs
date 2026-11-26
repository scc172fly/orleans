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
/// 接受、复制和处理一个 grain的耐久收到的信息。
/// </summary>
/// <remarks>
/// 所有州级转换都在启动调度器上运行 所以每个检查 - 测试 - 测试 - 测试序列都是原子
/// 只有在接受时间持久且可持续、 <see cref="Envelope.DeliverAt"/>,
/// 如有,已通过。
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

        // 送货只到达一个收件箱 与日记恢复, 所以激活永远无法解决
        // 信箱没有信息扩展名 。
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

        // 等待处理者的消息未提醒他们注意,
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
        // 记录的结果和到期决定 都活下来了 时钟倒回, 所以只有时间 收件箱会承认 受约束。
        if (message.SentAt > MessagingTime.AddClamped(receiverTime, Options.DeduplicationWindow)
            && !_processed.ContainsKey(key)
            && !_messages.ContainsKey(key)
            && message.SentAt > GetAdmissionFloor(receiverTime))
        {
            LogSentAtTooFarAhead(Logger, key, GrainContext.GrainId, message.SentAt, receiverTime);
            throw new ArgumentOutOfRangeException(nameof(message), message.SentAt,
                "SentAt must not be later than the receiver's clock plus DeduplicationWindow.");
        }

        // 发送是 grain 呼叫,因此激活就是服务请求。
        _pump.EnsureStarted();
        if (IsDuplicate(message, out var pending))
        {
            var rejection = pending is null ? _processed[key].Rejection : null;
            // 发送者一旦退回副本,可将其副本退退发,因此接受或处理的记录必须
            // 处理者或水槽完成的完成过程仍可飞行。
            if (pending is null ? !AreCompletionsDurable : !IsDurable(pending)) await PersistForCallerAsync(cancellationToken).ConfigureAwait(true);
            if (pending is not null) _pump.Signal();

            Shared.Instruments.OnInboxMessageReceived(_pump.GrainTypeTag, "duplicate");

            // 发送者,如果答错会再次发回,则收到拒绝,而一个水槽首先回答。
            return rejection is not null
                ? new DeliveryResult.DeadLettered(rejection.Reason, rejection.Code)
                : DeliveryResult.Duplicate.Instance;
        }

        // 过期前的留存身份:遗失确认书可能属于待完成或已完成的工作。
        // 只有持久的、非降级的楼层才能忘记证据,而不论晚间是否出现动议。
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

        // 由Sink所有的信息从不占用收件箱容量,不需要操作者。
        // 所以除了后压之外,每件结果 都会在写作持久之后报告
        if (StageSinkAcceptance(message) is { } sinkResult)
        {
            if (sinkResult is not DeliveryResult.Backpressured) await PersistForCallerAsync(cancellationToken).ConfigureAwait(true);
            return sinkResult;
        }

        // 每一个待定信息,不论是否排定,在完成之前都具有能力。
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
    /// 阶段接受此 grain 发送给自己的信件, 以便调用者下一期日记写入 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同步和不以书面形式适用 <see cref="DeliverAsync"/> 的身份规则:填写或
    /// 等同的待处理信件是重复的, 而与相同身份冲突不同的命令则是一个待处理的命令 。
    /// 接受以原子方式成为持久的 与调用者的其他阶段状态, 之后的收件箱程序
    /// 信息和往常一样。 当泵尚未运行时, 写作保证新的持久提醒
    /// 一旦启动服务电话响起, 即开始接收, 所以操作者在激活期间不会运行 。
    /// </para>
    /// <para>
    /// 同步发送无法报告后压或缺少处理器, 因此信件无论是否被接受
    /// <see cref="DurableMessagingOptions.MaxCapacity"/> 和有压力的水槽,
    /// 信息仍然占据着从其他用户发送信息的能力 grains 直至其完成,
    /// 处理等待处理器注册。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// 收件箱持有不同的命令,等待的命令与同一ID相同,或者信箱槽回压电文或
    /// 报告了不支持的结果 。
    /// </exception>
    internal void StageLocal(InboxMessage message)
    {
        // 重新使用发信箱的时钟观察:新读者可以看到又一个回落。
        var now = message.SentAt;
        // 本地发送发送带有收件箱, 这样它就可以使用时间在时钟倒转后第一次被接纳的时间 。
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
    /// 向已登记的汇提供信息,并分期处理第一个接收信息报告的汇的结果。
    /// </summary>
    /// <returns>交付结果,或 <see langword="null"/> 当每个水槽都掉落信息的时候</returns>
    /// <remarks>
    /// 一份被接受的信息日记仅记录其经处理的记录,其中写明汇的分阶段变化。
    /// 重复或后压信息信息不进行任何阶段。拒绝信息的信息汇槽本身不进行任何阶段,所以
    /// 框中将拒绝记录为信件处理的结果。
    /// 结果是,结果未知:它向打电话者传播,没有任何预谋,所以发送者重试。
    /// </remarks>
    /// <exception cref="InvalidOperationException">汇报告了一个无支持的交付结果。</exception>
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
                // 水槽的分阶段改变如果没有经过处理的记录是无法进行的。
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
    /// 承认保留身份或待定身份,不论发送时间长短,其记录发件时间都保持不变。
    /// </summary>
    /// <param name="message">收到的信息。</param>
    /// <param name="pending">待处理条目,或电文填写完毕或为新时的 <see langword="null"/> 。</param>
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
    /// 处理一批耐用、应发信息,并回复是否处理了任何信息。
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
            // 尝试最终没有结果。 在修改之前, 共享一个日记缓冲, 所以任何写作都会承诺
            // 启动程序失败,启动程序被丢弃,回收程序被收取了费用,运行了
            // 再次发送消息 。
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
    /// 尝试结果中断的阶段: 以后退重试, 或一旦 <c>InboxRetryTimeout</c> 出现废信
    /// 信件第一次失败后已过时间。 返回度量处理状态 。
    /// </summary>
    /// <remarks>
    /// 最后一次尝试是在超时过后进行, <c>Fail(reason)</c> 立即将信件写为已读信件。
    /// 恢复无法看出失败的尝试
    /// 在一个坠机中断的 坠机在它运行之前, 所以电荷充电 死信只发出一个消息 其窗口较早
    /// 收费开始。
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
    /// 返回返回延迟到下一条消息或已登记的提醒到期之前,或者当信箱中无输入时返回 <see langword="null"/>
    /// 也没有任何提醒回调报告此激活可以执行的工作。 信件等待不响直到处理者
    /// 被注册。
    /// </summary>
    /// <param name="jobNow">当前的持久工作时间, 调用者使用此时间来安排唤醒时间 。</param>
    internal TimeSpan? GetNextWakeupDelay(DateTimeOffset jobNow)
    {
        var next = GetRegisteredWakeupDelay();
        if (_messages.Count == 0 || _handlers.IsEmpty || next <= TimeSpan.Zero) return next;
        foreach (var entry in _messages.Values)
        {
            // 抓捕是在同步准备之后进行的;它的醒悟覆盖了写作直到确认。
            if (entry.Sequence > _capturedSequence) continue;
            var delay = GetDelay(entry, jobNow);
            if (delay == TimeSpan.Zero) return delay;
            if (next is null || delay < next) next = delay;
        }

        return next;
    }

    // 反馈可报告任何非积极的延迟,要求迅速提醒;将正常调为零,以便到期
    // 由此得出的时间可以代表。
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

    // DeliverAt 并重试最后期限在持久工作时间域内,
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
    /// 除非前一次激活时将一个干净的停止点编辑并返回
    /// 或预设任何事物。
    /// </summary>
    /// <remarks>
    /// 其处理器在无结果的情况下结束的启动,或其 silo 撞车停止而没有记录
    /// 复元时充电后退, <c>InboxRetryTimeout</c> 将每个失败路径都绑定
    /// 坠机时还收取未发送的应答信件,所以
    /// 单收费,绝不是死信 其回试窗口的信息
    /// 还没有开始,电荷在泵启动之前就已经爆炸了
    /// </remarks>
    private bool ChargeInterruptedAttempts()
    {
        if (_messages.Count == 0) return false;
        if (_stoppedCleanly)
        {
            // 装上标记,所以这个引爆装置的一站不干净 就会被引爆
            Emit(InboxCommand.Resume.Instance);
            return true;
        }

        var now = Shared.JobTimeProvider.GetUtcNow();
        var charged = 0;
        foreach (var entry in _messages.Values.ToArray())
        {
            // 不该传递的信息 根本不可能是企图
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
    /// 记录显示这次启动行动停止了 没有操作者试图中断 所以没有追索费
    /// </summary>
    /// <remarks>
    /// 泵在循环结束后就叫它这个。信息失败,比如一个处理器,没有结果的处理器,
    /// 失灵的写作让停站变得不干净,
    /// 最多是一次额外的尝试。
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

    /// <summary>一旦启动的信号状态被丢弃, 报告的深度就会释放。</summary>
    internal void ReleaseDepth() => ReportDepth(-_messages.Count);

    private void ReportDepth(int delta) => Shared.Instruments.OnInboxDepthChanged(_pump.GrainTypeTag, delta);

    /// <summary>常态的改变, 而 <paramref name="cancellationToken"/> 只结束呼叫者的等待。</summary>
    private ValueTask PersistForCallerAsync(CancellationToken cancellationToken)
    {
        var write = PersistAsync();
        if (write.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            return write;
        }

        // 写作在电话停止等待后继续其结果; PersistAsync 报告泵故障。
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
    /// 跟踪一个处理者尝试并分阶段其最终结果。
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
