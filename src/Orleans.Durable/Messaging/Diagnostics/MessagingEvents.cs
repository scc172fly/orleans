using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging.Diagnostics;

/// <summary>
/// 提供持久化消息事件的诊断侦听器及事件有效载荷类型。
/// </summary>
/// <remarks>
/// <para>
/// 这些类型是公开的，但可能在次要版本之间发生变化。它们适用于高级场景，例如
/// 仿真测试和诊断。
/// </para>
/// <para>
/// 每个事件都在引发该事件的激活实例上，于其所描述的状态转换点同步写入，因此
/// 观察者能够看到该转换点上的 Grain 状态。观察者抛出的异常将传播至
/// 引发事件的代码；<see cref="InboxMessageReceiving"/> 说明了投递操作如何处理此类异常。
/// </para>
/// </remarks>
public static class MessagingEvents
{
    /// <summary>持久化消息事件的诊断侦听器名称。</summary>
    public const string ListenerName = "Orleans.Messaging";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>获取所有持久化消息事件的可观察序列。</summary>
    public static IObservable<MessagingEvent> AllEvents { get; } = new Observable();

    /// <summary>持久化消息诊断事件的基类。</summary>
    /// <param name="grainContext">其收件箱或发件箱引发该事件的激活实例。</param>
    public abstract class MessagingEvent(IGrainContext grainContext)
    {
        /// <summary>其收件箱或发件箱引发该事件的激活实例。</summary>
        public readonly IGrainContext GrainContext = grainContext;
    }

    /// <summary>
    /// 发送时间高于收件箱准入下限的新投递的事件有效载荷，在收件箱
    /// 暂存消息产生的变更之前发出。
    /// </summary>
    /// <param name="grainContext">接收方激活实例。</param>
    /// <param name="message">被投递的消息。</param>
    /// <remarks>
    /// 观察者抛出的异常与收件箱消息接收器抛出的异常一样，会导致投递尝试失败：收件箱
    /// 不暂存任何变更，发送方发件箱重试该消息。测试利用这一机制注入投递故障。
    /// </remarks>
    public sealed class InboxMessageReceiving(IGrainContext grainContext, InboxMessage message) : MessagingEvent(grainContext)
    {
        /// <summary>被投递的消息。</summary>
        public readonly InboxMessage Message = message;
    }

    /// <summary>
    /// 投递尝试失败且发件箱已暂存重试时的事件有效载荷。
    /// </summary>
    /// <param name="grainContext">发送方激活实例。</param>
    /// <param name="messageId">消息 ID。</param>
    /// <param name="receiverId">接收方 Grain。</param>
    /// <param name="attemptCount">截至目前投递尝试失败的次数。</param>
    /// <param name="nextAttemptAt">发件箱可再次尝试投递的最早时间，采用 Durable Jobs 时钟。</param>
    /// <param name="error">此次尝试的失败信息。</param>
    public sealed class OutboxRetryScheduled(
        IGrainContext grainContext,
        MessageId messageId,
        GrainId receiverId,
        int attemptCount,
        DateTimeOffset nextAttemptAt,
        string error) : MessagingEvent(grainContext)
    {
        /// <summary>消息 ID。</summary>
        public readonly MessageId MessageId = messageId;

        /// <summary>接收方 Grain。</summary>
        public readonly GrainId ReceiverId = receiverId;

        /// <summary>截至目前投递尝试失败的次数。</summary>
        public readonly int AttemptCount = attemptCount;

        /// <summary>发件箱可再次尝试投递的最早时间，采用 Durable Jobs 时钟。</summary>
        public readonly DateTimeOffset NextAttemptAt = nextAttemptAt;

        /// <summary>此次尝试的失败信息。</summary>
        public readonly string Error = error;
    }

    /// <summary>
    /// 发件箱已暂存消息退出操作时的事件有效载荷：该消息已投递、被接收方
    /// 拒绝，或已由发件箱转入死信。
    /// </summary>
    /// <param name="grainContext">发送方激活实例。</param>
    /// <param name="messageId">消息 ID。</param>
    /// <param name="retirement">消息退出发件箱的方式。</param>
    /// <remarks>
    /// 退出操作随发送方 Grain 的下一次日志写入提交。引用消息的来源会先收到通知，
    /// 因此观察者运行时，该来源针对退出操作的变更已暂存。
    /// </remarks>
    public sealed class OutboxMessageRetired(
        IGrainContext grainContext,
        MessageId messageId,
        OutboxMessageRetirement retirement) : MessagingEvent(grainContext)
    {
        /// <summary>消息 ID。</summary>
        public readonly MessageId MessageId = messageId;

        /// <summary>消息退出发件箱的方式。</summary>
        public readonly OutboxMessageRetirement Retirement = retirement;
    }

    internal static void EmitInboxMessageReceiving(IGrainContext grainContext, InboxMessage message)
    {
        if (!Listener.IsEnabled(nameof(InboxMessageReceiving)))
        {
            return;
        }

        Emit(grainContext, message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, InboxMessage message) =>
            Write(nameof(InboxMessageReceiving), new InboxMessageReceiving(grainContext, message));
    }

    internal static void EmitOutboxRetryScheduled(
        IGrainContext grainContext,
        MessageId messageId,
        GrainId receiverId,
        int attemptCount,
        DateTimeOffset nextAttemptAt,
        string error)
    {
        if (!Listener.IsEnabled(nameof(OutboxRetryScheduled)))
        {
            return;
        }

        Emit(grainContext, messageId, receiverId, attemptCount, nextAttemptAt, error);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(
            IGrainContext grainContext,
            MessageId messageId,
            GrainId receiverId,
            int attemptCount,
            DateTimeOffset nextAttemptAt,
            string error) =>
            Write(
                nameof(OutboxRetryScheduled),
                new OutboxRetryScheduled(grainContext, messageId, receiverId, attemptCount, nextAttemptAt, error));
    }

    internal static void EmitOutboxMessageRetired(IGrainContext grainContext, MessageId messageId, OutboxMessageRetirement retirement)
    {
        if (!Listener.IsEnabled(nameof(OutboxMessageRetired)))
        {
            return;
        }

        Emit(grainContext, messageId, retirement);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, MessageId messageId, OutboxMessageRetirement retirement) =>
            Write(nameof(OutboxMessageRetired), new OutboxMessageRetired(grainContext, messageId, retirement));
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Event payloads are sealed types whose public fields and properties the generic annotation preserves, and subscribers consume the typed payload rather than reflecting over the members of the values it carries.")]
    private static void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TEvent>(string name, TEvent payload)
        where TEvent : MessagingEvent => Listener.Write(name, payload);

    private sealed class Observable : IObservable<MessagingEvent>
    {
        public IDisposable Subscribe(IObserver<MessagingEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<MessagingEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();

            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is MessagingEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
