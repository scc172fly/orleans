namespace Grains;

public class ObserverManager<TObserver> where TObserver : notnull
{
    private readonly Dictionary<TObserver, DateTime> _observers = new();
    private readonly TimeSpan _expiration;

    public ObserverManager(TimeSpan expiration) => _expiration = expiration;

    public int Count => _observers.Count;
    public void Clear() => _observers.Clear();
    public void Subscribe(TObserver observer) => _observers[observer] = DateTime.UtcNow;
    public void Unsubscribe(TObserver subscriber) => _observers.Remove(subscriber, out _);
    public void Notify(Action<TObserver> notification)
    {
        ClearExpired();
        foreach (var observer in _observers)
        {
            try
            {
                notification(observer.Key);
            }
            catch (Exception)
            {
                // 这也许应该记录下来
            }
        }
    }

    private void ClearExpired()
    {
        var now = DateTime.UtcNow;
        var defunct = default(List<TObserver>);
        foreach (var observer in _observers)
        {
            if (observer.Value + _expiration < now)
            {
                // 将取消过期观察员。
                defunct ??= new List<TObserver>();
                defunct.Add(observer.Key);
            }
        }

        // 删除已关闭的观察者 。
        if (defunct is { Count: > 0 })
        {
            foreach (var observer in defunct)
            {
                _observers.Remove(observer, out _);
            }
        }
    }
}