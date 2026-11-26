
namespace Orleans.Runtime
{
    /// <summary>
    /// 观察 silo 生命周期和观察员。
    /// </summary>
    public interface ISiloLifecycleSubject : ISiloLifecycle, ILifecycleObserver
    {
    }
}
