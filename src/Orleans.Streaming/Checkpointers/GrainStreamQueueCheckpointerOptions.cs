using System;
using System.Collections.Generic;
using Orleans.Providers;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 grain- 基于流队列检查。
    /// </summary>
    public sealed class GrainStreamQueueCheckpointerOptions
    {
        /// <summary>
        /// 获取或设定检查站之间的最小间隔。
        /// </summary>
        public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置用于长期检查站的 grain 存储供应商的名称。
        /// </summary>
        /// <remarks>
        /// 供应商必须登记为名存贮供应商 grain 。
        /// 选项由流源提供商范围界定,因此每个流源提供商可以选择不同的 grain 存储提供者。
        /// </remarks>
        public string StorageProviderName { get; set; } = ProviderConstants.DEFAULT_PUBSUB_PROVIDER_NAME;

        /// <summary>
        /// 获取或设置用来防止检查站向后移动的比较器。
        /// </summary>
        /// <remarks>
        /// 候选人检查站与最新的检查站比较,只有在比较大的检查站接受。
        /// 当该财产 <see langword="null"/>时,假定检查站会越来越频繁地抵达。
        /// 使用 <see cref="StreamCheckpointComparers.Numeric"/> 来计算任意大小的数值。
        /// </remarks>
        public IComparer<string>? CheckpointComparer { get; set; }
    }
}
