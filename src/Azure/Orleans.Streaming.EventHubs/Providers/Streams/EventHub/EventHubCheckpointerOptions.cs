using System;
using System.Collections.Generic;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 Azure 表存储一个事件枢纽流提供者的检查站持久性。
    /// </summary>
    public class AzureTableStreamCheckpointerOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// Azure 表格名称。
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;
        /// <summary>
        /// 默认 Azure 表格存储表名称。
        /// </summary>
        public const string DEFAULT_TABLE_NAME = "Checkpoint";

        /// <summary>
        /// 隔间来写检查站,防止垃圾存储
        /// </summary>
        public TimeSpan PersistInterval { get; set; } = DEFAULT_CHECKPOINT_PERSIST_INTERVAL;
        /// <summary>
        /// 检查点之间默认的最低间隔 。
        /// </summary>
        public static readonly TimeSpan DEFAULT_CHECKPOINT_PERSIST_INTERVAL = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置用于检查站分区密钥的前缀。
        /// </summary>
        public string PartitionKeyPrefix { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置用来防止检查站向后移动的比较器。
        /// </summary>
        /// <remarks>
        /// 当该财产 <see langword="null"/>时,假定检查站会越来越频繁地抵达。
        /// 使用 <see cref="StreamCheckpointComparers.Numeric"/> 来计算任意大小的数值。
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
        /// <param name="options">验证的选项。</param>
        /// <param name="name">选项名称。</param>
        public AzureTableStreamCheckpointerOptionsValidator(AzureTableStreamCheckpointerOptions options, string name) : base(options, name)
        {
        }
    }
}
