
namespace Orleans.Runtime
{
    /// <summary>
    /// 可观察的 Silo 生命周期及观察者。
    /// </summary>
    public interface ISiloLifecycleSubject : ISiloLifecycle, ILifecycleObserver
    {
    }
}
