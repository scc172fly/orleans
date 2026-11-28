
namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="ILifecycleParticipant{TLifecycleObservable}"/> 的扩展方法。
    /// </summary>
    public static class LifecycleParticipantExtensions
    {
        /// <summary>
        /// 使为参与任意 <see cref="ILifecycleObservable"/> 而编写的组件也能参与特定的生命周期。
        /// </summary>
        /// <typeparam name="TLifecycle">目标生命周期观察者的类型。</typeparam>
        /// <param name="participant">生命周期参与者。</param>
        /// <returns>包装 <paramref name="participant"/> 且实现 <see cref="ILifecycleParticipant{TLifecycle}"/> 的适配器。</returns>
        public static ILifecycleParticipant<TLifecycle> ParticipateIn<TLifecycle>(this ILifecycleParticipant<ILifecycleObservable> participant)
            where TLifecycle : ILifecycleObservable
        {
            return new Bridge<TLifecycle>(participant);
        }

        /// <summary>
        /// 将一个生命周期参与者适配为另一种观察者类型的生命周期参与者。
        /// </summary>
        /// <typeparam name="TLifecycle"></typeparam>
        private class Bridge<TLifecycle> : ILifecycleParticipant<TLifecycle>
            where TLifecycle : ILifecycleObservable
        {
            private readonly ILifecycleParticipant<ILifecycleObservable> participant;
            public Bridge(ILifecycleParticipant<ILifecycleObservable> participant)
            {
                this.participant = participant;
            }

            public void Participate(TLifecycle lifecycle)
            {
                this.participant?.Participate(lifecycle);
            }
        }
    }
}
