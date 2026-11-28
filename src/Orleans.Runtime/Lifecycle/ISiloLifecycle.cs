
namespace Orleans.Runtime
{
    /// <summary>
    /// 可观察的 Silo 生命周期。
    /// </summary>
    /// <remarks>
    /// 此类型通常用作 <see cref="ILifecycleParticipant{ISiloLifecycle}"/> 的泛型参数，
    /// 以参与 Silo 的各生命周期阶段。
    /// </remarks>
    /// <seealso cref="Orleans.ILifecycleObservable" />
    public interface ISiloLifecycle : ILifecycleObservable
    {
        /// <summary>
        /// 已完成启动的最高生命周期阶段。
        /// </summary>
        int HighestCompletedStage { get; }

        /// <summary>
        /// 已完成停止的最低生命周期阶段。
        /// </summary>
        int LowestStoppedStage { get; }
    }
}
