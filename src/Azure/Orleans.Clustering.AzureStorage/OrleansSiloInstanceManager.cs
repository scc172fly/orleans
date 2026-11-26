using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Logging;
using Orleans.Clustering.AzureStorage;
using Orleans.Clustering.AzureStorage.Utilities;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.AzureUtils
{
    internal partial class OrleansSiloInstanceManager
    {
        internal const int MaxMembershipSnapshotAttempts = 5;

        public string TableName { get; }

        private const string INSTANCE_STATUS_CREATED = nameof(SiloStatus.Created);  //“改良”;
        private const string INSTANCE_STATUS_ACTIVE = nameof(SiloStatus.Active);    //“主动”;
        private const string INSTANCE_STATUS_DEAD = nameof(SiloStatus.Dead);        //"死";

        private readonly AzureTableDataManager<SiloInstanceTableEntry> storage;
        private readonly IMembershipTableReadStorage membershipTableReadStorage;
        private readonly ILogger logger;
        private readonly AzureStoragePolicyOptions storagePolicyOptions;

        public string DeploymentId { get; private set; }

        private OrleansSiloInstanceManager(
            string clusterId,
            ILoggerFactory loggerFactory,
            AzureStorageOperationOptions options)
            : this(clusterId, loggerFactory, options, null)
        {
        }

        internal OrleansSiloInstanceManager(
            string clusterId,
            ILoggerFactory loggerFactory,
            AzureStorageOperationOptions options,
            IMembershipTableReadStorage? membershipTableReadStorage)
        {
            DeploymentId = clusterId;
            TableName = options.TableName;
            logger = loggerFactory.CreateLogger<OrleansSiloInstanceManager>();
            storage = new AzureTableDataManager<SiloInstanceTableEntry>(
                options,
                loggerFactory.CreateLogger<AzureTableDataManager<SiloInstanceTableEntry>>());
            this.membershipTableReadStorage = membershipTableReadStorage ?? new AzureMembershipTableReadStorage(storage);
            this.storagePolicyOptions = options.StoragePolicyOptions;
        }

        public static async Task<OrleansSiloInstanceManager> GetManager(
            string clusterId,
            ILoggerFactory loggerFactory,
            AzureStorageOperationOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = new OrleansSiloInstanceManager(clusterId, loggerFactory, options);
            try
            {
                await instance.storage.InitTableAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                instance.LogErrorConnectingToAzureTable(ex, instance.storage.TableName);
                throw;
            }
            return instance;
        }

        public SiloInstanceTableEntry CreateTableVersionEntry(int tableVersion)
        {
            return CreateTableVersionEntry(
                SiloInstanceTableEntry.TABLE_VERSION_ROW,
                tableVersion.ToString(CultureInfo.InvariantCulture));
        }

        private SiloInstanceTableEntry CreateTableVersionEntry(string rowKey, string membershipVersion)
        {
            return new()
            {
                DeploymentId = DeploymentId,
                PartitionKey = DeploymentId,
                RowKey = rowKey,
                MembershipVersion = membershipVersion
            };
        }

        private (SiloInstanceTableEntry Min, SiloInstanceTableEntry Max) CreateBoundaryVersionEntries(
            SiloInstanceTableEntry tableVersionEntry)
        {
            var membershipVersion = tableVersionEntry.MembershipVersion
                ?? throw new ArgumentException("The table version entry must have a membership version.", nameof(tableVersionEntry));
            var min = CreateTableVersionEntry(SiloInstanceTableEntry.TABLE_VERSION_ROW_MIN, membershipVersion);
            var max = CreateTableVersionEntry(SiloInstanceTableEntry.TABLE_VERSION_ROW_MAX, membershipVersion);

            return (min, max);
        }

        public Task<string> RegisterSiloInstance(SiloInstanceTableEntry entry)
        {
            entry.Status = INSTANCE_STATUS_CREATED;
            LogRegisterSiloInstance(entry);
            return storage.UpsertTableEntryAsync(entry);
        }

        public Task<string> UnregisterSiloInstance(SiloInstanceTableEntry entry)
        {
            entry.Status = INSTANCE_STATUS_DEAD;
            LogUnregisterSiloInstance(entry);
            return storage.UpsertTableEntryAsync(entry);
        }

        public Task<string> ActivateSiloInstance(SiloInstanceTableEntry entry)
        {
            LogActivateSiloInstance(entry);
            entry.Status = INSTANCE_STATUS_ACTIVE;
            return storage.UpsertTableEntryAsync(entry);
        }

        /// <summary>
        /// 表示网关 URI 格式的 silo 实例条目。
        /// </summary>
        /// <param name="gateway">输入 silo 实例</param>
        /// <returns></returns>
        private static Uri ConvertToGatewayUri(SiloInstanceTableEntry gateway)
        {
            int proxyPort = 0;
            if (!string.IsNullOrEmpty(gateway.ProxyPort))
                int.TryParse(gateway.ProxyPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out proxyPort);

            int gen = 0;
            if (!string.IsNullOrEmpty(gateway.Generation))
                int.TryParse(gateway.Generation, NumberStyles.Integer, CultureInfo.InvariantCulture, out gen);

            SiloAddress address = SiloAddress.New(IPAddress.Parse(gateway.Address!), proxyPort, gen);
            return address.ToGatewayUri();
        }

        public async Task<IList<Uri>> FindAllGatewayProxyEndpoints(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogDebugSearchingGateway(this.DeploymentId);

            try
            {
                const string Zero = "0";
                var queryResults = await storage.ReadTableEntriesAndEtagsAsync(
                    TableClient.CreateQueryFilter($"PartitionKey eq {DeploymentId} and Status eq {INSTANCE_STATUS_ACTIVE} and ProxyPort ne {Zero}"),
                    cancellationToken);

                var gatewaySiloInstances = queryResults.Select(entity => ConvertToGatewayUri(entity.Item1)).ToList();

                LogFoundGateway(gatewaySiloInstances.Count, this.DeploymentId);
                return gatewaySiloInstances;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                LogErrorSearchingGateway(exc, this.DeploymentId);
                throw;
            }
        }

        public async Task<string> DumpSiloInstanceTable()
        {
            var queryResults = await storage.ReadAllTableEntriesForPartitionAsync(this.DeploymentId);

            SiloInstanceTableEntry[] entries = queryResults.Select(entry => entry.Item1).ToArray();

            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.CurrentCulture, "Deployment {0}. Silos: ", DeploymentId));

            // 浏览结果,显示实体信息
            Array.Sort(entries,
                (e1, e2) =>
                {
                    if (e1 == null) return (e2 == null) ? 0 : -1;
                    if (e2 == null) return (e1 == null) ? 0 : 1;
                    if (e1.SiloName == null) return (e2.SiloName == null) ? 0 : -1;
                    if (e2.SiloName == null) return (e1.SiloName == null) ? 0 : 1;
                    return string.CompareOrdinal(e1.SiloName, e2.SiloName);
                });
            foreach (SiloInstanceTableEntry entry in entries)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.CurrentCulture,
                    "[IP {0}:{1}:{2}, {3}, Instance={4}, Status={5}]",
                    entry.Address,
                    entry.Port,
                    entry.Generation,
                    entry.HostName,
                    entry.SiloName,
                    entry.Status));
            }
            return sb.ToString();
        }

        internal Task<string> MergeTableEntryAsync(SiloInstanceTableEntry data, CancellationToken cancellationToken = default)
        {
            return storage.MergeTableEntryAsync(data, AzureTableUtils.ANY_ETAG, cancellationToken);
        }

        internal Task<(SiloInstanceTableEntry? Entity, string? ETag)> ReadSingleTableEntryAsync(string partitionKey, string rowKey)
        {
            return storage.ReadSingleTableEntryAsync(partitionKey, rowKey);
        }

        internal async Task<int> DeleteTableEntries(string clusterId, CancellationToken cancellationToken = default)
        {
            if (clusterId == null) throw new ArgumentNullException(nameof(clusterId));

            var entries = await storage.ReadAllTableEntriesForPartitionAsync(clusterId, cancellationToken);

            await DeleteEntriesBatch(entries, cancellationToken);

            return entries.Count;
        }

        /// <summary>
        /// 删除在截断前 最晚开始、心跳和怀疑时间的死记
        /// 使用行名表来保护同步更新并保存会籍版本。
        /// 打电话者在本地争论后,随后安排了尝试。
        /// </summary>
        public async Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            var entries = await FindAllSiloEntries(cancellationToken);
            var defunct = entries
                .Where(entry => !SiloInstanceTableEntry.IsVersionRow(entry.Entity.RowKey)
                    && entry.Entity.Status == INSTANCE_STATUS_DEAD
                    && GetEffectiveUpdateTime(entry.Entity) < beforeDate.UtcDateTime)
                .ToList();

            await DeleteEntriesBatch(defunct, cancellationToken);
        }

        private static DateTime GetEffectiveUpdateTime(SiloInstanceTableEntry entry)
        {
            var result = string.IsNullOrEmpty(entry.StartTime) ? default : LogFormatter.ParseDate(entry.StartTime);
            if (!string.IsNullOrEmpty(entry.IAmAliveTime))
            {
                var heartbeat = LogFormatter.ParseDate(entry.IAmAliveTime);
                result = heartbeat > result ? heartbeat : result;
            }

            if (!string.IsNullOrEmpty(entry.SuspectingTimes))
            {
                foreach (var value in entry.SuspectingTimes.Split('|'))
                {
                    var suspectTime = LogFormatter.ParseDate(value);
                    result = suspectTime > result ? suspectTime : result;
                }
            }

            return result;
        }

        private async Task DeleteEntriesBatch(List<(SiloInstanceTableEntry, string)> entriesList, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entriesList.Count <= this.storagePolicyOptions.MaxBulkUpdateRows)
            {
                await storage.DeleteTableEntriesAsync(entriesList, cancellationToken);
            }
            else
            {
                await TaskUtilities.WhenAllWithAggregateException(entriesList.BatchIEnumerable(this.storagePolicyOptions.MaxBulkUpdateRows)
                    .Select(batch => storage.DeleteTableEntriesAsync(batch, cancellationToken)));
            }
        }

        internal async Task<List<(SiloInstanceTableEntry, string)>> FindSiloEntryAndTableVersionRow(SiloAddress siloAddress, CancellationToken cancellationToken = default)
        {
            string rowKey = SiloInstanceTableEntry.ConstructRowKey(siloAddress);

            var filter = TableClient.CreateQueryFilter($"(PartitionKey eq {DeploymentId}) and ((RowKey eq {rowKey}) or (RowKey eq {SiloInstanceTableEntry.TABLE_VERSION_ROW}))");
            var queryResults = await storage.ReadTableEntriesAndEtagsAsync(filter, cancellationToken);
            if (queryResults.Count < 1 || queryResults.Count > 2)
                throw new KeyNotFoundException(string.Format(
                    CultureInfo.CurrentCulture,
                    "Could not find table version row or found too many entries. Was looking for key {0}, found = {1}",
                    siloAddress,
                    Utils.EnumerableToString(queryResults)));

            var numTableVersionRows = 0;
            foreach (var entry in queryResults)
            {
                if (entry.Item1.RowKey == SiloInstanceTableEntry.TABLE_VERSION_ROW)
                {
                    numTableVersionRows++;
                }
            }

            if (numTableVersionRows < 1)
                throw new KeyNotFoundException(string.Format(
                    CultureInfo.CurrentCulture,
                    "Did not read table version row. Read = {0}",
                    Utils.EnumerableToString(queryResults)));

            if (numTableVersionRows > 1)
                throw new KeyNotFoundException(string.Format(
                    CultureInfo.CurrentCulture,
                    "Read {0} table version rows, while was expecting only 1. Read = {1}",
                    numTableVersionRows,
                    Utils.EnumerableToString(queryResults)));

            return queryResults;
        }

        internal async Task<List<(SiloInstanceTableEntry Entity, string ETag)>> FindAllSiloEntries(
            CancellationToken cancellationToken = default)
        {
            for (var attempt = 0; attempt < MaxMembershipSnapshotAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = await membershipTableReadStorage.ReadAllTableEntriesForPartitionAsync(DeploymentId, cancellationToken);
                if (CanAcceptSnapshot(query.Entries))
                {
                    return RemoveBoundaryVersionRows(query.Entries);
                }
            }

            throw new InconsistentStateException(
                $"Unable to read a consistent membership snapshot for cluster '{DeploymentId}' from table '{TableName}' after {MaxMembershipSnapshotAttempts} attempts.");
        }

        private static bool CanAcceptSnapshot(List<(SiloInstanceTableEntry Entity, string ETag)> entries)
        {
            var version = ValidateAllSiloEntries(entries);
            var opening = entries[0].Entity.RowKey == SiloInstanceTableEntry.TABLE_VERSION_ROW_MIN;
            var closing = entries[^1].Entity.RowKey == SiloInstanceTableEntry.TABLE_VERSION_ROW_MAX;
            // 两种没有标记的标志都确定了传统格式,即使用传统符号读取语义。
            // 第一个成员以原子方式撰写新版本的两种标志。
            // 写时的查询重叠可以看到部分对, 所以重试部分对或不匹配对。
            if (!opening && !closing)
            {
                return true;
            }

            if (!opening || !closing)
            {
                return false;
            }

            var before = entries[0].Entity.MembershipVersion
                ?? throw new InvalidOperationException("The opening boundary row does not contain a membership version.");
            var after = entries[^1].Entity.MembershipVersion
                ?? throw new InvalidOperationException("The closing boundary row does not contain a membership version.");

            // 边界意识组织写道,所有三个版本都以原子方式提前。
            return (string.Equals(before, after, StringComparison.Ordinal)
                && string.Equals(before, version, StringComparison.Ordinal))
                || IsLegacyTableVersionAhead(version, before, after);
        }

        private static bool IsLegacyTableVersionAhead(string? tableVersion, string beforeVersion, string afterVersion)
        {
            // 旧作家只提前 VersionRow。两个标志的版本都具有遗留性
            // 包括可能撕破的笔记片等读义学,
            return int.TryParse(tableVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTableVersion)
                && int.TryParse(beforeVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBeforeVersion)
                && int.TryParse(afterVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAfterVersion)
                && parsedTableVersion > parsedBeforeVersion
                && parsedTableVersion > parsedAfterVersion;
        }

        private static List<(SiloInstanceTableEntry Entity, string ETag)> RemoveBoundaryVersionRows(
            List<(SiloInstanceTableEntry Entity, string ETag)> queryResults)
        {
            if (queryResults.Count > 0
                && queryResults[^1].Entity.RowKey == SiloInstanceTableEntry.TABLE_VERSION_ROW_MAX)
            {
                queryResults.RemoveAt(queryResults.Count - 1);
            }

            if (queryResults.Count > 0
                && queryResults[0].Entity.RowKey == SiloInstanceTableEntry.TABLE_VERSION_ROW_MIN)
            {
                queryResults.RemoveAt(0);
            }

            return queryResults;
        }

        private static string? ValidateAllSiloEntries(List<(SiloInstanceTableEntry Entity, string ETag)> queryResults)
        {
            if (queryResults.Count < 1)
                throw new KeyNotFoundException(string.Format(
                    CultureInfo.CurrentCulture,
                    "Could not find enough rows in the FindAllSiloEntries call. Found = {0}",
                    Utils.EnumerableToString(queryResults)));

            var numTableVersionRows = 0;
            string? tableVersion = null;
            foreach (var entry in queryResults)
            {
                if (entry.Entity.RowKey == SiloInstanceTableEntry.TABLE_VERSION_ROW)
                {
                    numTableVersionRows++;
                    tableVersion = entry.Entity.MembershipVersion;
                }
            }

            if (numTableVersionRows < 1)
                throw new KeyNotFoundException(string.Format(
                    CultureInfo.CurrentCulture,
                    "Did not find table version row. Read = {0}",
                    Utils.EnumerableToString(queryResults)));
            if (numTableVersionRows > 1)
                throw new KeyNotFoundException(string.Format(
                    CultureInfo.CurrentCulture,
                    "Read {0} table version rows, while was expecting only 1. Read = {1}",
                    numTableVersionRows,
                    Utils.EnumerableToString(queryResults)));

            return tableVersion;
        }

        /// <summary>
        /// 插入( 创建新) 行条目
        /// </summary>
        internal async Task<bool> TryCreateTableVersionEntryAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var versionRow = await storage.ReadSingleTableEntryAsync(DeploymentId, SiloInstanceTableEntry.TABLE_VERSION_ROW, cancellationToken);
                if (versionRow.Entity != null)
                {
                    return false;
                }

                var entry = CreateTableVersionEntry(0);
                var boundaryEntries = CreateBoundaryVersionEntries(entry);
                await storage.CreateTableEntriesAsync([entry, boundaryEntries.Min, boundaryEntries.Max], cancellationToken);
                return true;
            }
            catch (Exception exc)
            {
                if (!AzureTableUtils.EvaluateException(exc, out var httpStatusCode, out var restStatus)) throw;

                LogTraceInsertSiloEntryConditionallyFailed(httpStatusCode, restStatus);
                if (httpStatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed) return false;

                throw;
            }
        }

        /// <summary>
        /// 插入( 创建新) 行条目
        /// </summary>
        /// <param name="siloEntry">Silo 条目待写入</param>
        /// <param name="tableVersionEntry">更新</param>的版本行
        /// <param name="tableVersionEtag">版本行 eTag</param>
        /// <param name="cancellationToken">取消牌。</param>
        internal async Task<bool> InsertSiloEntryConditionally(SiloInstanceTableEntry siloEntry, SiloInstanceTableEntry tableVersionEntry, string tableVersionEtag, CancellationToken cancellationToken = default)
        {
            try
            {
                var boundaryEntries = CreateBoundaryVersionEntries(tableVersionEntry);
                await storage.CreateAndUpdateTableEntriesAsync(
                    siloEntry,
                    (tableVersionEntry, tableVersionEtag),
                    (boundaryEntries.Min, boundaryEntries.Max),
                    cancellationToken);
                return true;
            }
            catch (Exception exc)
            {
                if (!AzureTableUtils.EvaluateException(exc, out var httpStatusCode, out var restStatus)) throw;

                LogTraceInsertSiloEntryConditionallyFailed(httpStatusCode, restStatus);
                if (httpStatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed
                    || exc is RequestFailedException requestFailed && IsRowNotFound(requestFailed)) return false;

                throw;
            }
        }

        /// <summary>
        /// 原子取代卡尼科式表格转换电子标签下的一个成员行。
        /// </summary>
        /// <param name="siloEntry">Silo 条目待写入</param>
        /// <param name="tableVersionEntry">更新</param>的版本行
        /// <param name="versionEtag">版本行</param>的ETag值
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns></returns>
        internal async Task<bool> UpdateSiloEntryConditionally(SiloInstanceTableEntry siloEntry, SiloInstanceTableEntry tableVersionEntry, string versionEtag, CancellationToken cancellationToken = default)
        {
            try
            {
                var boundaryEntries = CreateBoundaryVersionEntries(tableVersionEntry);
                await storage.UpdateTableEntriesAsync(
                    (siloEntry, AzureTableUtils.ANY_ETAG),
                    (tableVersionEntry, versionEtag),
                    (boundaryEntries.Min, boundaryEntries.Max),
                    cancellationToken);
                return true;
            }
            catch (Exception exc)
            {
                if (!AzureTableUtils.EvaluateException(exc, out var httpStatusCode, out var restStatus)) throw;

                LogTraceUpdateSiloEntryConditionallyFailed(httpStatusCode, restStatus);
                if (httpStatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed
                    || exc is RequestFailedException requestFailed && IsRowNotFound(requestFailed)) return false;

                throw;
            }
        }

        private static bool IsRowNotFound(RequestFailedException exception)
            => exception.Status == (int)HttpStatusCode.NotFound
                && (exception.ErrorCode == TableErrorCode.ResourceNotFound.ToString()
                    || exception.ErrorCode == TableErrorCode.EntityNotFound.ToString());

        [LoggerMessage(
            EventId = (int)TableStorageErrorCode.AzureTable_33,
            Level = LogLevel.Error,
            Message = "Exception trying to create or connect to the Azure table {TableName}"
        )]
        private partial void LogErrorConnectingToAzureTable(Exception exception, string tableName);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100270,
            Level = LogLevel.Information,
            Message = "Registering silo instance: {Data}"
        )]
        private partial void LogRegisterSiloInstance(SiloInstanceTableEntry data);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100271,
            Level = LogLevel.Information,
            Message = "Unregistering silo instance: {Data}"
        )]
        private partial void LogUnregisterSiloInstance(SiloInstanceTableEntry data);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100272,
            Level = LogLevel.Information,
            Message = "Activating silo instance: {Data}"
        )]
        private partial void LogActivateSiloInstance(SiloInstanceTableEntry data);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100277,
            Level = LogLevel.Debug,
            Message = "Searching for active gateway silos for deployment {DeploymentId}."
        )]
        private partial void LogDebugSearchingGateway(string deploymentId);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100278,
            Level = LogLevel.Information,
            Message = "Found {GatewaySiloCount} active Gateway Silos for deployment {DeploymentId}."
        )]
        private partial void LogFoundGateway(int gatewaySiloCount, string deploymentId);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100331,
            Level = LogLevel.Error,
            Message = "Error searching for active gateway silos for deployment {DeploymentId} "
        )]
        private partial void LogErrorSearchingGateway(Exception exception, string deploymentId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "InsertSiloEntryConditionally failed with httpStatusCode={HttpStatusCode}, restStatus={RestStatus}"
        )]
        private partial void LogTraceInsertSiloEntryConditionallyFailed(HttpStatusCode httpStatusCode, string? restStatus);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "UpdateSiloEntryConditionally failed with httpStatusCode={HttpStatusCode}, restStatus={RestStatus}"
        )]
        private partial void LogTraceUpdateSiloEntryConditionallyFailed(HttpStatusCode httpStatusCode, string? restStatus);
    }

    internal readonly record struct MembershipTableQueryResult(
        List<(SiloInstanceTableEntry Entity, string ETag)> Entries,
        bool IsPaginated);

    internal interface IMembershipTableReadStorage
    {
        Task<MembershipTableQueryResult> ReadAllTableEntriesForPartitionAsync(
            string partitionKey,
            CancellationToken cancellationToken = default);
    }

    internal sealed class AzureMembershipTableReadStorage(AzureTableDataManager<SiloInstanceTableEntry> storage)
        : IMembershipTableReadStorage
    {
        public async Task<MembershipTableQueryResult> ReadAllTableEntriesForPartitionAsync(
            string partitionKey,
            CancellationToken cancellationToken = default)
        {
            var result = await storage.ReadAllTableEntriesForPartitionWithPaginationAsync(partitionKey, cancellationToken);
            return new(result.Entries, result.IsPaginated);
        }
    }
}
