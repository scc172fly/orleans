using System;
using System.Collections.Generic;
using Orleans.Providers;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置基于 Grain 的流队列检查点机制。
    /// </summary>
    public sealed class GrainStreamQueueCheckpointerOptions
    {
        /// <summary>
        /// 获取或设置检查点写入之间的最短时间间隔。
        /// </summary>
        public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置用于持久化检查点的 Grain 存储提供程序名称。
        /// </summary>
        /// <remarks>
        /// 必须将该提供程序注册为命名的 Grain 存储提供程序。
        /// 选项以流提供程序为作用域，因此每个流提供程序可以选择不同的 Grain 存储提供程序。
        /// </remarks>
        public string StorageProviderName { get; set; } = ProviderConstants.DEFAULT_PUBSUB_PROVIDER_NAME;

        /// <summary>
        /// 获取或设置用于防止检查点向后移动的比较器。
        /// </summary>
        /// <remarks>
        /// 将候选检查点与最新检查点进行比较，仅当候选值较大时才接受。
        /// 当此属性为 <see langword="null"/> 时，假定检查点按递增顺序到达。
        /// 对于任意大小的数值检查点，使用 <see cref="StreamCheckpointComparers.Numeric"/>。
        /// </remarks>
        public IComparer<string>? CheckpointComparer { get; set; }
    }
}
