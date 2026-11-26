
namespace Orleans
{
    /// <summary>
    /// 提供钩子来参与生命周期。
    /// 也可以充当信号界面,表明物体可以参与生命周期。
    /// </summary>
    /// <typeparam name="TLifecycleObservable">
    /// 所观测到的生命周期类型。
    /// </typeparam>
    public interface ILifecycleParticipant<TLifecycleObservable>
        where TLifecycleObservable : ILifecycleObservable
    {
        /// <summary>
        /// 加上作为生命周期参与者的被派观察员。
        /// </summary>
        /// <param name="lifecycle">
        /// 观察员
        /// </param>
        void Participate(TLifecycleObservable lifecycle);
    }
}
