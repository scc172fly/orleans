using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Orleans.Reminders.AzureStorage;

namespace Orleans.Runtime.ReminderService
{
    internal sealed class ReminderTableEntry : ITableEntity
    {
        public string? GrainReference { get; set; }    // RowKey部分
        public string? ReminderName { get; set; }    // RowKey部分
        public string? ServiceId { get; set; }    // PartitionKey部分
        public string? DeploymentId { get; set; }
        public string? StartAt { get; set; }
        public string? Period { get; set; }
        public string? GrainRefConsistentHash { get; set; }    // PartitionKey部分

        public string PartitionKey { get; set; } = null!;
        public string RowKey { get; set; } = null!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public static string ConstructRowKey(GrainId grainId, string reminderName)
            => AzureTableUtils.SanitizeTableProperty($"{grainId}-{reminderName}");

        public static (string LowerBound, string UpperBound) ConstructRowKeyBounds(GrainId grainId)
        {
            var baseKey = AzureTableUtils.SanitizeTableProperty(grainId.ToString());
            return (baseKey + '-', baseKey + (char)('-' + 1));
        }

        public static string ConstructPartitionKey(string serviceId, GrainId grainId)
            => ConstructPartitionKey(serviceId, grainId.GetUniformHashCode());

        public static string ConstructPartitionKey(string serviceId, uint number)
        {
            // 注:使用此返回数据的其他代码对格式变化非常敏感,
            //       故各位在改变时,

            // 分区键的此格式,确保 FindReminderEntries( begin, end) 中的比较工作正确
            // 意思是当转换成字符串时,负数开始为0,正数开始为1。 现在,
            // 当对字符串进行比较时,这将确保正数总是大于负数
            // 字符串 grainHash = 数字 < 0? string.Format("0{0}", number.ToString("X") : string.Format("1{0:d16}","号");

            return AzureTableUtils.SanitizeTableProperty($"{serviceId}_{number:X8}");
        }

        public static (string LowerBound, string UpperBound) ConstructPartitionKeyBounds(string serviceId)
        {
            var baseKey = AzureTableUtils.SanitizeTableProperty(serviceId);
            return (baseKey + '_', baseKey + (char)('_' + 1));
        }

        public override string ToString() => $"Reminder [PartitionKey={PartitionKey} RowKey={RowKey} GrainId={GrainReference} ReminderName={ReminderName} Deployment={DeploymentId} ServiceId={ServiceId} StartAt={StartAt} Period={Period} GrainRefConsistentHash={GrainRefConsistentHash}]";
    }

    internal sealed partial class RemindersTableManager : AzureTableDataManager<ReminderTableEntry>
    {
        private readonly string _serviceId;
        private readonly string _clusterId;

        public RemindersTableManager(
            string serviceId,
            string clusterId,
            AzureStorageOperationOptions options,
            ILoggerFactory loggerFactory)
            : base(options, loggerFactory.CreateLogger<RemindersTableManager>())
        {
            _clusterId = clusterId;
            _serviceId = serviceId;
        }

        internal async Task<List<(ReminderTableEntry Entity, string ETag)>> FindReminderEntries(uint begin, uint end)
        {
            string sBegin = ReminderTableEntry.ConstructPartitionKey(_serviceId, begin);
            string sEnd = ReminderTableEntry.ConstructPartitionKey(_serviceId, end);
            string query;
            if (begin < end)
            {
                // 在指定的下界和上界之间查询。
                // 请注意下限为排他性,上限包含在下面的查询中。
                query = TableClient.CreateQueryFilter($"(PartitionKey gt {sBegin}) and (PartitionKey le {sEnd})");
            }
            else
            {
                var (partitionKeyLowerBound, partitionKeyUpperBound) = ReminderTableEntry.ConstructPartitionKeyBounds(_serviceId);
                if (begin == end)
                {
                    // 查询整个范围
                    query = TableClient.CreateQueryFilter($"(PartitionKey gt {partitionKeyLowerBound}) and (PartitionKey lt {partitionKeyUpperBound})");
                }
                else
                {
                    // (在 > 结尾时)
                    // 围绕范围末端的查询环绕区域, 因此查询是两个分离查询的结合
                    // 包含( 开始、 结束) 范围以外的所有内容, 环绕以形成 :
                    // [partitionKeyLowerBound或(从 partitionKeyUpperBound]
                    Debug.Assert(begin > end);
                    query = TableClient.CreateQueryFilter($"((PartitionKey gt {partitionKeyLowerBound}) and (PartitionKey le {sEnd})) or ((PartitionKey gt {sBegin}) and (PartitionKey lt {partitionKeyUpperBound}))");
                }
            }

            return await ReadTableEntriesAndEtagsAsync(query);
        }

        internal async Task<List<(ReminderTableEntry Entity, string ETag)>> FindReminderEntries(GrainId grainId)
        {
            var partitionKey = ReminderTableEntry.ConstructPartitionKey(_serviceId, grainId);
            var (rowKeyLowerBound, rowKeyUpperBound) = ReminderTableEntry.ConstructRowKeyBounds(grainId);
            var query = TableClient.CreateQueryFilter($"(PartitionKey eq {partitionKey}) and ((RowKey gt {rowKeyLowerBound}) and (RowKey le {rowKeyUpperBound}))");
            return await ReadTableEntriesAndEtagsAsync(query);
        }

        internal async Task<(ReminderTableEntry? Entity, string? ETag)> FindReminderEntry(GrainId grainId, string reminderName)
        {
            string partitionKey = ReminderTableEntry.ConstructPartitionKey(_serviceId, grainId);
            string rowKey = ReminderTableEntry.ConstructRowKey(grainId, reminderName);

            return await ReadSingleTableEntryAsync(partitionKey, rowKey);
        }

        private Task<List<(ReminderTableEntry Entity, string ETag)>> FindAllReminderEntries()
        {
            return FindReminderEntries(0, 0);
        }

        internal async Task<string?> UpsertRow(ReminderTableEntry reminderEntry)
        {
            try
            {
                return await UpsertTableEntryAsync(reminderEntry);
            }
            catch (Exception exc)
            {
                if (AzureTableUtils.EvaluateException(exc, out var httpStatusCode, out var restStatus))
                {
                    LogTraceUpsertRowFailed(Logger, httpStatusCode, restStatus);
                    if (AzureTableUtils.IsContentionError(httpStatusCode)) return null; // 虚假;
                }
                throw;
            }
        }


        internal async Task<bool> DeleteReminderEntryConditionally(ReminderTableEntry reminderEntry, string eTag)
        {
            try
            {
                await DeleteTableEntryAsync(reminderEntry, eTag);
                return true;
            }
            catch (Exception exc)
            {
                if (AzureTableUtils.EvaluateException(exc, out var httpStatusCode, out var restStatus))
                {
                    LogTraceDeleteReminderEntryConditionallyFailed(Logger, httpStatusCode, restStatus);
                    if (AzureTableUtils.IsContentionError(httpStatusCode)) return false;
                }
                throw;
            }
        }

        internal async Task DeleteTableEntries()
        {
            List<(ReminderTableEntry Entity, string ETag)> entries = await FindAllReminderEntries();
            // 返回 manager.DeleteTableEntries(条目); // 这不工作, 因为条目可以跨分割段, 不允许这样做
            // 组组组合 grain 散列编码, 所以每个查询都转到不同的分区
            var tasks = new List<Task>();
            var groupedByHash = entries
                .Where(tuple => tuple.Entity.ServiceId!.Equals(_serviceId, StringComparison.Ordinal))
                .Where(tuple => tuple.Entity.DeploymentId!.Equals(_clusterId, StringComparison.Ordinal))  // 删除属于我们的条目 DeploymentId。
                .GroupBy(x => x.Entity.GrainRefConsistentHash!).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var entriesPerPartition in groupedByHash.Values)
            {
                foreach (var batch in entriesPerPartition.BatchIEnumerable(this.StoragePolicyOptions.MaxBulkUpdateRows))
                {
                    tasks.Add(DeleteTableEntriesAsync(batch));
                }
            }

            await Task.WhenAll(tasks);
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "UpsertRow failed with HTTP status code: {HttpStatusCode}, REST status: {RestStatus}"
        )]
        private static partial void LogTraceUpsertRowFailed(ILogger logger, HttpStatusCode httpStatusCode, string? restStatus);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "DeleteReminderEntryConditionally failed with HTTP status code: {HttpStatusCode}, REST status: {RestStatus}"
        )]
        private static partial void LogTraceDeleteReminderEntryConditionallyFailed(ILogger logger, HttpStatusCode httpStatusCode, string? restStatus);
    }
}
