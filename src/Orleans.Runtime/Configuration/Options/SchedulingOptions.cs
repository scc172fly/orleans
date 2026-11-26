using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置调度器行为选项
    /// </summary>
    public class SchedulingOptions
    {
        /// <summary>
        /// 获取或设置工作项排队延迟阈值, 在此值上写出警告日志消息 。
        /// 也就是说,如果工作项目的排队和执行工作项目之间的延迟大于 DelayWarningThreshold,则会写出警告日志。
        /// </summary>
        public TimeSpan DelayWarningThreshold { get; set; } = DEFAULT_DELAY_WARNING_THRESHOLD;

        /// <summary>
        /// <see cref="DelayWarningThreshold"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_DELAY_WARNING_THRESHOLD = TimeSpan.FromMilliseconds(10000); // 10秒

        /// <summary>
        /// 获取或设定启动宏观倾斜(若干微转)的时间期限的软性时间限制。 
        /// 如果一个启动运行的微转速比这长 我们就会放弃线条
        /// 如果设定此数为零或负数,则整个工作队列排干( ) (% 1)MaxWorkItemsPerTurn 允许)
        /// </summary>
        public TimeSpan ActivationSchedulingQuantum { get; set; } = DEFAULT_ACTIVATION_SCHEDULING_QUANTUM;

        /// <summary>
        /// <see cref="ActivationSchedulingQuantum"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_ACTIVATION_SCHEDULING_QUANTUM = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// 当微转在CPU中执行更长的时间时, 获取或设定软时间限制以生成跟踪警告 。 
        /// </summary>
        public TimeSpan TurnWarningLengthThreshold { get; set; } = DEFAULT_TURN_WARNING_THRESHOLD;

        /// <summary>
        /// <see cref="TurnWarningLengthThreshold"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_TURN_WARNING_THRESHOLD = TimeSpan.FromMilliseconds(1_000);

        /// <summary>
        /// 获取或设定每个工作组在生成警告前可以排队的项目数量限制。
        /// </summary>
        public int MaxPendingWorkItemsSoftLimit { get; set; } = DEFAULT_MAX_PENDING_ITEMS_SOFT_LIMIT;

        /// <summary>
        /// <see cref="MaxPendingWorkItemsSoftLimit"/>的默认值。
        /// </summary>
        public const int DEFAULT_MAX_PENDING_ITEMS_SOFT_LIMIT = 0;

        /// <summary>
        /// 获取或设定用于记录计划停止激活的任务错误的时间间隔 。
        /// </summary>
        public TimeSpan StoppedActivationWarningInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
