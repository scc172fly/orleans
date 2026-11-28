using System;
using System.Collections.Generic;

namespace Orleans.Configuration
{
    /// <summary>
    /// 用于空闲 Grain 回收的 Silo 选项。
    /// </summary>
    public class GrainCollectionOptions
    {
        /// <summary>
        /// 调节非活动 Grain 的周期性回收。
        /// </summary>
        public TimeSpan CollectionQuantum { get; set; } = DEFAULT_COLLECTION_QUANTUM;

        /// <summary>
        /// <see cref="CollectionQuantum"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_COLLECTION_QUANTUM = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置 Grain 达到可回收和停用条件所需的默认非活动时长。
        /// </summary>
        public TimeSpan CollectionAge { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// 按 Grain 类型指定达到可回收和停用条件所需的非活动时长。
        /// </summary>
        public Dictionary<string, TimeSpan> ClassSpecificCollectionAge { get; set; } = new Dictionary<string, TimeSpan>();

        /// <summary>
        /// 获取或设置运行时取消传递给激活代码的 <see cref="System.Threading.CancellationToken"/> 之前的时间间隔，
        /// 激活代码包括 <c>Grain.OnActivateAsync</c> 及 Grain 生命周期参与者。
        /// </summary>
        /// <remarks>
        /// 运行时等待激活代码完成，然后在令牌已被取消时使激活失败。
        /// 激活代码观察此令牌，并在其取消后尽快完成。
        /// </remarks>
        public TimeSpan ActivationTimeout { get; set; } = DEFAULT_ACTIVATION_TIMEOUT;

        /// <summary>
        /// <see cref="ActivationTimeout"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_ACTIVATION_TIMEOUT = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置运行时取消传递给停用代码的 <see cref="System.Threading.CancellationToken"/> 之前的时间间隔，
        /// 停用代码包括 <c>Grain.OnDeactivateAsync</c> 及 Grain 生命周期参与者。
        /// </summary>
        /// <remarks>
        /// 运行时等待停用代码执行完毕，再注销激活实例并释放其服务。
        /// 停用代码观察此令牌，并在其取消后尽快完成。
        /// 激活实例回收器最多等待每个回收批次达到此时长，然后继续后续回收轮次，而停用操作继续进行直至完成。
        /// </remarks>
        public TimeSpan DeactivationTimeout { get; set; } = DEFAULT_DEACTIVATION_TIMEOUT;

        /// <summary>
        /// <see cref="DeactivationTimeout"/> 的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_DEACTIVATION_TIMEOUT = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 指示内存压力是否应触发 Grain 激活实例削减。
        /// </summary>
        /// <remarks>
        /// 设为 true 时，内存使用量超过指定限制后 Silo 会削减 Grain 激活实例。
        /// 有关配置，请参阅 <see cref="MemoryUsageLimitPercentage"/>、<see cref="MemoryUsageTargetPercentage"/> 和 <see cref="MemoryUsagePollingPeriod"/>。
        /// </remarks>
        public bool EnableActivationSheddingOnMemoryPressure { get; set; }

        /// <summary>
        /// 轮询内存使用量的时间间隔。
        /// </summary>
        public TimeSpan MemoryUsagePollingPeriod { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 触发 Grain 回收的内存使用百分比（0–100）。
        /// 必须大于 0 且小于或等于 100。
        /// </summary>
        public double MemoryUsageLimitPercentage { get; set; } = 80;

        /// <summary>
        /// Grain 回收后希望达到的目标内存使用百分比（0–100）。
        /// 必须大于 0、小于或等于 100，并且小于 <see cref="MemoryUsageLimitPercentage"/>。
        /// </summary>
        public double MemoryUsageTargetPercentage { get; set; } = 75;
    }
}
