using System.Diagnostics.CodeAnalysis;

//
// 一旦按功能或领域拆分测试项目，就可以减少甚至移除 #if；否则会产生类型歧义及生成错误。
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
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
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
        // Azure 存储提供程序相关。
        AzureTable_DataNotFound = AzureTableBase + 50,
        AzureTable_TableNotCreated = AzureTableBase + 51,
    }
}
