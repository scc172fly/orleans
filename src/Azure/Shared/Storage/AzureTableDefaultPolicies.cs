using System;
using System.Diagnostics;
using Microsoft.Azure.Cosmos.Table;

//
// #ifs 数量可以减少(或删除), 一旦我们按照特性/区域分开测试项目, 否则我们会最终出现大错特错。
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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 默认重试/超时设置的 Azure 存储工具类。
    /// </summary>
    /// <remarks>
    /// 这些功能主要用于 Orleans 运行时的内部使用,但由于某些装配包装方面的限制,这类功能需要具有公众可见度。
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
            TableCreationTimeout = TimeSpan.FromMilliseconds(PauseBetweenTableCreationRetries.TotalMilliseconds * MaxTableCreationRetries * 3);    // 3分钟 3分钟

            TableOperationRetryPolicy = new LinearRetry(PauseBetweenTableOperationRetries, MaxTableOperationRetries); // 5 x 100米s
            TableOperationTimeout = TimeSpan.FromMilliseconds(PauseBetweenTableOperationRetries.TotalMilliseconds * MaxTableOperationRetries *6);    // 3秒3秒

            BusyRetriesTimeout = TimeSpan.FromMilliseconds(PauseBetweenBusyRetries.TotalMilliseconds * MaxBusyRetries);  // 1分钟1分
        }
    }
}
