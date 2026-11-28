namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>一个直接向其已注册处理程序分派消息的 <see cref="IInbox"/>。</summary>
internal sealed class TestInbox : IInbox
{
    private readonly InboxDispatcher _dispatcher = new([]);

    public bool IsEmpty => _dispatcher.IsEmpty;

    public IInbox Register(IInboxHandler handler)
    {
        _dispatcher.Add(handler);
        return this;
    }

    public IInbox Register(IInboxMessageSink sink) => throw new NotSupportedException("The test inbox dispatches to handlers only.");

    public IInbox RegisterWakeup(Func<TimeSpan?> getWakeupDelay) => throw new NotSupportedException("The test inbox dispatches to handlers only.");

    public ValueTask HandleAsync(IInboxHandlerContext context, CancellationToken cancellationToken) =>
        _dispatcher.HandleAsync(context, cancellationToken);
}

internal static class TestInboxExtensions
{
    public static ValueTask HandleAsync(this IInbox inbox, IInboxHandlerContext context, CancellationToken cancellationToken) =>
        ((TestInbox)inbox).HandleAsync(context, cancellationToken);
}
