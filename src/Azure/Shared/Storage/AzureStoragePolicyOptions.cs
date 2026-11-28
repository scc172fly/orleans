using System;
using System.Threading;

#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.AzureStorage
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.AzureStorage
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.AzureStorage
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.AzureStorage
#elif ORLEANS_EVENTHUBS
namespace Orleans.Streaming.EventHubs
#elif TESTER_AZUREUTILS
namespace Orleans.Tests.AzureUtils
#elif ORLEANS_TRANSACTIONS
namespace Orleans.Transactions.AzureStorage
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.AzureStorage
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 配置 Azure Table Storage 操作的批处理、重试及超时行为。
    /// </summary>
    public class AzureStoragePolicyOptions
    {
        private TimeSpan? creationTimeout;
        private TimeSpan? operationTimeout;
        private TimeSpan? maxPauseBetweenOperationRetries;

        /// <summary>
        /// 获取或设置批量更新或删除操作中包含的最大实体数量。
        /// </summary>
        public int MaxBulkUpdateRows { get; set; } = 100;

        /// <summary>
        /// 获取或设置用于计算默认 <see cref="CreationTimeout"/> 的旧版创建重试次数。
        /// </summary>
        public int MaxCreationRetries { get; set; } = 60;

        // 默认值与 Azure Storage SDK 的重试设置一致。
        /// <summary>
        /// 操作的最大重试次数。
        /// </summary>
        public int MaxOperationRetries { get; set; } = 5;

        /// <summary>
        /// 获取或设置用于计算默认 <see cref="CreationTimeout"/> 的旧版创建重试延迟。
        /// </summary>
        public TimeSpan PauseBetweenCreationRetries { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Azure SDK 指数重试策略使用的基础延迟。
        /// </summary>
        public TimeSpan PauseBetweenOperationRetries { get; set; } = TimeSpan.FromSeconds(0.8);

        /// <summary>
        /// Azure SDK 指数重试策略使用的最大延迟。
        /// </summary>
        public TimeSpan MaxPauseBetweenOperationRetries
        {
            get => this.maxPauseBetweenOperationRetries ?? TimeSpan.FromMinutes(1);
            set
            {
                if (value <= TimeSpan.Zero && !value.Equals(Timeout.InfiniteTimeSpan))
                {
                    throw new ArgumentOutOfRangeException(nameof(MaxPauseBetweenOperationRetries), value, "Value must be positive or Timeout.InfiniteTimeSpan.");
                }
                this.maxPauseBetweenOperationRetries = value;
            }
        }

        /// <summary>
        /// 获取或设置表创建超时诊断信息中包含的超时值。
        /// </summary>
        /// <remarks>
        /// 默认值为 <see cref="PauseBetweenCreationRetries"/> 与 <see cref="MaxCreationRetries"/> 乘积的三倍。
        /// </remarks>
        public TimeSpan CreationTimeout
        {
            get => this.creationTimeout ?? TimeSpan.FromMilliseconds(this.PauseBetweenCreationRetries.TotalMilliseconds * this.MaxCreationRetries * 3);
            set => SetIfValidTimeout(ref this.creationTimeout, value, nameof(CreationTimeout));
        }

        /// <summary>
        /// 获取或设置超过此时长后将表操作报告为慢操作的时间阈值。
        /// </summary>
        public TimeSpan OperationTimeout
        {
            get => this.operationTimeout ?? TimeSpan.FromSeconds(100);
            set => SetIfValidTimeout(ref this.operationTimeout, value, nameof(OperationTimeout));
        }

        private static void SetIfValidTimeout(ref TimeSpan? field, TimeSpan value, string propertyName)
        {
            if (value > TimeSpan.Zero || value.Equals(Timeout.InfiniteTimeSpan))
            {
                field = value;
            }
            else
            {
                throw new ArgumentNullException(propertyName);
            }
        }
    }
}
