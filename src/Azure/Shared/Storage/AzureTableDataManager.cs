using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

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
    /// 封装对 Azure Table Storage 按行访问的实用类。
    /// </summary>
    /// <typeparam name="T">此表或管理器使用的表数据记录类型。</typeparam>
    internal partial class AzureTableDataManager<T> where T : class, ITableEntity
    {
        private readonly AzureStorageOperationOptions options;

        /// <summary>此实例所管理的表名称。</summary>
        public string TableName { get; }

        /// <summary>此表管理器实例使用的日志记录器。</summary>
        protected internal ILogger Logger { get; }

        public AzureStoragePolicyOptions StoragePolicyOptions { get; }

        public TableClient Table { get; private set; } = null!;

        /// <summary>
        /// 创建新的 <see cref="AzureTableDataManager{T}"/> 实例。
        /// </summary>
        /// <param name="options">存储配置。</param>
        /// <param name="logger">要使用的日志记录器。</param>
        public AzureTableDataManager(AzureStorageOperationOptions options, ILogger logger)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));

            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            TableName = options.TableName ?? throw new ArgumentException("A table name must be configured.", nameof(options));
            StoragePolicyOptions = options.StoragePolicyOptions ?? throw new ArgumentException("Storage policy options must be configured.", nameof(options));

            AzureTableUtils.ValidateTableName(TableName);
        }

        /// <summary>
        /// 连接 Azure 表；如果表尚不存在，则创建并初始化新表。
        /// </summary>
        /// <param name="cancellationToken">用于取消表创建的令牌。</param>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public async Task InitTableAsync(CancellationToken cancellationToken = default)
        {
            const string operation = "InitTable";
            var startTime = DateTime.UtcNow;

            try
            {
                TableServiceClient tableCreationClient = await GetCloudTableCreationClientAsync(cancellationToken);
                var table = tableCreationClient.GetTableClient(TableName);
                var response = await table.CreateIfNotExistsAsync(cancellationToken);
                var alreadyExisted = response.GetRawResponse().Status == (int)HttpStatusCode.Conflict;

                LogInfoTableCreation(Logger, alreadyExisted ? "Attached to" : "Created", TableName);
                Table = table;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException te)
            {
                LogErrorTableCreationInTimeout(Logger, te, StoragePolicyOptions.CreationTimeout);
                throw new OrleansException($"Unable to create or connect to the Azure table in {StoragePolicyOptions.CreationTimeout}", te);
            }
            catch (Exception exc)
            {
                LogErrorTableCreation(Logger, exc, TableName);
                throw;
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 删除 Azure 表。
        /// </summary>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public async Task DeleteTableAsync()
        {
            const string operation = "DeleteTable";
            var startTime = DateTime.UtcNow;

            try
            {
                var tableCreationClient = await GetCloudTableCreationClientAsync();
                var response = await tableCreationClient.DeleteTableAsync(TableName);
                if (response.Status == 204)
                {
                    LogInfoTableDeletion(Logger, TableName);
                }
            }
            catch (Exception exc)
            {
                LogErrorTableDeletion(Logger, exc, TableName);
                throw;
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 删除 Azure 表中的全部实体。
        /// </summary>
        /// <returns>表示此操作完成情况的异步任务。</returns>
        public async Task ClearTableAsync()
        {
            var items = await ReadAllTableEntriesAsync();
            IEnumerable<Task> work = items.GroupBy(item => item.Item1.PartitionKey)
                                          .SelectMany(partition => partition.ToBatch(this.StoragePolicyOptions.MaxBulkUpdateRows))
                                          .Select(batch => DeleteTableEntriesAsync(batch.ToList()));
            await Task.WhenAll(work);
        }

        /// <summary>
        /// 在 Azure 表中创建新数据记录（插入新记录，而非更新现有记录）。
        /// 数据已存在时失败。
        /// </summary>
        /// <param name="data">要插入表中的数据。</param>
        /// <returns>异步任务，完成此存储操作后返回该数据条目的新 ETag。</returns>
        public Task<string> CreateTableEntryAsync(T data) => CreateTableEntryAsync(data, CancellationToken.None);

        /// <summary>
        /// 使用提供的取消令牌创建新表记录。
        /// </summary>
        /// <param name="data">要插入的记录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>新记录的 ETag。</returns>
        public async Task<string> CreateTableEntryAsync(T data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "CreateTableEntry";
            var startTime = DateTime.UtcNow;

            LogTraceTableEntryCreation(Logger, TableName, data);
            try
            {
                try
                {
                    var opResult = await Table.AddEntityAsync(data, cancellationToken);
                    return opResult.Headers.ETag.GetValueOrDefault().ToString();
                }
                catch (Exception exc) when (exc is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    CheckAlertWriteError(operation, data, null, exc);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 在 Azure 表中插入数据记录：若不存在则创建新记录，否则覆盖现有版本（不检查 ETag，采用就地更新语义）。
        /// </summary>
        /// <param name="data">要插入表中或替换现有记录的数据。</param>
        /// <returns>异步任务，完成此存储操作后返回该数据条目的新 ETag。</returns>
        public async Task<string> UpsertTableEntryAsync(T data)
        {
            const string operation = "UpsertTableEntry";
            var startTime = DateTime.UtcNow;
            LogTraceTableEntry(Logger, operation, data, TableName);
            try
            {
                try
                {
                    var opResult = await Table.UpsertEntityAsync(data);
                    return opResult.Headers.ETag.GetValueOrDefault().ToString();
                }
                catch (Exception exc)
                {
                    LogWarningUpsertTableEntry(Logger, exc, data, TableName);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 在 Azure 表中插入数据记录：若不存在则创建新记录。
        /// </summary>
        /// <param name="data">要插入表中或替换现有记录的数据。</param>
        /// <returns>异步任务，完成此存储操作后返回该数据条目的新 ETag。（译注：实际返回包含成功标志和 ETag 的元组，发生插入冲突时返回 (false, null)。）</returns>
        public async Task<(bool isSuccess, string? eTag)> InsertTableEntryAsync(T data)
        {
            const string operation = "InsertTableEntry";
            var startTime = DateTime.UtcNow;
            LogTraceTableEntry(Logger, operation, data, TableName);
            try
            {
                try
                {
                    var opResult = await Table.AddEntityAsync(data);
                    return (true, opResult.Headers.ETag.GetValueOrDefault().ToString());
                }
                catch (RequestFailedException storageException) when (storageException.Status == (int)HttpStatusCode.Conflict)
                {
                    return (false, null);
                }
                catch (Exception exc)
                {
                    LogWarningInsertTableEntry(Logger, exc, data, TableName);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 合并 Azure 表中的数据记录。
        /// </summary>
        /// <param name="data">要合并到表中的数据。</param>
        /// <param name="eTag">要应用的 ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务，完成此存储操作后返回该数据条目的新 ETag。</returns>
        internal Task<string> MergeTableEntryAsync(T data, string eTag, CancellationToken cancellationToken = default) => MergeTableEntryAsync(data, new ETag(eTag), cancellationToken);

        /// <summary>
        /// 合并 Azure 表中的数据记录。
        /// </summary>
        /// <param name="data">要合并到表中的数据。</param>
        /// <param name="eTag">要应用的 ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务，完成此存储操作后返回该数据条目的新 ETag。</returns>
        internal async Task<string> MergeTableEntryAsync(T data, ETag eTag, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "MergeTableEntry";
            var startTime = DateTime.UtcNow;
            LogTraceTableEntry(Logger, operation, data, TableName);
            try
            {
                try
                {
                    // 合并操作要求提供 ETag（可以是 '*' 通配符）。
                    data.ETag = eTag;
                    var opResult = await Table.UpdateEntityAsync(data, data.ETag, TableUpdateMode.Merge, cancellationToken);
                    return opResult.Headers.ETag.GetValueOrDefault().ToString();
                }
                catch (Exception exc)
                {
                    LogWarningMergeTableEntry(Logger, exc, data, TableName);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 更新 Azure 表中的数据记录：使用 ETag 更新表中已经存在的数据。
        /// 数据尚不存在或 ETag 不匹配时失败。
        /// </summary>
        /// <param name="data">要更新到表中的数据。</param>
        /// /// <param name="dataEtag">要使用的 ETag。</param>
        /// <returns>异步任务，完成此存储操作后返回该数据条目的新 ETag。</returns>
        public Task<string> UpdateTableEntryAsync(T data, string dataEtag) => UpdateTableEntryAsync(data, new ETag(dataEtag));

        /// <summary>
        /// 更新 Azure 表中的数据记录：使用 ETag 更新表中已经存在的数据。
        /// 数据尚不存在或 ETag 不匹配时失败。
        /// </summary>
        /// <param name="data">要更新到表中的数据。</param>
        /// /// <param name="dataEtag">要使用的 ETag。</param>
        /// <returns>异步任务，完成此存储操作后返回该数据条目的新 ETag。</returns>
        public Task<string> UpdateTableEntryAsync(T data, ETag dataEtag) => UpdateTableEntryAsync(data, dataEtag, CancellationToken.None);

        /// <summary>
        /// 使用提供的取消令牌有条件地替换表记录。
        /// </summary>
        /// <param name="data">要更新的记录。</param>
        /// <param name="dataEtag">预期的 ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>更新后记录的 ETag。</returns>
        public async Task<string> UpdateTableEntryAsync(T data, ETag dataEtag, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "UpdateTableEntryAsync";
            var startTime = DateTime.UtcNow;
            LogTraceTableEntry(Logger, operation, data, TableName);

            try
            {
                try
                {
                    data.ETag = dataEtag;
                    var opResult = await Table.UpdateEntityAsync(data, data.ETag, TableUpdateMode.Replace, cancellationToken);

                    // 后续操作需要数据的 ETag。
                    return opResult.Headers.ETag.GetValueOrDefault().ToString();
                }
                catch (Exception exc) when (exc is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    CheckAlertWriteError(operation, data, null, exc);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 使用 ETag 删除表中已经存在的数据。
        /// 数据尚不存在或 ETag 不匹配时失败。
        /// </summary>
        /// <param name="data">要从表中删除的数据记录。</param>
        /// <param name="eTag">要使用的 ETag。</param>
        /// <returns>表示此存储操作完成情况的异步任务。</returns>
        public Task DeleteTableEntryAsync(T data, string eTag) => DeleteTableEntryAsync(data, new ETag(eTag));

        /// <summary>
        /// 使用 ETag 删除表中已经存在的数据。
        /// 数据尚不存在或 ETag 不匹配时失败。
        /// </summary>
        /// <param name="data">要从表中删除的数据记录。</param>
        /// <param name="eTag">要使用的 ETag。</param>
        /// <returns>表示此存储操作完成情况的异步任务。</returns>
        public Task DeleteTableEntryAsync(T data, ETag eTag) => DeleteTableEntryAsync(data, eTag, CancellationToken.None);

        /// <summary>
        /// 使用提供的取消令牌有条件地删除表记录。
        /// </summary>
        /// <param name="data">要删除的记录。</param>
        /// <param name="eTag">预期的 ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示删除操作的任务。</returns>
        public async Task DeleteTableEntryAsync(T data, ETag eTag, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "DeleteTableEntryAsync";
            var startTime = DateTime.UtcNow;
            LogTraceTableEntry(Logger, operation, data, TableName);
            try
            {
                data.ETag = eTag;
                try
                {
                    var response = await Table.DeleteEntityAsync(data.PartitionKey, data.RowKey, data.ETag, cancellationToken);
                    if (response is { Status: 404 })
                    {
                        throw new RequestFailedException(response.Status, "Resource not found", response.ReasonPhrase, null);
                    }
                }
                catch (Exception exc) when (exc is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LogWarningDeleteTableEntry(Logger, exc, data, TableName);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 从存储表读取单条表记录。
        /// </summary>
        /// <param name="partitionKey">记录的分区键。</param>
        /// <param name="rowKey">记录的行键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>完成时返回包含数据记录及其 ETag 的元组的异步结果。</returns>
        public async Task<(T? Entity, string? ETag)> ReadSingleTableEntryAsync(
            string partitionKey,
            string rowKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "ReadSingleTableEntryAsync";
            var startTime = DateTime.UtcNow;
            LogTraceTableOperation(Logger, operation, TableName, partitionKey, rowKey);
            try
            {
                try
                {
                    var result = await Table.GetEntityIfExistsAsync<T>(
                        partitionKey,
                        rowKey,
                        cancellationToken: cancellationToken);
                    if (result.HasValue)
                    {
                        // 后续操作需要数据的 ETag。
                        return (result.Value!, result.Value!.ETag.ToString());
                    }
                }
                catch (RequestFailedException exception)
                {
                    if (!AzureTableUtils.TableStorageDataNotFound(exception))
                    {
                        throw;
                    }
                }

                LogDebugTableEntryNotFound(Logger, partitionKey, rowKey);
                return (default, default);  // 无数据。
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 读取存储表中一个分区的全部记录。
        /// 注意：对于较大的表分区，此操作可能成本较高且耗时较长！
        /// </summary>
        /// <param name="partitionKey">要搜索的分区的键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>指定表分区中全部记录的可枚举序列。</returns>
        public Task<List<(T Entity, string ETag)>> ReadAllTableEntriesForPartitionAsync(
            string partitionKey,
            CancellationToken cancellationToken = default)
        {
            string query = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
            return ReadTableEntriesAndEtagsAsync(query, cancellationToken);
        }

        /// <summary>
        /// 读取存储表中一个分区的全部记录，同时保留查询是否需要多个响应页的信息。
        /// </summary>
        /// <param name="partitionKey">要搜索的分区的键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>分区中的记录，以及是否需要多个响应页。</returns>
        public Task<(List<(T Entity, string ETag)> Entries, bool IsPaginated)> ReadAllTableEntriesForPartitionWithPaginationAsync(
            string partitionKey,
            CancellationToken cancellationToken = default)
        {
            string query = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
            return ReadTableEntriesAndEtagsWithPaginationAsync(query, cancellationToken);
        }

        /// <summary>
        /// 读取表中的全部记录。
        /// 注意：对于较大的表，此操作可能成本很高且耗时很长！
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表中全部记录的可枚举序列。</returns>
        public Task<List<(T Entity, string ETag)>> ReadAllTableEntriesAsync(
            CancellationToken cancellationToken = default)
        {
            return ReadTableEntriesAndEtagsAsync(null, cancellationToken);
        }

        /// <summary>
        /// 使用 ETag 删除表中一组已经存在的数据记录。
        /// 数据尚不存在或 ETag 不匹配时失败。
        /// </summary>
        /// <param name="collection">要从表中删除的数据记录及其对应的 ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示此存储操作完成情况的异步任务。</returns>
        public async Task DeleteTableEntriesAsync(List<(T Entity, string ETag)> collection, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "DeleteTableEntries";
            var startTime = DateTime.UtcNow;
            LogTraceTableEntries(Logger, operation, new(collection), TableName);

            if (collection == null) throw new ArgumentNullException(nameof(collection));

            if (collection.Count > this.StoragePolicyOptions.MaxBulkUpdateRows)
            {
                throw new ArgumentOutOfRangeException(nameof(collection), collection.Count,
                        "Too many rows for bulk delete - max " + this.StoragePolicyOptions.MaxBulkUpdateRows);
            }

            if (collection.Count == 0)
            {
                return;
            }

            try
            {
                var entityBatch = new List<TableTransactionAction>();
                foreach (var tuple in collection)
                {
                    T item = tuple.Entity;
                    item.ETag = new ETag(tuple.ETag);
                    entityBatch.Add(new TableTransactionAction(TableTransactionActionType.Delete, item, item.ETag));
                }

                try
                {
                    _ = await Table.SubmitTransactionAsync(entityBatch, cancellationToken);
                }
                catch (Exception exc)
                {
                    LogWarningDeleteTableEntries(Logger, exc, new(collection), TableName);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 从 Azure 表读取数据记录及其对应的 ETag。
        /// </summary>
        /// <param name="filter">用于查询表并筛选结果的筛选字符串。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表中满足查询条件的记录的可枚举序列。</returns>
        public async Task<List<(T Entity, string ETag)>> ReadTableEntriesAndEtagsAsync(
            string? filter,
            CancellationToken cancellationToken = default)
        {
            var result = await ReadTableEntriesAndEtagsWithPaginationAsync(filter, cancellationToken);
            return result.Entries;
        }

        private async Task<(List<(T Entity, string ETag)> Entries, bool IsPaginated)> ReadTableEntriesAndEtagsWithPaginationAsync(
            string? filter,
            CancellationToken cancellationToken)
        {
            const string operation = "ReadTableEntriesAndEtags";
            var startTime = DateTime.UtcNow;

            try
            {
                var results = new List<(T, string)>();
                var pageCount = 0;
                await foreach (var page in Table.QueryAsync<T>(filter, cancellationToken: cancellationToken).AsPages())
                {
                    pageCount++;
                    results.EnsureCapacity(results.Count + page.Values.Count);
                    foreach (var value in page.Values)
                    {
                        results.Add((value, value.ETag.ToString()));
                    }
                }

                return (results, pageCount > 1);
            }
            catch (Exception exc) when (exc is not OperationCanceledException)
            {
                if (!AzureTableUtils.TableStorageDataNotFound(exc))
                {
                    LogWarningReadTable(Logger, exc, TableName);
                }

                throw new OrleansException($"Failed to read Azure Storage table {TableName}", exc);
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        /// <summary>
        /// 向表中插入一组新数据记录。
        /// 数据已经存在时失败。
        /// </summary>
        /// <param name="collection">要插入表中的数据记录。</param>
        /// <returns>表示此存储操作完成情况的异步任务。</returns>
        public async Task BulkInsertTableEntries(IReadOnlyCollection<T> collection)
        {
            const string operation = "BulkInsertTableEntries";
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (collection.Count > this.StoragePolicyOptions.MaxBulkUpdateRows)
            {
                throw new ArgumentOutOfRangeException(nameof(collection), collection.Count,
                        "Too many rows for bulk update - max " + this.StoragePolicyOptions.MaxBulkUpdateRows);
            }

            if (collection.Count == 0)
            {
                return;
            }

            var startTime = DateTime.UtcNow;
            LogTraceTableEntriesCount(Logger, operation, collection.Count, TableName);
            try
            {
                var entityBatch = new List<TableTransactionAction>(collection.Count);
                foreach (T entry in collection)
                {
                    entityBatch.Add(new TableTransactionAction(TableTransactionActionType.Add, entry));
                }

                try
                {
                    await Table.SubmitTransactionAsync(entityBatch);
                }
                catch (Exception exc)
                {
                    LogWarningBulkInsertTableEntries(Logger, exc, collection.Count, TableName);
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        internal async Task CreateTableEntriesAsync(IReadOnlyCollection<T> collection, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "CreateTableEntries";
            const int maxTransactionSize = 100;
            ArgumentNullException.ThrowIfNull(collection);
            if (collection.Count is 0 or > maxTransactionSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(collection),
                    collection.Count,
                    $"The collection must contain between 1 and {maxTransactionSize} rows.");
            }

            var startTime = DateTime.UtcNow;
            LogTraceTableEntriesCount(Logger, operation, collection.Count, TableName);
            try
            {
                try
                {
                    var transaction = new List<TableTransactionAction>(collection.Count);
                    foreach (var entry in collection)
                    {
                        transaction.Add(new TableTransactionAction(TableTransactionActionType.Add, entry));
                    }

                    await Table.SubmitTransactionAsync(transaction, cancellationToken);
                }
                catch (Exception exception)
                {
                    CheckAlertWriteError(operation, collection, null, exception);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        internal async Task<(string CreatedEntryETag, string UpdatedEntryETag)> CreateAndUpdateTableEntriesAsync(
            T entryToCreate,
            (T Entity, string ETag) entryToUpdate,
            (T First, T Second)? entriesToUpsert = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "CreateAndUpdateTableEntries";
            ArgumentNullException.ThrowIfNull(entryToUpdate.Entity);
            string? entryToUpdateString = entryToUpdate.Entity.ToString();
            var startTime = DateTime.UtcNow;

            LogTraceTableEntries(Logger, operation, entryToCreate, entryToUpdateString, TableName);
            try
            {
                try
                {
                    entryToUpdate.Entity.ETag = new ETag(entryToUpdate.ETag);
                    var transaction = new List<TableTransactionAction>(entriesToUpsert.HasValue ? 4 : 2)
                    {
                        new(TableTransactionActionType.Add, entryToCreate),
                        new(TableTransactionActionType.UpdateReplace, entryToUpdate.Entity, entryToUpdate.Entity.ETag)
                    };
                    if (entriesToUpsert is { } upserts)
                    {
                        transaction.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, upserts.First));
                        transaction.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, upserts.Second));
                    }

                    var opResults = await Table.SubmitTransactionAsync(transaction, cancellationToken);

                    // 批处理结果按照执行顺序返回，
                    // 参考：https://msdn.microsoft.com/en-us/library/microsoft.windowsazure.storage.table.cloudtable.executebatch.aspx。
                    // 后续操作需要数据的 ETag。
                    var resultETag1 = opResults.Value[0].Headers.ETag.GetValueOrDefault().ToString();
                    var resultETag2 = opResults.Value[1].Headers.ETag.GetValueOrDefault().ToString();
                    return (resultETag1, resultETag2);
                }
                catch (Exception exc)
                {
                    CheckAlertWriteError(operation, entryToCreate, entryToUpdateString, exc);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        internal async Task<(string FirstEntryETag, string SecondEntryETag)> UpdateTableEntriesAsync(
            (T Entity, string ETag) firstEntry,
            (T Entity, string ETag) secondEntry,
            (T First, T Second)? entriesToUpsert = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string operation = "UpdateTableEntries";
            ArgumentNullException.ThrowIfNull(firstEntry.Entity);
            ArgumentNullException.ThrowIfNull(secondEntry.Entity);
            string? secondEntryString = secondEntry.Entity.ToString();
            var startTime = DateTime.UtcNow;
            LogTraceTableEntries(Logger, operation, firstEntry.Entity, secondEntryString, TableName);

            try
            {
                try
                {
                    firstEntry.Entity.ETag = new ETag(firstEntry.ETag);
                    var entityBatch = new List<TableTransactionAction>(entriesToUpsert.HasValue ? 4 : 2)
                    {
                        new(TableTransactionActionType.UpdateReplace, firstEntry.Entity, firstEntry.Entity.ETag)
                    };

                    secondEntry.Entity.ETag = new ETag(secondEntry.ETag);
                    entityBatch.Add(new TableTransactionAction(
                        TableTransactionActionType.UpdateReplace,
                        secondEntry.Entity,
                        secondEntry.Entity.ETag));

                    if (entriesToUpsert is { } upserts)
                    {
                        entityBatch.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, upserts.First));
                        entityBatch.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, upserts.Second));
                    }

                    var opResults = await Table.SubmitTransactionAsync(entityBatch, cancellationToken);

                    // 批处理结果按照执行顺序返回，
                    // 参考：https://msdn.microsoft.com/en-us/library/microsoft.windowsazure.storage.table.cloudtable.executebatch.aspx。
                    // 后续操作需要数据的 ETag。
                    var resultETag1 = opResults.Value[0].Headers.ETag.GetValueOrDefault().ToString();
                    var resultETag2 = opResults.Value[1].Headers.ETag.GetValueOrDefault().ToString();
                    return (resultETag1, resultETag2);
                }
                catch (Exception exc)
                {
                    CheckAlertWriteError(operation, firstEntry.Entity, secondEntryString, exc);
                    throw;
                }
            }
            finally
            {
                CheckAlertSlowAccess(startTime, operation);
            }
        }

        private async ValueTask<TableServiceClient> GetCloudTableCreationClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // TableServiceClient 不是可释放对象。工厂可以由应用程序提供，
                // 且不接受取消令牌，因此取消只能限制此处的等待。
                var clientTask = options.CreateClient();
                clientTask.Ignore();
                return await clientTask.WaitAsync(cancellationToken);
            }
            catch (Exception exc)
            {
                LogErrorTableServiceClientCreation(Logger, exc);
                throw;
            }
        }

        private void CheckAlertWriteError(string operation, object? data1, string? data2, Exception exc)
        {
            HttpStatusCode httpStatusCode;
            if (AzureTableUtils.EvaluateException(exc, out httpStatusCode, out _) && AzureTableUtils.IsContentionError(httpStatusCode))
            {
                // 以 Verbose 级别记录日志，因为条件操作失败并不是错误。如有必要，稍后分析并发出警告。
                LogWarningTableWrite(Logger, operation, TableName, data1 ?? "null", data2 ?? "null");
            }
            else
            {
                LogErrorTableWrite(Logger, exc, operation, TableName, data1);
            }
        }

        private void CheckAlertSlowAccess(DateTime startOperation, string operation)
        {
            var timeSpan = DateTime.UtcNow - startOperation;
            if (timeSpan > this.StoragePolicyOptions.OperationTimeout)
            {
                LogWarningSlowAccess(Logger, operation, TableName, timeSpan);
            }
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)Utilities.ErrorCode.AzureTable_01,
            Message = "{Action} Azure storage table {TableName}"
        )]
        private static partial void LogInfoTableCreation(ILogger logger, string action, string tableName);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)Utilities.ErrorCode.AzureTable_TableNotCreated,
            Message = "Unable to create or connect to the Azure table in {CreationTimeout}"
        )]
        private static partial void LogErrorTableCreationInTimeout(ILogger logger, TimeoutException exception, TimeSpan creationTimeout);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)Utilities.ErrorCode.AzureTable_02,
            Message = "Could not initialize connection to storage table {TableName}"
        )]
        private static partial void LogErrorTableCreation(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)Utilities.ErrorCode.AzureTable_03,
            Message = "Deleted Azure storage table {TableName}"
        )]
        private static partial void LogInfoTableDeletion(ILogger logger, string tableName);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)Utilities.ErrorCode.AzureTable_04,
            Message = "Could not delete storage table {TableName}"
        )]
        private static partial void LogErrorTableDeletion(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Creating {TableName} table entry: {Data}"
        )]
        private static partial void LogTraceTableEntryCreation(ILogger logger, string tableName, T data);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{Operation} entry {Data} into table {TableName}"
        )]
        private static partial void LogTraceTableEntry(ILogger logger, string operation, T data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_06,
            Message = "Intermediate error upserting entry {Data} to the table {TableName}"
        )]
        private static partial void LogWarningUpsertTableEntry(ILogger logger, Exception exception, T data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_06,
            Message = "Intermediate error inserting entry {Data} to the table {TableName}"
        )]
        private static partial void LogWarningInsertTableEntry(ILogger logger, Exception exception, T data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_07,
            Message = "Intermediate error merging entry {Data} to the table {TableName}"
        )]
        private static partial void LogWarningMergeTableEntry(ILogger logger, Exception exception, T data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_08,
            Message = "Intermediate error deleting entry {Data} from the table {TableName}."
        )]
        private static partial void LogWarningDeleteTableEntry(ILogger logger, Exception exception, T data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{Operation} table {TableName} partitionKey {PartitionKey} rowKey {RowKey}"
        )]
        private static partial void LogTraceTableOperation(ILogger logger, string operation, string tableName, string partitionKey, string rowKey);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Could not find table entry for PartitionKey={PartitionKey} RowKey={RowKey}"
        )]
        private static partial void LogDebugTableEntryNotFound(ILogger logger, string partitionKey, string rowKey);

        private readonly struct CollectionLogEntry(List<(T Entity, string ETag)> collection)
        {
            public override string ToString() => Utils.EnumerableToString(collection);
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{Operation} entries: {Data} table {TableName}"
        )]
        private static partial void LogTraceTableEntries(ILogger logger, string operation, CollectionLogEntry data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_08,
            Message = "Intermediate error deleting entries {Data} from the table {TableName}."
        )]
        private static partial void LogWarningDeleteTableEntries(ILogger logger, Exception exception, CollectionLogEntry data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_09,
            Message = "Failed to read Azure Storage table {TableName}"
        )]
        private static partial void LogWarningReadTable(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{Operation} {Count} entries table {TableName}"
        )]
        private static partial void LogTraceTableEntriesCount(ILogger logger, string operation, int count, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_37,
            Message = "Intermediate error bulk inserting {Count} entries in the table {TableName}"
        )]
        private static partial void LogWarningBulkInsertTableEntries(ILogger logger, Exception exception, int count, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{Operation} data1 {Data1} data2 {Data2} table {TableName}"
        )]
        private static partial void LogTraceTableEntries(ILogger logger, string operation, T data1, string? data2, string tableName);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error creating TableServiceClient."
        )]
        private static partial void LogErrorTableServiceClientCreation(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)Utilities.ErrorCode.AzureTable_13,
            Message = "Intermediate Azure table write error {Operation} to table {TableName} data1 {Data1} data2 {Data2}"
        )]
        private static partial void LogWarningTableWrite(ILogger logger, string operation, string tableName, object data1, object data2);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)Utilities.ErrorCode.AzureTable_14,
            Message = "Azure table access write error {Operation} to table {TableName} entry {Data1}"
        )]
        private static partial void LogErrorTableWrite(ILogger logger, Exception exception, string operation, string tableName, object? data1);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)Utilities.ErrorCode.AzureTable_15,
            Message = "Slow access to Azure Table {TableName} for {Operation}, which took {Duration}"
        )]
        private static partial void LogWarningSlowAccess(ILogger logger, string tableName, string operation, TimeSpan duration);
    }

    internal static class TableDataManagerInternalExtensions
    {
        internal static IEnumerable<IEnumerable<TItem>> ToBatch<TItem>(this IEnumerable<TItem> source, int size)
        {
            using (IEnumerator<TItem> enumerator = source.GetEnumerator())
                while (enumerator.MoveNext())
                    yield return Take(enumerator, size);
        }

        private static IEnumerable<TItem> Take<TItem>(IEnumerator<TItem> source, int size)
        {
            int i = 0;
            do
                yield return source.Current;
            while (++i < size && source.MoveNext());
        }
    }
}
