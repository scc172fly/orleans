using System;

namespace Orleans
{
    /// <summary>
    /// 可见的生命周期。
    /// 生命周期的每个阶段都可观测到,所有观察员在开始和停止时将被告知该阶段何时达到。
    /// 阶段按升序开始,按降序停止。
    /// </summary>
    public interface ILifecycleObservable
    {
        /// <summary>
        /// 订定在开始或停止时到达某一阶段的通知。
        /// </summary>
        /// <param name="observerName">为报告目的,观察员的姓名。</param>
        /// <param name="stage">订阅的阶段。</param>
        /// <param name="observer">观察员</param>
        /// <returns>一种可一次性处理的可取消订阅。</returns>
        IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer);
    }
}
