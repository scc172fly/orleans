
namespace Orleans
{
    /// <summary>
    /// 提供参与生命周期的钩子。
    /// 也可以作为标记接口，指示对象能够参与生命周期。
    /// </summary>
    /// <typeparam name="TLifecycleObservable">
    /// 正在观察的生命周期类型。
    /// </typeparam>
    public interface ILifecycleParticipant<TLifecycleObservable>
        where TLifecycleObservable : ILifecycleObservable
    {
        /// <summary>
        /// 将指定观察者添加为生命周期参与者。
        /// </summary>
        /// <param name="lifecycle">
        /// 观察者。
        /// </param>
        void Participate(TLifecycleObservable lifecycle);
    }
}
