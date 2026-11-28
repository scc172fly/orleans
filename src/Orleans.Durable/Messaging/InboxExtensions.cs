using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Messaging;

/// <summary>向 <see cref="IInbox"/> 注册强类型主题处理器。</summary>
/// <remarks>带状态的重载直接存储参数，并支持静态委托。</remarks>
public static class InboxExtensions
{
    /// <summary>注册同步主题处理器。</summary>
    /// <typeparam name="T">正文契约。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题与序列化器的绑定。</param>
    /// <param name="handler">接收消息正文和上下文的同步委托。</param>
    /// <returns>收件箱。</returns>
    public static IInbox Register<T>(this IInbox inbox, MessageType<T> messageType, Action<T, IInboxHandlerContext> handler)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        return inbox.Register(new SynchronousHandler<T>(messageType, handler));
    }

    /// <summary>注册带显式参数的同步主题处理器。</summary>
    /// <typeparam name="T">正文契约。</typeparam>
    /// <typeparam name="TArg">处理器参数的类型。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题与序列化器的绑定。</param>
    /// <param name="argument">与处理器一起存储的参数。</param>
    /// <param name="handler">接收正文、参数和上下文的同步委托。</param>
    /// <returns>收件箱。</returns>
    /// <remarks>使用静态委托传递状态，无需捕获闭包。</remarks>
    public static IInbox Register<T, TArg>(
        this IInbox inbox, MessageType<T> messageType, TArg argument, Action<T, TArg, IInboxHandlerContext> handler)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        return inbox.Register(new SynchronousHandler<T, TArg>(messageType, argument, handler));
    }

    /// <summary>注册支持异步准备的主题处理器。</summary>
    /// <typeparam name="T">正文契约。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题与序列化器的绑定。</param>
    /// <param name="handler">接收正文、上下文和尝试取消令牌的委托。</param>
    /// <returns>收件箱。</returns>
    public static IInbox Register<T>(
        this IInbox inbox, MessageType<T> messageType, Func<T, IInboxHandlerContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        return inbox.Register(new AsynchronousHandler<T>(messageType, handler));
    }

    /// <summary>注册带显式参数的异步主题处理器。</summary>
    /// <typeparam name="T">正文契约。</typeparam>
    /// <typeparam name="TArg">处理器参数的类型。</typeparam>
    /// <param name="inbox">收件箱。</param>
    /// <param name="messageType">主题与序列化器的绑定。</param>
    /// <param name="argument">与处理器一起存储的参数。</param>
    /// <param name="handler">接收正文、参数、上下文和尝试取消令牌的委托。</param>
    /// <returns>收件箱。</returns>
    /// <remarks>使用静态委托传递状态，无需捕获闭包。</remarks>
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
            // 委托拥有已解码的正文，即使取消在解码期间到达也是如此。
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
