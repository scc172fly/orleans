using System;

namespace Orleans
{
    /// <summary>
    /// 可观察的生命周期。
    /// 生命周期的每个阶段均可观察。在启动及停止过程中到达相应阶段时，所有观察者都会收到通知。
    /// 各阶段按升序启动，按降序停止。
    /// </summary>
    public interface ILifecycleObservable
    {
        /// <summary>
        /// 订阅通知，以便在启动或停止过程中到达指定阶段时收到通知。
        /// </summary>
        /// <param name="observerName">观察者名称，用于报告。</param>
        /// <param name="stage">要订阅的阶段。</param>
        /// <param name="observer">观察者。</param>
        /// <returns>可通过释放来取消订阅的对象。</returns>
        IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer);
    }
}
