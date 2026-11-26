
namespace Orleans
{
    /// <summary>
    /// 生命周期观察者和可见生命周期。
    /// </summary>
    public interface ILifecycleSubject : ILifecycleObservable, ILifecycleObserver
    {
    }
}
