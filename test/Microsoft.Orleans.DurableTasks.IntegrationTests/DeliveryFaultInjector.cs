using Orleans.Durable.Messaging.Diagnostics;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>
/// 在接收方收件箱中使 <paramref name="matches"/> 选定的传递尝试失败，方法是从
/// <see cref="MessagingEvents.InboxMessageReceiving"/> 抛出异常：收件箱不暂存任何内容，发送方的发件箱进行重试。
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

    /// <summary>获取注入器使其失败的传递尝试次数。</summary>
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
