
namespace Orleans.Runtime
{
    /// <summary>
    /// 可观测到的 silo 生命周期。
    /// </summary>
    /// <remarks>
    /// 此类型通常在 <see cref="ILifecycleParticipant{ISiloLifecycle}"/> 中用作通用参数,
    /// a 参与 silo生命周期阶段的手段。
    /// </remarks>
    /// <seealso cref="Orleans.ILifecycleObservable" />
    public interface ISiloLifecycle : ILifecycleObservable
    {
        /// <summary>
        /// 完成最大生命周期阶段的启动。
        /// </summary>
        int HighestCompletedStage { get; }

        /// <summary>
        /// 完成停止的最低生命周期阶段。
        /// </summary>
        int LowestStoppedStage { get; }
    }
}
