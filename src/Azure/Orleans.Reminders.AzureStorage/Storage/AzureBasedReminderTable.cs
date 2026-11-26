using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.AzureUtils.Utilities;
using Orleans.Configuration;
using Orleans.Reminders.AzureStorage;

namespace Orleans.Runtime.ReminderService
{
    /// <summary>
    /// 仓库和回收 Orleans 使用提醒条目 Azure 桌子储存。
    /// </summary>
    public sealed partial class AzureBasedReminderTable : IReminderTable
    {
        internal const string ServiceIdValidationExceptionDataKey = "AzureBasedReminderTable.ServiceIdValidationException";

        private readonly ILogger logger;
        private readonly ILoggerFactory loggerFactory;
        private readonly ClusterOptions clusterOptions;
        private readonly AzureTableReminderStorageOptions storageOptions;
        private readonly RemindersTableManager remTableManager;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private TaskCompletionSource _initializationTask = CreateInitializationSource();

        /// <summary>
        /// 初始化 <see cref="AzureBasedReminderTable"/> 类的新实例。
        /// </summary>
        /// <param name="loggerFactory">伐木厂</param>
        /// <param name="clusterOptions">用于范围提醒条目的群集识别选项。</param>
        /// <param name="storageOptions">下 Azure 表格存储提醒选项。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="clusterOptions"/> 或 <paramref name="storageOptions"/> 是 <see langword="null"/>。
        /// </exception>
        public AzureBasedReminderTable(
            ILoggerFactory loggerFactory,
            IOptions<ClusterOptions> clusterOptions,
            IOptions<AzureTableReminderStorageOptions> storageOptions)
        {
            this.logger = loggerFactory.CreateLogger<AzureBasedReminderTable>();
            this.loggerFactory = loggerFactory;
            ArgumentNullException.ThrowIfNull(clusterOptions);
            ArgumentNullException.ThrowIfNull(storageOptions);
            this.clusterOptions = clusterOptions.Value;
            this.storageOptions = storageOptions.Value;
            this.remTableManager = new RemindersTableManager(
                this.clusterOptions.ServiceId,
                this.clusterOptions.ClusterId,
                this.storageOptions,
                this.loggerFactory);
        }

        /// <summary>
        /// 连接到 Azure 表存储,如果不存在提醒表,则创建提醒表格。
        /// </summary>
        /// <param name="cancellationToken">用于取消生命周期锁定获取和启动尝试之间延迟的标记。</param>
        /// <returns>一项代表初始化操作的任务。</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                if (_initializationTask.Task.IsCompletedSuccessfully)
                {
                    return;
                }

                if (_initializationTask.Task.IsCompleted)
                {
                    Volatile.Write(ref _initializationTask, CreateInitializationSource());
                }

                var initialization = _initializationTask;
                while (true)
                {
                    try
                    {
                        await remTableManager.InitTableAsync(cancellationToken);
                        initialization.TrySetResult();
                        return;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        LogErrorCreatingAzureTable(ex);
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                LogErrorReminderTableInitializationCanceled(ex);
                _initializationTask.TrySetCanceled(ex.CancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                LogErrorInitializingReminderTable(ex);
                _initializationTask.TrySetException(ex);
                throw;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// 停止提醒表格,直到它再次启动。
        /// </summary>
        /// <param name="cancellationToken">用于取消停止操作的标记。</param>
        /// <returns>代表停止操作的任务。</returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                var stopped = CreateInitializationSource();
                stopped.TrySetCanceled(CancellationToken.None);
                Volatile.Write(ref _initializationTask, stopped);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private static TaskCompletionSource CreateInitializationSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ReminderTableData ConvertFromTableEntryList(List<(ReminderTableEntry Entity, string ETag)> entries)
        {
            var remEntries = new List<ReminderEntry>();
            foreach (var entry in entries)
            {
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception.
                try
                {
                    ReminderEntry converted = ConvertFromTableEntry(entry.Entity, entry.ETag);
                    remEntries.Add(converted);
                }
                catch (Exception)
                {
                    // 忽略...
                }
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception.
            }
            return new ReminderTableData(remEntries);
        }

        internal ReminderEntry ConvertFromTableEntry(ReminderTableEntry tableEntry, string eTag)
        {
            ReminderEntry converted;
            try
            {
                converted = new ReminderEntry
                {
                    GrainId = GrainId.Parse(tableEntry.GrainReference!),
                    ReminderName = tableEntry.ReminderName!,
                    StartAt = LogFormatter.ParseDate(tableEntry.StartAt!),
                    Period = TimeSpan.Parse(tableEntry.Period!),
                    ETag = eTag,
                };
            }
            catch (Exception exc)
            {
                LogErrorParsingReminderEntry(exc, tableEntry);
                try
                {
                    ValidateServiceId(tableEntry);
                }
                catch (OrleansException serviceIdException)
                {
                    exc.Data[ServiceIdValidationExceptionDataKey] = serviceIdException;
                }
                catch (NullReferenceException serviceIdException)
                {
                    exc.Data[ServiceIdValidationExceptionDataKey] = serviceIdException;
                }

                throw;
            }

            ValidateServiceId(tableEntry);
            return converted;
        }

        private void ValidateServiceId(ReminderTableEntry tableEntry)
        {
            string serviceIdStr = this.clusterOptions.ServiceId;
            if (!tableEntry.ServiceId!.Equals(serviceIdStr, StringComparison.Ordinal))
            {
                LogWarningAzureTable_ReadWrongReminder(tableEntry, serviceIdStr);
                throw new OrleansException($"Read a reminder entry for wrong Service id. Read {tableEntry}, but my service id is {serviceIdStr}. Going to discard it.");
            }
        }

        private static ReminderTableEntry ConvertToTableEntry(ReminderEntry remEntry, string serviceId, string deploymentId)
        {
            string partitionKey = ReminderTableEntry.ConstructPartitionKey(serviceId, remEntry.GrainId);
            string rowKey = ReminderTableEntry.ConstructRowKey(remEntry.GrainId, remEntry.ReminderName);

            var consistentHash = remEntry.GrainId.GetUniformHashCode();

            return new ReminderTableEntry
            {
                PartitionKey = partitionKey,
                RowKey = rowKey,

                ServiceId = serviceId,
                DeploymentId = deploymentId,
                GrainReference = remEntry.GrainId.ToString(),
                ReminderName = remEntry.ReminderName,

                StartAt = LogFormatter.PrintDate(remEntry.StartAt),
                Period = remEntry.Period.ToString(),

                GrainRefConsistentHash = consistentHash.ToString("X8"),
                // 下 Azure SDK 接受默认提醒 ETag, 即使其字符串构造器不可读。
                ETag = new ETag(remEntry.ETag!),
            };
        }

        /// <summary>
        /// 删除当前 Orleans 服务和组群的所有提醒项。
        /// </summary>
        /// <returns>代表删除操作的任务。</returns>
        public async Task TestOnlyClearTable()
        {
            await Volatile.Read(ref _initializationTask).Task;

            await this.remTableManager.DeleteTableEntries();
        }

        /// <summary>
        /// 读取与指定 grain有关的所有提醒条目。
        /// </summary>
        /// <param name="grainId"> grain 标识符。</param>
        /// <returns>与 <paramref name="grainId"/>.</returns>
        public async Task<ReminderTableData> ReadRows(GrainId grainId)
        {
            try
            {
                await Volatile.Read(ref _initializationTask).Task;

                var entries = await this.remTableManager.FindReminderEntries(grainId);
                ReminderTableData data = ConvertFromTableEntryList(entries);
                LogTraceReadForGrain(grainId, data);
                return data;
            }
            catch (Exception exc)
            {
                LogWarningReadingReminders(exc, grainId, this.remTableManager.TableName);
                throw;
            }
        }

        /// <summary>
        /// 读取提醒条目,其 grain 散列在范围 <c>(begin, end]</c>。
        /// </summary>
        /// <param name="begin">散列范围排他性下限</param>
        /// <param name="end">散列范围的包容性上层。</param>
        /// <returns>指定散列范围内的提醒条目。</returns>
        public async Task<ReminderTableData> ReadRows(uint begin, uint end)
        {
            try
            {
                await Volatile.Read(ref _initializationTask).Task;

                var entries = await this.remTableManager.FindReminderEntries(begin, end);
                ReminderTableData data = ConvertFromTableEntryList(entries);
                LogTraceReadInRange(new(begin, end), data);
                return data;
            }
            catch (Exception exc)
            {
                LogWarningReadingReminderRange(exc, new(begin, end), this.remTableManager.TableName);
                throw;
            }
        }

        /// <summary>
        /// 读取指定 grain 和提醒名称的提醒项。
        /// </summary>
        /// <param name="grainId"> grain 标识符。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <returns>发现提醒项时;否则, <see langword="null"/>.</returns>
        public async Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
        {
            try
            {
                await Volatile.Read(ref _initializationTask).Task;

                LogDebugReadRow(grainId, reminderName);
                var result = await this.remTableManager.FindReminderEntry(grainId, reminderName);
                return result.Entity is null ? null : ConvertFromTableEntry(result.Entity, result.ETag!);
            }
            catch (Exception exc)
            {
                LogWarningReadingReminderRow(exc, grainId, reminderName, this.remTableManager.TableName);
                throw;
            }
        }

        /// <summary>
        /// 插入或替换提醒条目。
        /// </summary>
        /// <param name="entry">备存提醒项。</param>
        /// <returns>写成功时的新实体标记;否则, <see langword="null"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> 是 <see langword="null"/>.</exception>
        public async Task<string?> UpsertRow(ReminderEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            try
            {
                await Volatile.Read(ref _initializationTask).Task;

                LogDebugUpsertRow(entry);
                ReminderTableEntry remTableEntry = ConvertToTableEntry(entry, this.clusterOptions.ServiceId, this.clusterOptions.ClusterId);

                string? result = await this.remTableManager.UpsertRow(remTableEntry);
                if (result == null)
                {
                    LogWarningReminderUpsertFailed(entry);
                }
                return result;
            }
            catch (Exception exc)
            {
                LogWarningUpsertReminderEntry(exc, entry, this.remTableManager.TableName);
                throw;
            }
        }

        /// <summary>
        /// 当实体标签与存储条目匹配时, 将删除提醒项 。
        /// </summary>
        /// <param name="grainId"> grain 标识符。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="eTag">用于乐观货币的实体标记。</param>
        /// <returns>
        /// 条目被删除时的<see langword="true"/> ;否则, <see langword="false"/>。
        /// </returns>
        public async Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
        {
            var entry = new ReminderTableEntry
            {
                PartitionKey = ReminderTableEntry.ConstructPartitionKey(this.clusterOptions.ServiceId, grainId),
                RowKey = ReminderTableEntry.ConstructRowKey(grainId, reminderName),
                ETag = new ETag(eTag),
            };

            try
            {
                await Volatile.Read(ref _initializationTask).Task;

                LogTraceRemoveRow(entry);

                bool result = await this.remTableManager.DeleteReminderEntryConditionally(entry, eTag);
                if (result == false)
                {
                    LogWarningOnReminderDeleteRetry(entry);
                }
                return result;
            }
            catch (Exception exc)
            {
                LogWarningWhenDeletingReminder(exc, entry, this.remTableManager.TableName);
                throw;
            }
        }

        private readonly struct RingRangeLogValue(uint Begin, uint End)
        {
            public override string? ToString() => RangeFactory.CreateRange(Begin, End).ToString();
        }

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)AzureReminderErrorCode.AzureTable_39,
            Message = "Exception trying to create or connect to the Azure table"
        )]
        private partial void LogErrorCreatingAzureTable(Exception ex);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Reminder table initialization canceled."
        )]
        private partial void LogErrorReminderTableInitializationCanceled(Exception ex);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error initializing reminder table."
        )]
        private partial void LogErrorInitializingReminderTable(Exception ex);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)AzureReminderErrorCode.AzureTable_49,
            Message = "Failed to parse ReminderTableEntry: {TableEntry}. This entry is corrupt, going to ignore it."
        )]
        private partial void LogErrorParsingReminderEntry(Exception ex, object tableEntry);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_ReadWrongReminder,
            Message = "Read a reminder entry for wrong Service id. Read {TableEntry}, but my service id is {ServiceId}. Going to discard it."
        )]
        private partial void LogWarningAzureTable_ReadWrongReminder(ReminderTableEntry tableEntry, string serviceId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Read for grain {GrainId} Table={Data}"
        )]
        private partial void LogTraceReadForGrain(GrainId grainId, ReminderTableData data);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_47,
            Message = "Intermediate error reading reminders for grain {GrainId} in table {TableName}."
        )]
        private partial void LogWarningReadingReminders(Exception ex, GrainId grainId, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Read in {RingRange} Table={Data}"
        )]
        private partial void LogTraceReadInRange(RingRangeLogValue ringRange, ReminderTableData data);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_40,
            Message = "Intermediate error reading reminders in range {RingRange} for table {TableName}."
        )]
        private partial void LogWarningReadingReminderRange(Exception ex, RingRangeLogValue ringRange, string tableName);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "ReadRow grainRef = {GrainId} reminderName = {ReminderName}"
        )]
        private partial void LogDebugReadRow(GrainId grainId, string reminderName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_46,
            Message = "Intermediate error reading row with grainId = {GrainId} reminderName = {ReminderName} from table {TableName}."
        )]
        private partial void LogWarningReadingReminderRow(Exception ex, GrainId grainId, string reminderName, string tableName);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "UpsertRow entry = {Data}"
        )]
        private partial void LogDebugUpsertRow(ReminderEntry data);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_45,
            Message = "Upsert failed on the reminder table. Will retry. Entry = {Data}"
        )]
        private partial void LogWarningReminderUpsertFailed(ReminderEntry data);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_42,
            Message = "Intermediate error upserting reminder entry {Data} to the table {TableName}."
        )]
        private partial void LogWarningUpsertReminderEntry(Exception ex, ReminderEntry data, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "RemoveRow entry = {Data}"
        )]
        private partial void LogTraceRemoveRow(ReminderTableEntry data);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_43,
            Message = "Delete failed on the reminder table. Will retry. Entry = {Data}"
        )]
        private partial void LogWarningOnReminderDeleteRetry(ReminderTableEntry data);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)AzureReminderErrorCode.AzureTable_44,
            Message = "Intermediate error when deleting reminder entry {Data} to the table {TableName}."
        )]
        private partial void LogWarningWhenDeletingReminder(Exception ex, ReminderTableEntry data, string tableName);
    }
}
