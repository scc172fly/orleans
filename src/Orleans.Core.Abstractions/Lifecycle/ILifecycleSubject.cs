
namespace Orleans
{
    /// <summary>
    /// 同时是生命周期观察者和可观察的生命周期。
    /// </summary>
    public interface ILifecycleSubject : ILifecycleObservable, ILifecycleObserver
    {
    }
}
