using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Reminders.DynamoDB
{
    /// <summary>
    /// 使用 DynamoDB 作为底层存储的 IReminderTable 实现。
    /// </summary>
    internal sealed partial class DynamoDBReminderTable : IReminderTable
    {
        private const string GRAIN_REFERENCE_PROPERTY_NAME = "GrainReference";
        private const string REMINDER_NAME_PROPERTY_NAME = "ReminderName";
        private const string SERVICE_ID_PROPERTY_NAME = "ServiceId";
        private const string START_TIME_PROPERTY_NAME = "StartTime";
        private const string PERIOD_PROPERTY_NAME = "Period";
        private const string GRAIN_HASH_PROPERTY_NAME = "GrainHash";
        private const string REMINDER_ID_PROPERTY_NAME = "ReminderId";
        private const string ETAG_PROPERTY_NAME = "ETag";
        private const string CURRENT_ETAG_ALIAS = ":currentETag";
        private const string SERVICE_ID_GRAIN_HASH_INDEX = "ServiceIdIndex";
        private const string SERVICE_ID_GRAIN_REFERENCE_INDEX = "ServiceIdGrainReferenceIndex";

        private readonly ILogger logger;
        private readonly DynamoDBReminderStorageOptions options;
        private readonly string serviceId;

        private DynamoDBStorage storage = null!;

        /// <summary>初始化 <see cref="DynamoDBReminderTable"/> 类的新实例。</summary>
        /// <param name="loggerFactory">要使用的日志记录器工厂。</param>
        /// <param name="clusterOptions"></param>
        /// <param name="storageOptions"></param>
        public DynamoDBReminderTable(
            ILoggerFactory loggerFactory,
            IOptions<ClusterOptions> clusterOptions,
            IOptions<DynamoDBReminderStorageOptions> storageOptions)
        {
            this.logger = loggerFactory.CreateLogger<DynamoDBReminderTable>();
            this.serviceId = clusterOptions.Value.ServiceId;
            this.options = storageOptions.Value;
        }

        /// <summary>使用指定的全局配置和日志记录器初始化当前实例。</summary>
        public Task Init()
        {
            this.storage = new DynamoDBStorage(
                this.logger,
                this.options.Service,
                this.options.AccessKey,
                this.options.SecretKey,
                this.options.Token,
                this.options.ProfileName,
                this.options.ReadCapacityUnits,
                this.options.WriteCapacityUnits,
                this.options.UseProvisionedThroughput,
                this.options.CreateIfNotExists,
                this.options.UpdateIfExists);

            LogInformationInitializingDynamoDBRemindersTable(logger);

            var serviceIdGrainHashGlobalSecondaryIndex = new GlobalSecondaryIndex
            {
                IndexName = SERVICE_ID_GRAIN_HASH_INDEX,
                Projection = new Projection { ProjectionType = ProjectionType.ALL },
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement { AttributeName = SERVICE_ID_PROPERTY_NAME, KeyType = KeyType.HASH},
                    new KeySchemaElement { AttributeName = GRAIN_HASH_PROPERTY_NAME, KeyType = KeyType.RANGE }
                }
            };

            var serviceIdGrainReferenceGlobalSecondaryIndex = new GlobalSecondaryIndex
            {
                IndexName = SERVICE_ID_GRAIN_REFERENCE_INDEX,
                Projection = new Projection { ProjectionType = ProjectionType.ALL },
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement { AttributeName = SERVICE_ID_PROPERTY_NAME, KeyType = KeyType.HASH},
                    new KeySchemaElement { AttributeName = GRAIN_REFERENCE_PROPERTY_NAME, KeyType = KeyType.RANGE }
                }
            };

            return this.storage.InitializeTable(this.options.TableName,
                new List<KeySchemaElement>
                {
                    new KeySchemaElement { AttributeName = REMINDER_ID_PROPERTY_NAME, KeyType = KeyType.HASH },
                    new KeySchemaElement { AttributeName = GRAIN_HASH_PROPERTY_NAME, KeyType = KeyType.RANGE }
                },
                new List<AttributeDefinition>
                {
                    new AttributeDefinition { AttributeName = REMINDER_ID_PROPERTY_NAME, AttributeType = ScalarAttributeType.S },
                    new AttributeDefinition { AttributeName = GRAIN_HASH_PROPERTY_NAME, AttributeType = ScalarAttributeType.N },
                    new AttributeDefinition { AttributeName = SERVICE_ID_PROPERTY_NAME, AttributeType = ScalarAttributeType.S },
                    new AttributeDefinition { AttributeName = GRAIN_REFERENCE_PROPERTY_NAME, AttributeType = ScalarAttributeType.S }
                },
                new List<GlobalSecondaryIndex> { serviceIdGrainHashGlobalSecondaryIndex, serviceIdGrainReferenceGlobalSecondaryIndex });
        }

        /// <summary>
        /// 根据提醒器名称读取指定 Grain 引用的提醒器。
        /// 从提醒器表读取一条记录。
        /// </summary>
        /// <param name="grainId">用于定位记录的 Grain 引用。</param>
        /// <param name="reminderName">用于定位记录的提醒器名称。</param>
        /// <returns>成功读取记录时返回 ReminderTableData。</returns>
        public async Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
        {
            var reminderId = ConstructReminderId(this.serviceId, grainId, reminderName);

            var keys = new Dictionary<string, AttributeValue>
                {
                    { $"{REMINDER_ID_PROPERTY_NAME}", new AttributeValue(reminderId) },
                    { $"{GRAIN_HASH_PROPERTY_NAME}", new AttributeValue { N = grainId.GetUniformHashCode().ToString() } }
                };

            try
            {
                return await this.storage.ReadSingleEntryAsync(this.options.TableName, keys, this.Resolve).ConfigureAwait(false);
            }
            catch (Exception exc)
            {
                LogWarningReadReminderEntry(logger, exc, new(keys), this.options.TableName);
                throw;
            }
        }

        /// <summary>
        /// 从提醒器表读取一条记录。
        /// </summary>
        /// <param name="grainId">用于定位记录的 Grain 引用。</param>
        /// <returns>成功读取记录时返回 ReminderTableData。</returns>
        public async Task<ReminderTableData> ReadRows(GrainId grainId)
        {
            var expressionValues = new Dictionary<string, AttributeValue>
                {
                    { $":{SERVICE_ID_PROPERTY_NAME}", new AttributeValue(this.serviceId) },
                    { $":{GRAIN_REFERENCE_PROPERTY_NAME}", new AttributeValue(grainId.ToString()) }
                };

            try
            {
                var expression = $"{SERVICE_ID_PROPERTY_NAME} = :{SERVICE_ID_PROPERTY_NAME} AND {GRAIN_REFERENCE_PROPERTY_NAME} = :{GRAIN_REFERENCE_PROPERTY_NAME}";
                var records = await this.storage.QueryAllAsync(this.options.TableName, expressionValues, expression, this.Resolve, SERVICE_ID_GRAIN_REFERENCE_INDEX, consistentRead: false).ConfigureAwait(false);

                return new ReminderTableData(records);
            }
            catch (Exception exc)
            {
                LogWarningReadReminderEntries(logger, exc, new(expressionValues), this.options.TableName);
                throw;
            }
        }

        /// <summary>
        /// 读取指定哈希范围的提醒器表数据。
        /// </summary>
        /// <param name="begin"></param>
        /// <param name="end"></param>
        /// <returns>成功读取记录时返回 ReminderTableData。</returns>
        public async Task<ReminderTableData> ReadRows(uint begin, uint end)
        {
            Dictionary<string, AttributeValue>? expressionValues = null;

            try
            {
                string expression = string.Empty;
                List<ReminderEntry> records;

                if (begin < end)
                {
                    expressionValues = new Dictionary<string, AttributeValue>
                    {
                        { $":{SERVICE_ID_PROPERTY_NAME}", new AttributeValue(this.serviceId) },
                        { $":Begin{GRAIN_HASH_PROPERTY_NAME}", new AttributeValue { N = (begin + 1).ToString() } },
                        { $":End{GRAIN_HASH_PROPERTY_NAME}", new AttributeValue { N = end.ToString() } }
                    };
                    expression = $"{SERVICE_ID_PROPERTY_NAME} = :{SERVICE_ID_PROPERTY_NAME} AND {GRAIN_HASH_PROPERTY_NAME} BETWEEN :Begin{GRAIN_HASH_PROPERTY_NAME} AND :End{GRAIN_HASH_PROPERTY_NAME}";
                    records = await this.storage.QueryAllAsync(this.options.TableName, expressionValues, expression, this.Resolve, SERVICE_ID_GRAIN_HASH_INDEX, consistentRead: false).ConfigureAwait(false);
                }
                else
                {
                    expressionValues = new Dictionary<string, AttributeValue>
                    {
                        { $":{SERVICE_ID_PROPERTY_NAME}", new AttributeValue(this.serviceId) },
                        { $":End{GRAIN_HASH_PROPERTY_NAME}", new AttributeValue { N = end.ToString() } }
                    };
                    expression = $"{SERVICE_ID_PROPERTY_NAME} = :{SERVICE_ID_PROPERTY_NAME} AND {GRAIN_HASH_PROPERTY_NAME} <= :End{GRAIN_HASH_PROPERTY_NAME}";
                    records = await this.storage.QueryAllAsync(this.options.TableName, expressionValues, expression, this.Resolve, SERVICE_ID_GRAIN_HASH_INDEX, consistentRead: false).ConfigureAwait(false);

                    expressionValues = new Dictionary<string, AttributeValue>
                    {
                        { $":{SERVICE_ID_PROPERTY_NAME}", new AttributeValue(this.serviceId) },
                        { $":Begin{GRAIN_HASH_PROPERTY_NAME}", new AttributeValue { N = begin.ToString() } }
                    };
                    expression = $"{SERVICE_ID_PROPERTY_NAME} = :{SERVICE_ID_PROPERTY_NAME} AND {GRAIN_HASH_PROPERTY_NAME} > :Begin{GRAIN_HASH_PROPERTY_NAME}";
                    records.AddRange(await this.storage.QueryAllAsync(this.options.TableName, expressionValues, expression, this.Resolve, SERVICE_ID_GRAIN_HASH_INDEX, consistentRead: false).ConfigureAwait(false));

                }

                return new ReminderTableData(records);
            }
            catch (Exception exc)
            {
                LogWarningReadReminderEntryRange(logger, exc, new(expressionValues!), this.options.TableName);
                throw;
            }
        }

        private ReminderEntry Resolve(Dictionary<string, AttributeValue> item)
        {
            return new ReminderEntry
            {
                ETag = item[ETAG_PROPERTY_NAME].N,
                GrainId = GrainId.Parse(item[GRAIN_REFERENCE_PROPERTY_NAME].S),
                Period = TimeSpan.Parse(item[PERIOD_PROPERTY_NAME].S),
                ReminderName = item[REMINDER_NAME_PROPERTY_NAME].S,
                StartAt = DateTime.Parse(item[START_TIME_PROPERTY_NAME].S)
            };
        }

        /// <summary>
        /// 从提醒器表删除一条记录。
        /// </summary>
        /// <param name="grainId">用于定位记录的指定 Grain 引用。</param>
        /// <param name="reminderName">用于定位记录的提醒器名称。</param>
        /// <param name="eTag">ETag。</param>
        /// <returns>记录删除成功时返回 true。</returns>
        public async Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
        {
            var reminderId = ConstructReminderId(this.serviceId, grainId, reminderName);

            var keys = new Dictionary<string, AttributeValue>
                {
                    { $"{REMINDER_ID_PROPERTY_NAME}", new AttributeValue(reminderId) },
                    { $"{GRAIN_HASH_PROPERTY_NAME}", new AttributeValue { N = grainId.GetUniformHashCode().ToString() } }
                };

            try
            {
                var conditionalValues = new Dictionary<string, AttributeValue> { { CURRENT_ETAG_ALIAS, new AttributeValue { N = eTag } } };
                var expression = $"{ETAG_PROPERTY_NAME} = {CURRENT_ETAG_ALIAS}";

                await this.storage.DeleteEntryAsync(this.options.TableName, keys, expression, conditionalValues).ConfigureAwait(false);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }

        /// <summary>
        /// 用于清除提醒器表数据的测试钩子。
        /// </summary>
        /// <returns></returns>
        public async Task TestOnlyClearTable()
        {
            var expressionValues = new Dictionary<string, AttributeValue>
                {
                    { $":{SERVICE_ID_PROPERTY_NAME}", new AttributeValue(this.serviceId) }
                };

            try
            {
                var expression = $"{SERVICE_ID_PROPERTY_NAME} = :{SERVICE_ID_PROPERTY_NAME}";
                var records = await this.storage.ScanAsync(this.options.TableName, expressionValues, expression,
                    item => new Dictionary<string, AttributeValue>
                    {
                        { REMINDER_ID_PROPERTY_NAME, item[REMINDER_ID_PROPERTY_NAME] },
                        { GRAIN_HASH_PROPERTY_NAME, item[GRAIN_HASH_PROPERTY_NAME] }
                    }).ConfigureAwait(false);

                if (records.Count <= 25)
                {
                    await this.storage.DeleteEntriesAsync(this.options.TableName, records);
                }
                else
                {
                    List<Task> tasks = new List<Task>();
                    foreach (var batch in records.BatchIEnumerable(25))
                    {
                        tasks.Add(this.storage.DeleteEntriesAsync(this.options.TableName, batch));
                    }
                    await Task.WhenAll(tasks);
                }
            }
            catch (Exception exc)
            {
                LogWarningRemoveReminderEntries(logger, exc, new(expressionValues), this.options.TableName);
                throw;
            }
        }

        /// <summary>
        /// 将条目写入提醒器表的异步方法。
        /// </summary>
        /// <param name="entry">要写入的条目。</param>
        /// <returns>成功插入或更新条目时返回该条目的 ETag。</returns>
        public async Task<string?> UpsertRow(ReminderEntry entry)
        {
            var reminderId = ConstructReminderId(this.serviceId, entry.GrainId, entry.ReminderName);

            var fields = new Dictionary<string, AttributeValue>
                {
                    { REMINDER_ID_PROPERTY_NAME, new AttributeValue(reminderId) },
                    { GRAIN_HASH_PROPERTY_NAME, new AttributeValue { N = entry.GrainId.GetUniformHashCode().ToString() } },
                    { SERVICE_ID_PROPERTY_NAME, new AttributeValue(this.serviceId) },
                    { GRAIN_REFERENCE_PROPERTY_NAME, new AttributeValue( entry.GrainId.ToString()) },
                    { PERIOD_PROPERTY_NAME, new AttributeValue(entry.Period.ToString()) },
                    { START_TIME_PROPERTY_NAME, new AttributeValue(entry.StartAt.ToString()) },
                    { REMINDER_NAME_PROPERTY_NAME, new AttributeValue(entry.ReminderName) },
                    { ETAG_PROPERTY_NAME, new AttributeValue { N = Random.Shared.Next().ToString() } }
                };

            try
            {
                LogDebugUpsertRow(logger, entry, entry.ETag);

                await this.storage.PutEntryAsync(this.options.TableName, fields);

                entry.ETag = fields[ETAG_PROPERTY_NAME].N;
                return entry.ETag;
            }
            catch (Exception exc)
            {
                LogWarningUpdateReminderEntry(logger, exc, entry, options.TableName);
                throw;
            }
        }

        private static string ConstructReminderId(string serviceId, GrainId grainId, string reminderName) => $"{serviceId}_{grainId}_{reminderName}";

        [LoggerMessage(
            EventId = (int)ErrorCode.ReminderServiceBase,
            Level = LogLevel.Information,
            Message = "Initializing AWS DynamoDB Reminders Table"
        )]
        private static partial void LogInformationInitializingDynamoDBRemindersTable(ILogger logger);

        private readonly struct DictionaryLogRecord(Dictionary<string, AttributeValue> keys)
        {
            public override string ToString() => Utils.DictionaryToString(keys);
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.ReminderServiceBase,
            Level = LogLevel.Warning,
            Message = "Intermediate error reading reminder entry {Keys} from table {TableName}."
        )]
        private static partial void LogWarningReadReminderEntry(ILogger logger, Exception exception, DictionaryLogRecord keys, string tableName);

        [LoggerMessage(
            EventId = (int)ErrorCode.ReminderServiceBase,
            Level = LogLevel.Warning,
            Message = "Intermediate error reading reminder entry {Entries} from table {TableName}."
        )]
        private static partial void LogWarningReadReminderEntries(ILogger logger, Exception exception, DictionaryLogRecord entries, string tableName);

        [LoggerMessage(
            EventId = (int)ErrorCode.ReminderServiceBase,
            Level = LogLevel.Warning,
            Message = "Intermediate error reading reminder entry {ExpressionValues} from table {TableName}."
        )]
        private static partial void LogWarningReadReminderEntryRange(ILogger logger, Exception exception, DictionaryLogRecord expressionValues, string tableName);

        [LoggerMessage(
            EventId = (int)ErrorCode.ReminderServiceBase,
            Level = LogLevel.Warning,
            Message = "Intermediate error removing reminder entries {Entries} from table {TableName}."
        )]
        private static partial void LogWarningRemoveReminderEntries(ILogger logger, Exception exception, DictionaryLogRecord entries, string tableName);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "UpsertRow entry = {Entry}, etag = {ETag}"
        )]
        private static partial void LogDebugUpsertRow(ILogger logger, ReminderEntry entry, string? eTag);

        [LoggerMessage(
            EventId = (int)ErrorCode.ReminderServiceBase,
            Level = LogLevel.Warning,
            Message = "Intermediate error updating entry {Entry} to the table {TableName}."
        )]
        private static partial void LogWarningUpdateReminderEntry(ILogger logger, Exception exception, ReminderEntry entry, string tableName);
    }
}
