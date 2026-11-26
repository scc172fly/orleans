using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 LeaseBasedQueueBalancer。用户需要配置此选项,才能使用 LeaseBasedQueueBalancer 中在
    ///   流提供方的串流提供方选项可以配置为与提供者同名的命名选项。
    /// </summary>
    public class LeaseBasedQueueBalancerOptions
    {
        /// <summary>
        /// 租约的长短或定数
        /// </summary>
        /// <value>租约的长度。</value>
        public TimeSpan LeaseLength { get; set; } = DefaultLeaseLength;

        /// <summary>
        /// 默认租赁期长度 。
        /// </summary>
        public static readonly TimeSpan DefaultLeaseLength = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 延长或确定续租期。
        /// </summary>
        /// <value>租赁期的续延。</value>
        public TimeSpan LeaseRenewPeriod { get; set; } = DefaultLeaseRenewPeriod;

        /// <summary>
        /// 违约租赁续期
        /// </summary>
        /// <remarks>
        /// <see cref="DefaultLeaseRenewPeriod"/> 设于<see cref="DefaultLeaseLength"/>/2 - 1,以便在我们失去租约之前至少允许两次重新通话。        
        /// </remarks>
        public static readonly TimeSpan DefaultLeaseRenewPeriod = TimeSpan.FromSeconds(29);

        /// <summary>
        /// 获得或确定平衡者试图获得租赁的频率。
        /// </summary>
        public TimeSpan LeaseAcquisitionPeriod { get; set; } = DefaultMinLeaseAcquisitionPeriod;

        /// <summary>
        /// 获得或确定平衡者试图获得租赁的频率。
        /// </summary>
        [Obsolete($"Use {nameof(LeaseAcquisitionPeriod)} instead.", error: true)]
        public TimeSpan LeaseAquisitionPeriod { get => LeaseAcquisitionPeriod; set => LeaseAcquisitionPeriod = value; }

        /// <summary>
        /// 默认最低租赁期。
        /// </summary>
        public static readonly TimeSpan DefaultMinLeaseAcquisitionPeriod = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 默认最低租赁期。
        /// </summary>
        [Obsolete($"Use {nameof(DefaultMinLeaseAcquisitionPeriod)} instead.", error: true)]
        public static readonly TimeSpan DefaultMinLeaseAquisitionPeriod = DefaultMinLeaseAcquisitionPeriod;

        /// <summary>
        /// 获得或确定租赁类别,允许对租赁进行更多的罚款 grain 分割。
        /// </summary>
        public string LeaseCategory { get; set; } = DefaultLeaseCategory;

        /// <summary>
        /// 默认租赁类别
        /// </summary>
        public const string DefaultLeaseCategory = "QueueBalancer";
    }
}
