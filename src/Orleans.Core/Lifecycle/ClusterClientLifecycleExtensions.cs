
namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="ILifecycleParticipant{TLifecycleObservable}"/>的扩展期。
    /// </summary>
    public static class LifecycleParticipantExtensions
    {
        /// <summary>
        /// 符合要求与任何 <see cref="ILifecycleObservable"/> 一起参与特定生命周期的成份。
        /// </summary>
        /// <typeparam name="TLifecycle">目标生命周期观察类型。</typeparam>
        /// <param name="participant">生命周期参与者。</param>
        /// <returns>环绕 <paramref name="participant"/> 的适配器,该适配器可以执行 <see cref="ILifecycleParticipant{TLifecycle}"/>。</returns>
        public static ILifecycleParticipant<TLifecycle> ParticipateIn<TLifecycle>(this ILifecycleParticipant<ILifecycleObservable> participant)
            where TLifecycle : ILifecycleObservable
        {
            return new Bridge<TLifecycle>(participant);
        }

        /// <summary>
        /// 使一个生命周期参与者适应另一个观察类型生命周期参与者。
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
