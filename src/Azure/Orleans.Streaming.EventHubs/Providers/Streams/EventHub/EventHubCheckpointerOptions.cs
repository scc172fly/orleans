using System;
using System.Collections.Generic;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 为 Event Hubs 流提供程序配置基于 Azure Table Storage 的检查点持久化。
    /// </summary>
    public class AzureTableStreamCheckpointerOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// Azure Table 表名。
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;
        /// <summary>
        /// 默认 Azure Table Storage 表名。
        /// </summary>
        public const string DEFAULT_TABLE_NAME = "Checkpoint";

        /// <summary>
        /// 写入检查点的时间间隔，用于避免频繁访问存储。
        /// </summary>
        public TimeSpan PersistInterval { get; set; } = DEFAULT_CHECKPOINT_PERSIST_INTERVAL;
        /// <summary>
        /// 检查点写入之间的默认最短间隔。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CHECKPOINT_PERSIST_INTERVAL = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置应用于检查点分区键的前缀。
        /// </summary>
        public string PartitionKeyPrefix { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置用于防止检查点倒退的比较器。
        /// </summary>
        /// <remarks>
        /// 此属性为 <see langword="null"/> 时，假定检查点按递增顺序到达。
        /// 对于任意大小的数值型检查点值，使用 <see cref="StreamCheckpointComparers.Numeric"/>。
        /// </remarks>
        public IComparer<string>? CheckpointComparer { get; set; }
    }

    /// <summary>
    /// 验证 <see cref="AzureTableStreamCheckpointerOptions"/>。
    /// </summary>
    public class AzureTableStreamCheckpointerOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableStreamCheckpointerOptions>
    {
        /// <summary>
        /// 初始化 <see cref="AzureTableStreamCheckpointerOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">选项名称。</param>
        public AzureTableStreamCheckpointerOptionsValidator(AzureTableStreamCheckpointerOptions options, string name) : base(options, name)
        {
        }
    }
}
