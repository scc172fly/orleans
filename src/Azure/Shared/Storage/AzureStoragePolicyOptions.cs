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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 配置 Azure 表存储操作的批量、重试和超时行为。
    /// </summary>
    public class AzureStoragePolicyOptions
    {
        private TimeSpan? creationTimeout;
        private TimeSpan? operationTimeout;
        private TimeSpan? maxPauseBetweenOperationRetries;

        /// <summary>
        /// 获取或设定批量更新或删除操作中包含的实体的最大数量。
        /// </summary>
        public int MaxBulkUpdateRows { get; set; } = 100;

        /// <summary>
        /// 获取或设置用于计算默认 <see cref="CreationTimeout"/>的创建遗产重试数。
        /// </summary>
        public int MaxCreationRetries { get; set; } = 60;

        // 默认值匹配 Azure 存储 SDK 重试设置 。
        /// <summary>
        /// 最大操作次数重试尝试。
        /// </summary>
        public int MaxOperationRetries { get; set; } = 5;

        /// <summary>
        /// 获取或设置用于计算默认 <see cref="CreationTimeout"/>的创建遗留数据的延迟时间。
        /// </summary>
        public TimeSpan PauseBetweenCreationRetries { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 基数的延迟 Azure SDK指数重试政策
        /// </summary>
        public TimeSpan PauseBetweenOperationRetries { get; set; } = TimeSpan.FromSeconds(0.8);

        /// <summary>
        /// 使用的最大延迟时间 Azure SDK指数重试政策
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
        /// 获取或设定包含在创建表格超时诊断中的超时值。
        /// </summary>
        /// <remarks>
        /// 默认值是 <see cref="PauseBetweenCreationRetries"/> 和 <see cref="MaxCreationRetries"/>产物的三倍。
        /// </remarks>
        public TimeSpan CreationTimeout
        {
            get => this.creationTimeout ?? TimeSpan.FromMilliseconds(this.PauseBetweenCreationRetries.TotalMilliseconds * this.MaxCreationRetries * 3);
            set => SetIfValidTimeout(ref this.creationTimeout, value, nameof(CreationTimeout));
        }

        /// <summary>
        /// 获取或设置表格操作报告缓慢的时间段。
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
