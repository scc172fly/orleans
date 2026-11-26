using System.Diagnostics.CodeAnalysis;

//
// #ifs 数量可以减少(或删除), 一旦我们按照特性/区域分开测试项目, 否则我们会最终出现大错特错。
//

#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.AzureStorage.Utilities
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.AzureStorage.Utilities
#elif ORLEANS_REMINDERS_PROVIDER
namespace Orleans.Reminders
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.AzureStorage.Utilities
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.AzureStorage.Utilities
#elif ORLEANS_EVENTHUBS
namespace Orleans.Streaming.EventHubs.Utilities
#elif TESTER_AZUREUTILS
namespace Orleans.Tests.AzureUtils.Utilities
#elif ORLEANS_TRANSACTIONS
namespace Orleans.Transactions.AzureStorage.Utilities
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.AzureStorage.Utilities
#else
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal enum ErrorCode
    {
        Runtime = 100000,
        AzureTableBase = Runtime + 800,

        AzureTable_01 = AzureTableBase + 1,
        AzureTable_02 = AzureTableBase + 2,
        AzureTable_03 = AzureTableBase + 3,
        AzureTable_04 = AzureTableBase + 4,
        AzureTable_06 = AzureTableBase + 6,
        AzureTable_07 = AzureTableBase + 7,
        AzureTable_08 = AzureTableBase + 8,
        AzureTable_09 = AzureTableBase + 9,
        AzureTable_10 = AzureTableBase + 10,
        AzureTable_11 = AzureTableBase + 11,
        AzureTable_12 = AzureTableBase + 12,
        AzureTable_13 = AzureTableBase + 13,
        AzureTable_14 = AzureTableBase + 14,
        AzureTable_15 = AzureTableBase + 15,
        AzureTable_17 = AzureTableBase + 17,
        AzureTable_18 = AzureTableBase + 18,
        AzureTable_19 = AzureTableBase + 19,
        AzureTable_37 = AzureTableBase + 37,
        // Azure 与储存供应商有关的
        AzureTable_DataNotFound = AzureTableBase + 50,
        AzureTable_TableNotCreated = AzureTableBase + 51,
    }
}
