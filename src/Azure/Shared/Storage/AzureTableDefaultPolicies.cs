using System;
using System.Diagnostics;
using Microsoft.Azure.Cosmos.Table;

//
// 一旦按功能或领域拆分测试项目，就可以减少甚至移除 #if；否则会产生类型歧义及生成错误。
//

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
    /// Azure Storage 默认重试及超时设置的实用类。
    /// </summary>
    /// <remarks>
    /// 这些函数主要供 Orleans 运行时内部使用，但由于某些程序集打包限制，此类必须具有公共可见性。
    /// </remarks>
    public static class AzureTableDefaultPolicies
    {
        public static int MaxTableCreationRetries { get; private set; }
        public static int MaxTableOperationRetries { get; private set; }
        public static int MaxBusyRetries { get; internal set; }

        public static TimeSpan PauseBetweenTableCreationRetries { get; private set; }
        public static TimeSpan PauseBetweenTableOperationRetries { get; private set; }
        public static TimeSpan PauseBetweenBusyRetries { get; private set; }

        public static TimeSpan TableCreationTimeout { get; private set; }
        public static TimeSpan TableOperationTimeout { get; private set; }
        public static TimeSpan BusyRetriesTimeout { get; private set; }

        public static IRetryPolicy TableCreationRetryPolicy { get; private set; }
        public static IRetryPolicy TableOperationRetryPolicy { get; private set; }

        public const int MAX_BULK_UPDATE_ROWS = 100;

        static AzureTableDefaultPolicies()
        {
            MaxTableCreationRetries = 60;
            PauseBetweenTableCreationRetries = (!Debugger.IsAttached)
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(100);

            MaxTableOperationRetries = 5;
            PauseBetweenTableOperationRetries = (!Debugger.IsAttached)
                ? TimeSpan.FromMilliseconds(100)
                : TimeSpan.FromSeconds(10);

            MaxBusyRetries = 120;
            PauseBetweenBusyRetries = (!Debugger.IsAttached)
                ? TimeSpan.FromMilliseconds(500)
                : TimeSpan.FromSeconds(5);

            TableCreationRetryPolicy = new LinearRetry(PauseBetweenTableCreationRetries, MaxTableCreationRetries); // 60 x 1s
            TableCreationTimeout = TimeSpan.FromMilliseconds(PauseBetweenTableCreationRetries.TotalMilliseconds * MaxTableCreationRetries * 3);    // 3 分钟。

            TableOperationRetryPolicy = new LinearRetry(PauseBetweenTableOperationRetries, MaxTableOperationRetries); // 5 × 100 ms。
            TableOperationTimeout = TimeSpan.FromMilliseconds(PauseBetweenTableOperationRetries.TotalMilliseconds * MaxTableOperationRetries *6);    // 3 秒。

            BusyRetriesTimeout = TimeSpan.FromMilliseconds(PauseBetweenBusyRetries.TotalMilliseconds * MaxBusyRetries);  // 1 分钟。
        }
    }
}
