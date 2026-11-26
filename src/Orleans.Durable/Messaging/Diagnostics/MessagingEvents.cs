using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging.Diagnostics;

/// <summary>
/// 为耐用信息发布活动提供诊断听众和事件有效载荷类型。
/// </summary>
/// <remarks>
/// <para>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断。
/// </para>
/// <para>
/// 每个事件都同步写在 启动它时的激活上, 在它描述的过渡期间,
/// 观察员在过渡时看到 grain的状态。
/// 所出现的事件; <see cref="InboxMessageReceiving"/> 记录交付如何对待。
/// </para>
/// </remarks>
public static class MessagingEvents
{
    /// <summary>耐久信息活动诊断听众的名字。</summary>
    public const string ListenerName = "Orleans.Messaging";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>获得所有持久信息活动的可观测序列。</summary>
    public static IObservable<MessagingEvent> AllEvents { get; } = new Observable();

    /// <summary>用于耐用信息诊断活动的基础等级。</summary>
    /// <param name="grainContext">启动其信箱或发件箱引发事件的信号。</param>
    public abstract class MessagingEvent(IGrainContext grainContext)
    {
        /// <summary>启动其信箱或发件箱引发事件的信号。</summary>
        public readonly IGrainContext GrainContext = grainContext;
    }

    /// <summary>
    /// 事件有效载荷,用于新交付的发送时间在收件箱接收楼层以上、收件箱前的新投送
    /// 等相电文效果。
    /// </summary>
    /// <param name="grainContext">接收启动。</param>
    /// <param name="message">传递的信息。</param>
    /// <remarks>
    /// 观察员提出的例外,与收信箱信息汇槽例外:收件箱一样,未能完成送货尝试。
    /// 发送者发件人发件箱重写信息,用来测试投送故障。
    /// </remarks>
    public sealed class InboxMessageReceiving(IGrainContext grainContext, InboxMessage message) : MessagingEvent(grainContext)
    {
        /// <summary>传递的信息。</summary>
        public readonly InboxMessage Message = message;
    }

    /// <summary>
    /// 重试发件箱的失败送货尝试事件有效载荷
    /// </summary>
    /// <param name="grainContext">发送激活。</param>
    /// <param name="messageId">消息的代号。</param>
    /// <param name="receiverId">接受者 grain.</param>
    /// <param name="attemptCount">迄今为止,交付尝试失败的次数。</param>
    /// <param name="nextAttemptAt">时间,在“持久工作钟”上,发信箱再次尝试从中交付。</param>
    /// <param name="error">尝试失败了</param>
    public sealed class OutboxRetryScheduled(
        IGrainContext grainContext,
        MessageId messageId,
        GrainId receiverId,
        int attemptCount,
        DateTimeOffset nextAttemptAt,
        string error) : MessagingEvent(grainContext)
    {
        /// <summary>消息的代号。</summary>
        public readonly MessageId MessageId = messageId;

        /// <summary>接受者 grain.</summary>
        public readonly GrainId ReceiverId = receiverId;

        /// <summary>迄今为止,交付尝试失败的次数。</summary>
        public readonly int AttemptCount = attemptCount;

        /// <summary>时间,在“持久工作钟”上,发信箱再次尝试从中交付。</summary>
        public readonly DateTimeOffset NextAttemptAt = nextAttemptAt;

        /// <summary>尝试失败了</summary>
        public readonly string Error = error;
    }

    /// <summary>
    /// 发送盒退出后所制作的发箱信息输出箱事件有效载荷:电文已发送,接收器
    /// 或以妄言否认它,或以妄言定真理。
    /// </summary>
    /// <param name="grainContext">发送激活。</param>
    /// <param name="messageId">消息的代号。</param>
    /// <param name="retirement">消息如何退出。</param>
    /// <remarks>
    /// 使用发送 grain的下一期日记写时承诺退休。通知了引用信件的来源
    /// 首先,在观察员运行时,将对其退休情况进行修改。
    /// </remarks>
    public sealed class OutboxMessageRetired(
        IGrainContext grainContext,
        MessageId messageId,
        OutboxMessageRetirement retirement) : MessagingEvent(grainContext)
    {
        /// <summary>消息的代号。</summary>
        public readonly MessageId MessageId = messageId;

        /// <summary>消息如何退出。</summary>
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
