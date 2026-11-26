using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Messaging;

/// <summary>注册输入了 <see cref="IInbox"/>的标处理器。</summary>
/// <remarks>州级超负荷人口直接储存其论点,并支持静态代表。</remarks>
public static class InboxExtensions
{
    /// <summary>注册同步主题处理器。</summary>
    /// <typeparam name="T">体力合同。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题和序列装订。</param>
    /// <param name="handler">同步体和背景代表</param>
    /// <returns>收件箱。</returns>
    public static IInbox Register<T>(this IInbox inbox, MessageType<T> messageType, Action<T, IInboxHandlerContext> handler)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        return inbox.Register(new SynchronousHandler<T>(messageType, handler));
    }

    /// <summary>注册同步主题处理器,并有明确的参数。</summary>
    /// <typeparam name="T">体力合同。</typeparam>
    /// <typeparam name="TArg">处理器参数类型。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题和序列装订。</param>
    /// <param name="argument">与处理器一起存储的参数。</param>
    /// <param name="handler">同步体、辩论和背景代表。</param>
    /// <returns>收件箱。</returns>
    /// <remarks>使用静态代表到通过州,而不进行记录关闭。</remarks>
    public static IInbox Register<T, TArg>(
        this IInbox inbox, MessageType<T> messageType, TArg argument, Action<T, TArg, IInboxHandlerContext> handler)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        return inbox.Register(new SynchronousHandler<T, TArg>(messageType, argument, handler));
    }

    /// <summary>登记一个主体处理器,支持非同步的准备工作。</summary>
    /// <typeparam name="T">体力合同。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题和序列装订。</param>
    /// <param name="handler">身体、背景和主动代表。</param>
    /// <returns>收件箱。</returns>
    public static IInbox Register<T>(
        this IInbox inbox, MessageType<T> messageType, Func<T, IInboxHandlerContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        return inbox.Register(new AsynchronousHandler<T>(messageType, handler));
    }

    /// <summary>登记一个非同步的主题处理器,并有明确的论点。</summary>
    /// <typeparam name="T">体力合同。</typeparam>
    /// <typeparam name="TArg">处理器参数类型。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题和序列装订。</param>
    /// <param name="argument">与处理器一起存储的参数。</param>
    /// <param name="handler">身体、论点、背景和未遂代表。</param>
    /// <returns>收件箱。</returns>
    /// <remarks>使用静态代表到通过州,而不进行记录关闭。</remarks>
    public static IInbox Register<T, TArg>(
        this IInbox inbox, MessageType<T> messageType, TArg argument,
        Func<T, TArg, IInboxHandlerContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        return inbox.Register(new AsynchronousHandler<T, TArg>(messageType, argument, handler));
    }

    private abstract class Handler<T>(MessageType<T> messageType) : IInboxHandler
    {
        public bool CanHandle(InboxMessage message) =>
            string.Equals(message.Envelope.Subject, messageType.Subject, StringComparison.Ordinal);

        public ValueTask HandleAsync(IInboxHandlerContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 该代表拥有该代号机构,包括在解码时取消的登记。
            var body = messageType.Decode(context.Message.Envelope);
            return Invoke(body, context, cancellationToken);
        }

        protected abstract ValueTask Invoke(T body, IInboxHandlerContext context, CancellationToken cancellationToken);
    }

    private sealed class SynchronousHandler<T>(
        MessageType<T> messageType, Action<T, IInboxHandlerContext> handler) : Handler<T>(messageType)
    {
        protected override ValueTask Invoke(T body, IInboxHandlerContext context, CancellationToken cancellationToken)
        {
            handler(body, context);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SynchronousHandler<T, TArg>(
        MessageType<T> messageType, TArg argument, Action<T, TArg, IInboxHandlerContext> handler) : Handler<T>(messageType)
    {
        protected override ValueTask Invoke(T body, IInboxHandlerContext context, CancellationToken cancellationToken)
        {
            handler(body, argument, context);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsynchronousHandler<T>(
        MessageType<T> messageType,
        Func<T, IInboxHandlerContext, CancellationToken, ValueTask> handler) : Handler<T>(messageType)
    {
        protected override ValueTask Invoke(T body, IInboxHandlerContext context, CancellationToken cancellationToken) =>
            handler(body, context, cancellationToken);
    }

    private sealed class AsynchronousHandler<T, TArg>(
        MessageType<T> messageType, TArg argument,
        Func<T, TArg, IInboxHandlerContext, CancellationToken, ValueTask> handler) : Handler<T>(messageType)
    {
        protected override ValueTask Invoke(T body, IInboxHandlerContext context, CancellationToken cancellationToken) =>
            handler(body, argument, context, cancellationToken);
    }
}
