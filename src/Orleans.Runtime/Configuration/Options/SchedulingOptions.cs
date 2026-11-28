using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 用于配置调度器行为的选项。
    /// </summary>
    public class SchedulingOptions
    {
        /// <summary>
        /// 获取或设置工作项排队延迟阈值，达到该阈值时记录警告日志。
        /// 即工作项入队至开始执行之间的延迟大于 DelayWarningThreshold 时，记录警告日志。
        /// </summary>
        public TimeSpan DelayWarningThreshold { get; set; } = DEFAULT_DELAY_WARNING_THRESHOLD;

        /// <summary>
        /// <see cref="DelayWarningThreshold"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_DELAY_WARNING_THRESHOLD = TimeSpan.FromMilliseconds(10000); // 10 秒。

        /// <summary>
        /// 获取或设置激活实例宏观执行轮次（若干微观执行轮次）时长的软限制。
        /// 若激活实例执行其微观轮次的时间超过此值，则让出线程。
        /// 若设为零或负数，则排空整个工作队列（以 MaxWorkItemsPerTurn 允许的范围为限）。
        /// </summary>
        public TimeSpan ActivationSchedulingQuantum { get; set; } = DEFAULT_ACTIVATION_SCHEDULING_QUANTUM;

        /// <summary>
        /// <see cref="ActivationSchedulingQuantum"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_ACTIVATION_SCHEDULING_QUANTUM = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// 获取或设置微观执行轮次在 CPU 上执行超过此时长时生成跟踪警告的软时间限制。
        /// </summary>
        public TimeSpan TurnWarningLengthThreshold { get; set; } = DEFAULT_TURN_WARNING_THRESHOLD;

        /// <summary>
        /// <see cref="TurnWarningLengthThreshold"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_TURN_WARNING_THRESHOLD = TimeSpan.FromMilliseconds(1_000);

        /// <summary>
        /// 获取或设置每个工作组在生成警告之前可以排队的工作项数量上限。
        /// </summary>
        public int MaxPendingWorkItemsSoftLimit { get; set; } = DEFAULT_MAX_PENDING_ITEMS_SOFT_LIMIT;

        /// <summary>
        /// <see cref="MaxPendingWorkItemsSoftLimit"/> 的默认值。
        /// </summary>
        public const int DEFAULT_MAX_PENDING_ITEMS_SOFT_LIMIT = 0;

        /// <summary>
        /// 获取或设置在向已停止的激活实例调度任务时记录错误日志之前的时长。
        /// </summary>
        public TimeSpan StoppedActivationWarningInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
