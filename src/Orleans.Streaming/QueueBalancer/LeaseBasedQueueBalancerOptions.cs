using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// LeaseBasedQueueBalancer 的配置。若要在流提供程序中使用 LeaseBasedQueueBalancer，
    /// 用户需要配置此选项。可使用与提供程序同名的命名选项分别配置各流提供程序。
    /// </summary>
    public class LeaseBasedQueueBalancerOptions
    {
        /// <summary>
        /// 获取或设置租约有效期。
        /// </summary>
        /// <value>租约有效期。</value>
        public TimeSpan LeaseLength { get; set; } = DefaultLeaseLength;

        /// <summary>
        /// 默认租约有效期。
        /// </summary>
        public static readonly TimeSpan DefaultLeaseLength = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 获取或设置租约续订周期。
        /// </summary>
        /// <value>租约续订周期。</value>
        public TimeSpan LeaseRenewPeriod { get; set; } = DefaultLeaseRenewPeriod;

        /// <summary>
        /// 默认租约续订周期。
        /// </summary>
        /// <remarks>
        /// <see cref="DefaultLeaseRenewPeriod"/> 设置为 (<see cref="DefaultLeaseLength"/>/2 - 1)，以便在丢失租约之前至少有时间进行两次续订调用。
        /// </remarks>
        public static readonly TimeSpan DefaultLeaseRenewPeriod = TimeSpan.FromSeconds(29);

        /// <summary>
        /// 获取或设置均衡器尝试获取租约的频率。
        /// </summary>
        public TimeSpan LeaseAcquisitionPeriod { get; set; } = DefaultMinLeaseAcquisitionPeriod;

        /// <summary>
        /// 获取或设置均衡器尝试获取租约的频率。
        /// </summary>
        [Obsolete($"Use {nameof(LeaseAcquisitionPeriod)} instead.", error: true)]
        public TimeSpan LeaseAquisitionPeriod { get => LeaseAcquisitionPeriod; set => LeaseAcquisitionPeriod = value; }

        /// <summary>
        /// 默认最短租约获取周期。
        /// </summary>
        public static readonly TimeSpan DefaultMinLeaseAcquisitionPeriod = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 默认最短租约获取周期。
        /// </summary>
        [Obsolete($"Use {nameof(DefaultMinLeaseAcquisitionPeriod)} instead.", error: true)]
        public static readonly TimeSpan DefaultMinLeaseAquisitionPeriod = DefaultMinLeaseAcquisitionPeriod;

        /// <summary>
        /// 获取或设置租约类别，以便对租约进行更细粒度的分区。
        /// </summary>
        public string LeaseCategory { get; set; } = DefaultLeaseCategory;

        /// <summary>
        /// 默认租约类别。
        /// </summary>
        public const string DefaultLeaseCategory = "QueueBalancer";
    }
}
