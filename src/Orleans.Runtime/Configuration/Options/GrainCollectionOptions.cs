using System;
using System.Collections.Generic;

namespace Orleans.Configuration
{
    /// <summary>
    /// Silo 闲置选项 grain 收藏。
    /// </summary>
    public class GrainCollectionOptions
    {
        /// <summary>
        /// 规范非活动 grains的定期收集。
        /// </summary>
        public TimeSpan CollectionQuantum { get; set; } = DEFAULT_COLLECTION_QUANTUM;

        /// <summary>
        /// <see cref="CollectionQuantum"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_COLLECTION_QUANTUM = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设定一个 grain 可用于收集和停用所需的默认不活动期。
        /// </summary>
        public TimeSpan CollectionAge { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// 工作期间为 grain 可用于收集并解除其 grain 类型。
        /// </summary>
        public Dictionary<string, TimeSpan> ClassSpecificCollectionAge { get; set; } = new Dictionary<string, TimeSpan>();

        /// <summary>
        /// 获取或设置运行时间取消 <see cref="System.Threading.CancellationToken"/> 传递到激活代码的时期,
        /// 包括 <c>Grain.OnActivateAsync</c> 和 grain 生命周期参与者。
        /// </summary>
        /// <remarks>
        /// 运行时间等待激活代码完成, 如果该符号被取消则失效 。
        /// 激活代码观察标志,一旦取消即迅速完成。
        /// </remarks>
        public TimeSpan ActivationTimeout { get; set; } = DEFAULT_ACTIVATION_TIMEOUT;

        /// <summary>
        /// <see cref="ActivationTimeout"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_ACTIVATION_TIMEOUT = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置运行时间取消 <see cref="System.Threading.CancellationToken"/> 传递到禁用代码的注销时间后的时间段,
        /// 包括 <c>Grain.OnDeactivateAsync</c> 和 grain 生命周期参与者。
        /// </summary>
        /// <remarks>
        /// 运行时间等待解除代码完成,然后才能对激活进行登记并处置激活服务。
        /// 解除引爆代码会观察标记,一旦取消后立即完成。
        /// 激活采集器等待到这一阶段,对每个收集批次进行等待,然后在解除功能完成时继续通过随后的通行证。
        /// </remarks>
        public TimeSpan DeactivationTimeout { get; set; } = DEFAULT_DEACTIVATION_TIMEOUT;

        /// <summary>
        /// <see cref="DeactivationTimeout"/>的默认值。
        /// </summary>
        public static readonly TimeSpan DEFAULT_DEACTIVATION_TIMEOUT = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 表示内存压力是否触发 grain 激活停止。
        /// </summary>
        /// <remarks>
        /// 如果设置为真实, silo 将随着内存使用量超过规定的限制而丢弃 grain 激活。
        /// 配置见 <see cref="MemoryUsageLimitPercentage"/>、 <see cref="MemoryUsageTargetPercentage"/>和 <see cref="MemoryUsagePollingPeriod"/> 。
        /// </remarks>
        public bool EnableActivationSheddingOnMemoryPressure { get; set; }

        /// <summary>
        /// 测量内存使用间隔。
        /// </summary>
        public TimeSpan MemoryUsagePollingPeriod { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 触发 grain 收藏的内存使用百分比( 0- 100) 。
        /// 大于0且小于或等于100。
        /// </summary>
        public double MemoryUsageLimitPercentage { get; set; } = 80;

        /// <summary>
        /// 收集 grain 后达到的目标内存使用率(0-100)
        /// 必须大于0、小于或等于100,小于 <see cref="MemoryUsageLimitPercentage"/>。
        /// </summary>
        public double MemoryUsageTargetPercentage { get; set; } = 75;
    }
}
