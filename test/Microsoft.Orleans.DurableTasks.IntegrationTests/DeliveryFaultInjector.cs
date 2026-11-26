using Orleans.Durable.Messaging.Diagnostics;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 无法通过从接收箱丢弃 <paramref name="matches"/> 选择的投送尝试,
/// <see cref="MessagingEvents.InboxMessageReceiving"/>:收件箱没有阶段,发件人发件人发件人发件人发件人发送的发件人发件人发件人发件人发件人电报。
/// </summary>
internal sealed class DeliveryFaultInjector : IObserver<MessagingEvents.MessagingEvent>, IDisposable
{
    private readonly Func<MessagingEvents.InboxMessageReceiving, bool> _matches;
    private readonly string _error;
    private readonly IDisposable _subscription;
    private int _count;

    public DeliveryFaultInjector(Func<MessagingEvents.InboxMessageReceiving, bool> matches, string error)
    {
        (_matches, _error) = (matches, error);
        _subscription = MessagingEvents.AllEvents.Subscribe(this);
    }

    /// <summary>获取注射器失败的投递次数。</summary>
    public int Count => Volatile.Read(ref _count);

    public void OnNext(MessagingEvents.MessagingEvent value)
    {
        if (value is MessagingEvents.InboxMessageReceiving receiving && _matches(receiving))
        {
            Interlocked.Increment(ref _count);
            throw new InvalidOperationException(_error);
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Dispose() => _subscription.Dispose();
}
