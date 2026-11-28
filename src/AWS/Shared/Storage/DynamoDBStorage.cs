using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime.CredentialManagement;

#if CLUSTERING_DYNAMODB
namespace Orleans.Clustering.DynamoDB
#elif PERSISTENCE_DYNAMODB
namespace Orleans.Persistence.DynamoDB
#elif REMINDERS_DYNAMODB
namespace Orleans.Reminders.DynamoDB
#elif AWSUTILS_TESTS
namespace Orleans.AWSUtils.Tests
#elif TRANSACTIONS_DYNAMODB
namespace Orleans.Transactions.DynamoDB
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// AWS DynamoDB SDK 的包装类。
    /// </summary>
#if TRANSACTIONS_DYNAMODB
    public partial class DynamoDBStorage
#else
    internal partial class DynamoDBStorage
#endif
    {
        private readonly string? _accessKey;
        private readonly string? _token;
        private readonly string? _profileName;
        /// <summary>此 DynamoDB 表的私有访问密钥。</summary>
        protected string? secretKey;
        private readonly string _service;

        /// <summary>
        /// DynamoDB 预置容量表的默认读取容量单位数。
        /// </summary>
        public const int DefaultReadCapacityUnits = 10;

        /// <summary>
        /// DynamoDB 预置容量表的默认写入容量单位数。
        /// </summary>
        public const int DefaultWriteCapacityUnits = 5;
        private readonly ProvisionedThroughput? _provisionedThroughput;
        private readonly bool _createIfNotExists;
        private readonly bool _updateIfExists;
        private readonly bool _useProvisionedThroughput;
        private readonly ReadOnlyCollection<TableStatus> _updateTableValidTableStatuses = new ReadOnlyCollection<TableStatus>(new List<TableStatus>()
            {
                TableStatus.CREATING, TableStatus.UPDATING, TableStatus.ACTIVE
            });
        private AmazonDynamoDBClient _ddbClient;
        private readonly ILogger _logger;

        /// <summary>
        /// 创建 DynamoDBStorage 实例。
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="accessKey"></param>
        /// <param name="secretKey"></param>
        /// <param name="token"></param>
        /// <param name="profileName"></param>
        /// <param name="service"></param>
        /// <param name="readCapacityUnits"></param>
        /// <param name="writeCapacityUnits"></param>
        /// <param name="useProvisionedThroughput"></param>
        /// <param name="createIfNotExists"></param>
        /// <param name="updateIfExists"></param>
        public DynamoDBStorage(
            ILogger logger,
            string service,
            string? accessKey = "",
            string? secretKey = "",
            string? token = "",
            string? profileName = "",
            int readCapacityUnits = DefaultReadCapacityUnits,
            int writeCapacityUnits = DefaultWriteCapacityUnits,
            bool useProvisionedThroughput = true,
            bool createIfNotExists = true,
            bool updateIfExists = true)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            this._accessKey = accessKey;
            this.secretKey = secretKey;
            this._token = token;
            this._profileName = profileName;
            this._service = service;
            this._useProvisionedThroughput = useProvisionedThroughput;
            this._provisionedThroughput = this._useProvisionedThroughput
                ? new ProvisionedThroughput(readCapacityUnits, writeCapacityUnits)
                : null;
            this._createIfNotExists = createIfNotExists;
            this._updateIfExists = updateIfExists;
            _logger = logger;
            CreateClient();
        }

        /// <summary>
        /// 如果 DynamoDB 表不存在，则创建它。
        /// </summary>
        /// <param name="tableName">表名称。</param>
        /// <param name="keys">键定义。</param>
        /// <param name="attributes">键定义使用的属性。</param>
        /// <param name="secondaryIndexes">（可选）二级索引定义。</param>
        /// <param name="ttlAttributeName">（可选）指示条目 TTL 的属性名称；为 null 时不启用 TTL。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists",
            Justification = "Retains the shipped signature and AWS SDK List<KeySchemaElement>, List<AttributeDefinition>, and List<GlobalSecondaryIndex> request contracts.")]
        public async Task InitializeTable(string tableName, List<KeySchemaElement> keys, List<AttributeDefinition> attributes, List<GlobalSecondaryIndex>? secondaryIndexes = null, string? ttlAttributeName = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this._createIfNotExists && !this._updateIfExists)
            {
                LogInformationTableNotCreatedOrUpdated(_logger, tableName);
                return;
            }

            try
            {
                TableDescription? tableDescription = await GetTableDescription(tableName, cancellationToken);
                await (tableDescription == null
                    ? CreateTableAsync(tableName, keys, attributes, secondaryIndexes, ttlAttributeName, cancellationToken)
                    : UpdateTableAsync(tableDescription, attributes, secondaryIndexes, ttlAttributeName, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                LogErrorCouldNotInitializeTable(_logger, exc, tableName);
                throw;
            }
        }

        [MemberNotNull(nameof(_ddbClient))]
        private void CreateClient()
        {
            var isServiceUrl = Uri.TryCreate(this._service, UriKind.Absolute, out var serviceUri)
                && (serviceUri.Scheme == Uri.UriSchemeHttp || serviceUri.Scheme == Uri.UriSchemeHttps);
            var config = isServiceUrl
                ? new AmazonDynamoDBConfig { ServiceURL = this._service }
                : new AmazonDynamoDBConfig { RegionEndpoint = AWSUtils.GetRegionEndpoint(this._service) };
            var credentials = GetClientCredentials(serviceUri);

            this._ddbClient = credentials is null
                ? new AmazonDynamoDBClient(config)
                : new AmazonDynamoDBClient(credentials, config);
        }

        private AWSCredentials? GetClientCredentials(Uri? serviceUri)
        {
            var credentials = GetExplicitCredentials();
            if (credentials is null && serviceUri?.Scheme == Uri.UriSchemeHttp)
            {
                credentials = new BasicAWSCredentials("dummy", "dummyKey");
            }

            return credentials;
        }

        private AWSCredentials? GetExplicitCredentials()
        {
            if (!string.IsNullOrEmpty(this._accessKey)
                && !string.IsNullOrEmpty(this.secretKey)
                && !string.IsNullOrEmpty(this._token))
            {
                return new SessionAWSCredentials(this._accessKey, this.secretKey, this._token);
            }

            if (!string.IsNullOrEmpty(this._accessKey) && !string.IsNullOrEmpty(this.secretKey))
            {
                return new BasicAWSCredentials(this._accessKey, this.secretKey);
            }

            if (!string.IsNullOrEmpty(this._profileName))
            {
                var chain = new CredentialProfileStoreChain();
                if (chain.TryGetAWSCredentials(this._profileName, out var credentials))
                {
                    return credentials;
                }

                throw new InvalidOperationException(
                    $"AWS named profile '{this._profileName}' provided, but credentials could not be retrieved");
            }

            return null;
        }

        private async Task<TableDescription?> GetTableDescription(string tableName, CancellationToken cancellationToken = default)
        {
            try
            {
                var description = await _ddbClient.DescribeTableAsync(tableName, cancellationToken);
                if (description.Table != null)
                    return description.Table;
            }
            catch (ResourceNotFoundException)
            {
                return null;
            }
            return null;
        }

        private async ValueTask CreateTableAsync(string tableName, List<KeySchemaElement> keys, List<AttributeDefinition> attributes, List<GlobalSecondaryIndex>? secondaryIndexes = null, string? ttlAttributeName = null, CancellationToken cancellationToken = default)
        {
            if (!_createIfNotExists)
            {
                LogWarningTableNotCreated(_logger, tableName);
                return;
            }

            var request = new CreateTableRequest
            {
                TableName = tableName,
                AttributeDefinitions = attributes,
                KeySchema = keys,
                BillingMode = this._useProvisionedThroughput ? BillingMode.PROVISIONED : BillingMode.PAY_PER_REQUEST,
                ProvisionedThroughput = _provisionedThroughput
            };

            if (secondaryIndexes != null && secondaryIndexes.Count > 0)
            {
                if (this._useProvisionedThroughput)
                {
                    secondaryIndexes.ForEach(i =>
                    {
                        i.ProvisionedThroughput = _provisionedThroughput;
                    });
                }

                request.GlobalSecondaryIndexes = secondaryIndexes;
            }

            try
            {
                try
                {
                    await _ddbClient.CreateTableAsync(request, cancellationToken);
                }
                catch (ResourceInUseException)
                {
                    // 表已创建。
                }

                TableDescription tableDescription = await TableWaitOnStatusAsync(tableName, TableStatus.CREATING, TableStatus.ACTIVE, cancellationToken: cancellationToken);
                tableDescription = await TableUpdateTtlAsync(tableDescription, ttlAttributeName, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                LogErrorCouldNotCreateTable(_logger, exc, tableName);
                throw;
            }
        }

        private async ValueTask UpdateTableAsync(TableDescription tableDescription, List<AttributeDefinition> attributes, List<GlobalSecondaryIndex>? secondaryIndexes = null, string? ttlAttributeName = null, CancellationToken cancellationToken = default)
        {
            if (!this._updateIfExists)
            {
                LogWarningTableNotUpdated(_logger, tableDescription.TableName);
                return;
            }

            if (!_updateTableValidTableStatuses.Contains(tableDescription.TableStatus))
            {
                throw new InvalidOperationException($"Table {tableDescription.TableName} has a status of {tableDescription.TableStatus} and can't be updated automatically.");
            }

            if (tableDescription.TableStatus == TableStatus.CREATING
                || tableDescription.TableStatus == TableStatus.UPDATING)
            {
                tableDescription = await TableWaitOnStatusAsync(tableDescription.TableName, tableDescription.TableStatus, TableStatus.ACTIVE, cancellationToken: cancellationToken);
            }

            var request = new UpdateTableRequest
            {
                TableName = tableDescription.TableName,
                AttributeDefinitions = attributes,
                BillingMode = this._useProvisionedThroughput ? BillingMode.PROVISIONED : BillingMode.PAY_PER_REQUEST,
                ProvisionedThroughput = _provisionedThroughput,
                GlobalSecondaryIndexUpdates = this._useProvisionedThroughput
                    ? tableDescription.GlobalSecondaryIndexes?.Select(gsi => new GlobalSecondaryIndexUpdate
                    {
                        Update = new UpdateGlobalSecondaryIndexAction
                        {
                            IndexName = gsi.IndexName,
                            ProvisionedThroughput = _provisionedThroughput
                        }
                    }).ToList()
                    : null
            };

            try
            {
                if ((request.ProvisionedThroughput?.ReadCapacityUnits ?? 0) != tableDescription.ProvisionedThroughput?.ReadCapacityUnits        // PROVISIONED 模式下的读取吞吐容量变更。
                    || (request.ProvisionedThroughput?.WriteCapacityUnits ?? 0) != tableDescription.ProvisionedThroughput?.WriteCapacityUnits   // PROVISIONED 模式下的写入吞吐容量变更。
                    || (tableDescription.ProvisionedThroughput?.ReadCapacityUnits != 0 && tableDescription.ProvisionedThroughput?.WriteCapacityUnits != 0 && this._useProvisionedThroughput == false /* 从 PROVISIONED 切换为 PAY_PER_REQUEST */))
                {
                    await _ddbClient.UpdateTableAsync(request, cancellationToken);
                    tableDescription = await TableWaitOnStatusAsync(tableDescription.TableName, TableStatus.UPDATING, TableStatus.ACTIVE, cancellationToken: cancellationToken);
                }

                tableDescription = await TableUpdateTtlAsync(tableDescription, ttlAttributeName, cancellationToken);

                // 等待表的所有索引进入 ACTIVE 状态。
                // 同一时间只能有一个 GSI 处于 CREATING 状态。
                // 还需等待所有索引完成 UPDATING，因为在全部索引创建完成之前，表尚未准备好接受 Orleans 的查询。
                List<GlobalSecondaryIndexDescription>? globalSecondaryIndexes = tableDescription.GlobalSecondaryIndexes;
                if (globalSecondaryIndexes != null)
                {
                    foreach (var globalSecondaryIndex in globalSecondaryIndexes)
                    {
                        if (globalSecondaryIndex.IndexStatus == IndexStatus.CREATING
                            || globalSecondaryIndex.IndexStatus == IndexStatus.UPDATING)
                        {
                            tableDescription = await TableIndexWaitOnStatusAsync(tableDescription.TableName, globalSecondaryIndex.IndexName, globalSecondaryIndex.IndexStatus, IndexStatus.ACTIVE, cancellationToken: cancellationToken);
                        }
                    }
                }

                var existingGlobalSecondaryIndexes = tableDescription.GlobalSecondaryIndexes?.Select(globalSecondaryIndex => globalSecondaryIndex.IndexName).ToArray() ?? Array.Empty<string>();
                var secondaryIndexesToCreate = (secondaryIndexes ?? Enumerable.Empty<GlobalSecondaryIndex>()).Where(secondaryIndex => !existingGlobalSecondaryIndexes.Contains(secondaryIndex.IndexName));

                foreach (var secondaryIndex in secondaryIndexesToCreate)
                {
                    await TableCreateSecondaryIndex(tableDescription.TableName, attributes, secondaryIndex, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                LogErrorCouldNotUpdateTable(_logger, exc, tableDescription.TableName);
                throw;
            }
        }

        private async Task TableCreateSecondaryIndex(string tableName, List<AttributeDefinition> attributes, GlobalSecondaryIndex secondaryIndex, CancellationToken cancellationToken)
        {
            await _ddbClient.UpdateTableAsync(new UpdateTableRequest
            {
                TableName = tableName,
                GlobalSecondaryIndexUpdates = new List<GlobalSecondaryIndexUpdate>
                {
                    new GlobalSecondaryIndexUpdate
                    {
                        Create = new CreateGlobalSecondaryIndexAction()
                        {
                            IndexName = secondaryIndex.IndexName,
                            Projection = secondaryIndex.Projection,
                            ProvisionedThroughput = _provisionedThroughput,
                            KeySchema = secondaryIndex.KeySchema
                        }
                    }
                },
                AttributeDefinitions = attributes
            }, cancellationToken);

            // 向表添加 GSI 是最终一致的操作。如果在更新表的调用后立即查询表状态，可能错过表的 UPDATING 状态。
            // 创建 GSI 所需时间远超 1 秒，因此此延迟不会增加本方法的总耗时。
            await Task.Delay(1000, cancellationToken);

            // 添加 GSI 时，表会短暂进入 UPDATING 状态；GSI 创建过程通常需要更长时间。
            // 因此，必须等待表和索引均进入 ACTIVE 状态，才能将操作标记为完成。
            await TableWaitOnStatusAsync(tableName, TableStatus.UPDATING, TableStatus.ACTIVE, cancellationToken: cancellationToken);
            await TableIndexWaitOnStatusAsync(tableName, secondaryIndex.IndexName, IndexStatus.CREATING, IndexStatus.ACTIVE, cancellationToken: cancellationToken);
        }

        private async ValueTask<TableDescription> TableUpdateTtlAsync(TableDescription tableDescription, string? ttlAttributeName, CancellationToken cancellationToken)
        {
            var describeTimeToLive = (await _ddbClient.DescribeTimeToLiveAsync(tableDescription.TableName, cancellationToken)).TimeToLiveDescription;

            // 仅支持将表 TTL 从 DISABLED 更新为 ENABLED。
            // 这是因为更新 TTL 属性需要先禁用表 TTL，再使用新 TTL 属性重新启用它。
            // 根据该 API 的详细说明：变更完全生效最多可能需要一小时；此期间对同一表再次调用 UpdateTimeToLive 会导致 ValidationException。
            // https://docs.aws.amazon.com/amazondynamodb/latest/APIReference/API_UpdateTimeToLive.html
            // 但若 TTL 已设置在正确的属性上，每次写入记录时该属性值都会更新，因此配置已正确，无需记录警告。
            if (describeTimeToLive.TimeToLiveStatus != TimeToLiveStatus.DISABLED && describeTimeToLive.AttributeName != ttlAttributeName)
            {
                LogWarningTtlNotDisabled(_logger, tableDescription.TableName);
                return tableDescription;
            }

            if (string.IsNullOrEmpty(ttlAttributeName) || describeTimeToLive.AttributeName == ttlAttributeName)
            {
                return tableDescription;
            }

            try
            {
                await _ddbClient.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
                {
                    TableName = tableDescription.TableName,
                    TimeToLiveSpecification = new TimeToLiveSpecification { AttributeName = ttlAttributeName, Enabled = true }
                }, cancellationToken);

                return await TableWaitOnStatusAsync(tableDescription.TableName, TableStatus.UPDATING, TableStatus.ACTIVE, cancellationToken: cancellationToken);
            }
            catch (AmazonDynamoDBException ddbEx)
            {
                // 必须吞掉此异常，因为调用 UpdateTimeToLive(Async) 前没有公开 API 可判断是否会发生以下问题：
                // "Time to live has been modified multiple times within a fixed interval".
                // 如果目标表的 TTL 功能最近被禁用，就可能遇到这种情况。
                LogErrorUpdateTtlException(_logger, ddbEx, tableDescription.TableName, ttlAttributeName);
                return tableDescription;
            }
        }

        private async Task<TableDescription> TableWaitOnStatusAsync(string tableName, TableStatus whileStatus, TableStatus desiredStatus, int delay = 2000, CancellationToken cancellationToken = default)
        {
            TableDescription ret = null!;

            do
            {
                if (ret != null)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                ret = (await GetTableDescription(tableName, cancellationToken))!;
            } while (ret.TableStatus == whileStatus);

            if (ret.TableStatus != desiredStatus)
            {
                throw new InvalidOperationException($"Table {tableName} has failed to reach the desired status of {desiredStatus}");
            }

            return ret;
        }

        private async Task<TableDescription> TableIndexWaitOnStatusAsync(string tableName, string indexName, IndexStatus whileStatus, IndexStatus? desiredStatus = null, int delay = 2000, CancellationToken cancellationToken = default)
        {
            TableDescription ret;
            GlobalSecondaryIndexDescription? index = null;

            do
            {
                if (index != null)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                ret = (await GetTableDescription(tableName, cancellationToken))!;
                index = ret.GlobalSecondaryIndexes?.Find(index => index.IndexName == indexName);
            } while (index != null && index.IndexStatus == whileStatus);

            if (desiredStatus != null && (index == null || index.IndexStatus != desiredStatus))
            {
                throw new InvalidOperationException($"Index {indexName} in table {tableName} has failed to reach the desired status of {desiredStatus}");
            }

            return ret;
        }

        /// <summary>
        /// 从 DynamoDB 删除表。
        /// </summary>
        /// <param name="tableName">要删除的表名称。</param>
        /// <returns></returns>
        public Task DeleTableAsync(string tableName)
        {
            try
            {
                return _ddbClient.DeleteTableAsync(new DeleteTableRequest { TableName = tableName });
            }
            catch (Exception exc)
            {
                LogErrorCouldNotDeleteTable(_logger, exc, tableName);
                throw;
            }
        }

        /// <summary>
        /// 在 DynamoDB 表中创建或替换条目。
        /// </summary>
        /// <param name="tableName">要写入条目的表名称。</param>
        /// <param name="fields">要在表中添加或替换的字段或属性。</param>
        /// <param name="conditionExpression">可选的条件表达式。</param>
        /// <param name="conditionValues">条件表达式使用的可选字段或属性值。</param>
        /// <returns></returns>
        public Task PutEntryAsync(string tableName, Dictionary<string, AttributeValue> fields, string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null)
            => PutEntryAsync(tableName, fields, CancellationToken.None, conditionExpression, conditionValues);

        /// <summary>使用指定的取消令牌创建或替换条目。</summary>
        /// <param name="tableName">表名称。</param>
        /// <param name="fields">要写入的字段。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="conditionExpression">可选的条件表达式。</param>
        /// <param name="conditionValues">条件表达式使用的值。</param>
        public Task PutEntryAsync(string tableName, Dictionary<string, AttributeValue> fields, CancellationToken cancellationToken, string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceCreatingTableEntry(_logger, tableName, new(fields));

            try
            {
                var request = new PutItemRequest(tableName, fields, ReturnValue.NONE);
                if (!string.IsNullOrWhiteSpace(conditionExpression))
                    request.ConditionExpression = conditionExpression;

                if (conditionValues != null && conditionValues.Keys.Count > 0)
                    request.ExpressionAttributeValues = conditionValues;

                return _ddbClient.PutItemAsync(request, cancellationToken);
            }
            catch (Exception exc)
            {
                LogErrorUnableToCreateItem(_logger, exc, tableName);
                throw;
            }
        }

        /// <summary>
        /// 在 DynamoDB 表中创建或更新条目。
        /// </summary>
        /// <param name="tableName">要插入或更新条目的表名称。</param>
        /// <param name="keys">此表条目的键。</param>
        /// <param name="fields">要在表中添加或更新的字段或属性。</param>
        /// <param name="conditionExpression">可选的条件表达式。</param>
        /// <param name="conditionValues">条件表达式使用的可选字段或属性值。</param>
        /// <param name="extraExpression">附加到插入或更新表达式末尾的额外表达式。</param>
        /// <param name="extraExpressionValues">extraExpression 使用的额外字段或属性。</param>
        /// <remarks>字段字典中的值将更新为 DynamoDB 返回的值。</remarks>
        /// <returns></returns>
        public Task UpsertEntryAsync(string tableName, Dictionary<string, AttributeValue> keys, Dictionary<string, AttributeValue> fields,
            string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null, string extraExpression = "",
            Dictionary<string, AttributeValue>? extraExpressionValues = null)
            => UpsertEntryAsync(tableName, keys, fields, CancellationToken.None, conditionExpression, conditionValues, extraExpression, extraExpressionValues);

        /// <summary>使用指定的取消令牌创建或更新条目。</summary>
        /// <param name="tableName">表名称。</param>
        /// <param name="keys">条目的键。</param>
        /// <param name="fields">要写入的字段，其值会更新为 DynamoDB 返回的值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="conditionExpression">可选的条件表达式。</param>
        /// <param name="conditionValues">条件表达式使用的值。</param>
        /// <param name="extraExpression">额外的更新表达式。</param>
        /// <param name="extraExpressionValues">额外表达式使用的值。</param>
        public async Task UpsertEntryAsync(string tableName, Dictionary<string, AttributeValue> keys, Dictionary<string, AttributeValue> fields,
            CancellationToken cancellationToken, string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null,
            string extraExpression = "", Dictionary<string, AttributeValue>? extraExpressionValues = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceUpsertingEntry(_logger, new(fields), new(keys), tableName);

            try
            {
                var request = new UpdateItemRequest
                {
                    TableName = tableName,
                    Key = keys,
                    ReturnValues = ReturnValue.UPDATED_NEW
                };

                (request.UpdateExpression, request.ExpressionAttributeValues) = ConvertUpdate(fields, conditionValues,
                    extraExpression, extraExpressionValues);

                if (!string.IsNullOrWhiteSpace(conditionExpression))
                    request.ConditionExpression = conditionExpression;

                var result = await _ddbClient.UpdateItemAsync(request, cancellationToken);

                foreach (var key in result.Attributes.Keys)
                {
                    if (fields.ContainsKey(key))
                    {
                        fields[key] = result.Attributes[key];
                    }
                    else
                    {
                        fields.Add(key, result.Attributes[key]);
                    }
                }
            }
            catch (Exception exc)
            {
                LogWarningIntermediateUpsert(_logger, exc, tableName);
                throw;
            }
        }

        /// <summary>
        /// 创建 DynamoDB 更新表达式及其表达式属性值。
        /// </summary>
        /// <param name="fields">要在更新表达式中赋值的属性。</param>
        /// <param name="conditionValues">条件表达式引用的值。</param>
        /// <param name="extraExpression">附加到属性赋值之后的额外更新表达式。</param>
        /// <param name="extraExpressionValues">由 <paramref name="extraExpression"/> 引用的值。</param>
        /// <returns>更新表达式，以及更新表达式和条件表达式使用的全部表达式属性值。</returns>
        public (string updateExpression, Dictionary<string, AttributeValue> expressionAttributeValues)
            ConvertUpdate(Dictionary<string, AttributeValue> fields,
                Dictionary<string, AttributeValue>? conditionValues = null,
                string extraExpression = "", Dictionary<string, AttributeValue>? extraExpressionValues = null)
        {
            ArgumentNullException.ThrowIfNull(fields);
            if (fields.Count == 0)
            {
                throw new ArgumentException("At least one field is required.", nameof(fields));
            }

            var expressionAttributeValues = new Dictionary<string, AttributeValue>();

            var updateExpression = new StringBuilder();
            foreach (var field in fields.Keys)
            {
                var valueKey = ":" + field;
                expressionAttributeValues.Add(valueKey, fields[field]);
                updateExpression.Append($" {field} = {valueKey},");
            }
            updateExpression.Insert(0, "SET");

            if (string.IsNullOrWhiteSpace(extraExpression))
            {
                updateExpression.Remove(updateExpression.Length - 1, 1);
            }
            else
            {
                updateExpression.Append($" {extraExpression}");
                if (extraExpressionValues != null && extraExpressionValues.Count > 0)
                {
                    foreach (var key in extraExpressionValues.Keys)
                    {
                        expressionAttributeValues.Add(key, extraExpressionValues[key]);
                    }
                }
            }

            if (conditionValues != null && conditionValues.Keys.Count > 0)
            {
                foreach (var item in conditionValues)
                {
                    expressionAttributeValues.Add(item.Key, item.Value);
                }
            }

            return (updateExpression.ToString(), expressionAttributeValues);
        }

        /// <summary>
        /// 从 DynamoDB 表中删除条目。
        /// </summary>
        /// <param name="tableName">要删除条目的表名称。</param>
        /// <param name="keys">要删除的表条目的键。</param>
        /// <param name="conditionExpression">可选的条件表达式。</param>
        /// <param name="conditionValues">条件表达式使用的可选字段或属性值。</param>
        /// <returns></returns>
        public Task DeleteEntryAsync(string tableName, Dictionary<string, AttributeValue> keys, string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null)
            => DeleteEntryAsync(tableName, keys, CancellationToken.None, conditionExpression, conditionValues);

        internal Task DeleteEntryAsync(string tableName, Dictionary<string, AttributeValue> keys, CancellationToken cancellationToken,
            string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceDeletingTableEntry(_logger, tableName, new(keys));

            try
            {
                var request = new DeleteItemRequest
                {
                    TableName = tableName,
                    Key = keys
                };

                if (!string.IsNullOrWhiteSpace(conditionExpression))
                    request.ConditionExpression = conditionExpression;

                if (conditionValues != null && conditionValues.Keys.Count > 0)
                    request.ExpressionAttributeValues = conditionValues;

                return _ddbClient.DeleteItemAsync(request, cancellationToken);
            }
            catch (Exception exc)
            {
                LogWarningIntermediateDelete(_logger, exc, tableName);
                throw;
            }
        }

        /// <summary>
        /// 从 DynamoDB 表中删除多个条目（批量删除）。
        /// </summary>
        /// <param name="tableName">要删除条目的表名称。</param>
        /// <param name="toDelete">批次中每个待删除条目的键值列表。</param>
        /// <returns></returns>
        public Task DeleteEntriesAsync(string tableName, IReadOnlyCollection<Dictionary<string, AttributeValue>> toDelete)
            => DeleteEntriesAsync(tableName, toDelete, CancellationToken.None);

        /// <summary>使用指定的取消令牌批量删除条目。</summary>
        /// <param name="tableName">表名称。</param>
        /// <param name="toDelete">待删除条目的键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task DeleteEntriesAsync(string tableName, IReadOnlyCollection<Dictionary<string, AttributeValue>> toDelete, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogTraceDeletingTableEntries(_logger, tableName);

            if (toDelete == null) throw new ArgumentNullException(nameof(toDelete));

            if (toDelete.Count == 0)
                return;

            try
            {
                var request = new BatchWriteItemRequest();
                request.RequestItems = new Dictionary<string, List<WriteRequest>>();
                var batch = new List<WriteRequest>();

                foreach (var keys in toDelete)
                {
                    var writeRequest = new WriteRequest();
                    writeRequest.DeleteRequest = new DeleteRequest();
                    writeRequest.DeleteRequest.Key = keys;
                    batch.Add(writeRequest);
                }
                request.RequestItems.Add(tableName, batch);
                var response = await _ddbClient.BatchWriteItemAsync(request, cancellationToken);
                var unprocessedCount = response.UnprocessedItems?.Values.Sum(items => items.Count) ?? 0;
                if (unprocessedCount > 0)
                {
                    throw new InvalidOperationException($"Amazon DynamoDB failed to delete {unprocessedCount} item(s) from table {tableName}.");
                }
            }
            catch (Exception exc)
            {
                LogWarningIntermediateDeleteEntries(_logger, exc, tableName);
                throw;
            }
        }

        /// <summary>
        /// 从 DynamoDB 表中读取条目。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">要查找条目的表名称。</param>
        /// <param name="keys">要查找的表条目键。</param>
        /// <param name="resolver">将返回的字段转换为具体类型的函数。仅当结果不为 null 时调用此函数。</param>
        /// <returns>由解析函数转换得到的对象。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 <see langword="null"/>。</exception>
        public Task<TResult?> ReadSingleEntryAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys, Func<Dictionary<string, AttributeValue>, TResult> resolver) where TResult : class
            => ReadSingleEntryAsync(tableName, keys, resolver, CancellationToken.None);

        /// <summary>使用指定的取消令牌读取条目。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表名称。</param>
        /// <param name="keys">条目的键。</param>
        /// <param name="resolver">将返回的字段转换为结果。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 <see langword="null"/>。</exception>
        public async Task<TResult?> ReadSingleEntryAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys, Func<Dictionary<string, AttributeValue>, TResult> resolver, CancellationToken cancellationToken) where TResult : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(resolver);

            try
            {
                var request = new GetItemRequest
                {
                    TableName = tableName,
                    Key = keys,
                    ConsistentRead = true
                };

                var response = await _ddbClient.GetItemAsync(request, cancellationToken);

                if (response.IsItemSet)
                {
                    return resolver(response.Item);
                }
                else
                {
                    return null;
                }
            }
            catch (Exception)
            {
                LogDebugUnableToFindTableEntry(_logger, new(keys));
                throw;
            }
        }

        /// <summary>
        /// 通过筛选键查询 DynamoDB 表中的多个条目。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">要查找条目的表名称。</param>
        /// <param name="keys">要查找的表条目键。</param>
        /// <param name="keyConditionExpression">用于筛选键的表达式。</param>
        /// <param name="resolver">将返回字段转换为具体类型的函数。仅在结果不为 null 时调用；每个匹配查询并加入结果列表的条目都会调用此函数。</param>
        /// <param name="indexName">keyConditionExpression 使用二级索引时指定该索引。</param>
        /// <param name="scanIndexForward">使用索引时，指示查找顺序为升序（true）还是降序（false）。</param>
        /// <param name="lastEvaluatedKey">此操作将评估的首个条目的主键。使用上一次操作返回的 LastEvaluatedKey。</param>
        /// <param name="consistentRead">决定读取一致性模型。使用 GSI 时此值必须为 false。</param>
        /// <returns>包含解析函数转换所得对象列表及分页所需 LastEvaluatedKey 的集合。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 <see langword="null"/>。</exception>
        public Task<(List<TResult> results, Dictionary<string, AttributeValue>? lastEvaluatedKey)> QueryAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys, string keyConditionExpression, Func<Dictionary<string, AttributeValue>, TResult> resolver, string indexName = "", bool scanIndexForward = true, Dictionary<string, AttributeValue>? lastEvaluatedKey = null, bool consistentRead = true) where TResult : class
            => QueryAsync(tableName, keys, keyConditionExpression, resolver, CancellationToken.None, indexName, scanIndexForward, lastEvaluatedKey, consistentRead);

        /// <summary>使用指定的取消令牌查询一页条目。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表名称。</param>
        /// <param name="keys">键条件使用的值。</param>
        /// <param name="keyConditionExpression">用于筛选键的表达式。</param>
        /// <param name="resolver">将每个条目转换为结果类型。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="indexName">可选的二级索引名称。</param>
        /// <param name="scanIndexForward">是否按升序读取索引。</param>
        /// <param name="lastEvaluatedKey">上一页的续页键。</param>
        /// <param name="consistentRead">是否要求强一致性读取。</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 <see langword="null"/>。</exception>
        public async Task<(List<TResult> results, Dictionary<string, AttributeValue>? lastEvaluatedKey)> QueryAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys, string keyConditionExpression, Func<Dictionary<string, AttributeValue>, TResult> resolver, CancellationToken cancellationToken, string indexName = "", bool scanIndexForward = true, Dictionary<string, AttributeValue>? lastEvaluatedKey = null, bool consistentRead = true) where TResult : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(resolver);

            try
            {
                var request = new QueryRequest
                {
                    TableName = tableName,
                    ExpressionAttributeValues = keys,
                    ConsistentRead = consistentRead,
                    KeyConditionExpression = keyConditionExpression,
                    Select = Select.ALL_ATTRIBUTES
                };

                if (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0)
                {
                    request.ExclusiveStartKey = lastEvaluatedKey;
                }

                if (!string.IsNullOrWhiteSpace(indexName))
                {
                    request.ScanIndexForward = scanIndexForward;
                    request.IndexName = indexName;
                }

                var response = await _ddbClient.QueryAsync(request, cancellationToken);

                var resultList = new List<TResult>();
                foreach (var item in response.Items)
                {
                    resultList.Add(resolver(item));
                }
                return (resultList, response.LastEvaluatedKey);
            }
            catch (Exception)
            {
                LogDebugUnableToFindTableEntry(_logger, new(keys));
                throw;
            }
        }

        /// <summary>
        /// 通过筛选键查询 DynamoDB 表中的多个条目。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">要查找条目的表名称。</param>
        /// <param name="keys">要查找的表条目键。</param>
        /// <param name="keyConditionExpression">用于筛选键的表达式。</param>
        /// <param name="resolver">将返回字段转换为具体类型的函数。仅在结果不为 null 时调用；每个匹配查询并加入结果列表的条目都会调用此函数。</param>
        /// <param name="indexName">keyConditionExpression 使用二级索引时指定该索引。</param>
        /// <param name="scanIndexForward">使用索引时，指示查找顺序为升序（true）还是降序（false）。</param>
        /// <param name="consistentRead">决定读取一致性模型。使用 GSI 时此值必须为 false。</param>
        /// <returns>包含解析函数转换所得对象列表的集合。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 <see langword="null"/>。</exception>
        public Task<List<TResult>> QueryAllAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys,
                string keyConditionExpression, Func<Dictionary<string, AttributeValue>, TResult> resolver,
                string indexName = "", bool scanIndexForward = true, bool consistentRead = true) where TResult : class
            => QueryAllAsync(tableName, keys, keyConditionExpression, resolver, CancellationToken.None, indexName, scanIndexForward, consistentRead);

        /// <summary>使用指定的取消令牌查询所有匹配条目。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表名称。</param>
        /// <param name="keys">键条件使用的值。</param>
        /// <param name="keyConditionExpression">用于筛选键的表达式。</param>
        /// <param name="resolver">将每个条目转换为结果类型。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="indexName">可选的二级索引名称。</param>
        /// <param name="scanIndexForward">是否按升序读取索引。</param>
        /// <param name="consistentRead">是否要求强一致性读取。</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 <see langword="null"/>。</exception>
        public async Task<List<TResult>> QueryAllAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys,
                string keyConditionExpression, Func<Dictionary<string, AttributeValue>, TResult> resolver,
                CancellationToken cancellationToken, string indexName = "", bool scanIndexForward = true, bool consistentRead = true) where TResult : class
        {
            List<TResult>? resultList = null;
            Dictionary<string, AttributeValue>? lastEvaluatedKey = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<TResult> results;
                (results, lastEvaluatedKey) = await QueryAsync(tableName, keys, keyConditionExpression, resolver,
                    cancellationToken, indexName, scanIndexForward, lastEvaluatedKey, consistentRead);
                if (resultList == null)
                {
                    resultList = results;
                }
                else
                {
                    resultList.AddRange(results);
                }
            } while (lastEvaluatedKey != null && lastEvaluatedKey.Count != 0);

            return resultList!;
        }

        /// <summary>
        /// 通过查询条目字段扫描 DynamoDB 表。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">要查找条目的表名称。</param>
        /// <param name="attributes">表达式使用的属性。</param>
        /// <param name="expression">筛选表达式。</param>
        /// <param name="resolver">将返回字段转换为具体类型的函数。仅在结果不为 null 时调用；每个匹配查询并加入结果列表的条目都会调用此函数。</param>
        /// <returns>包含解析函数转换所得对象列表的集合。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 为 <see langword="null"/>。</exception>
        public Task<List<TResult>> ScanAsync<TResult>(string tableName, Dictionary<string, AttributeValue> attributes, string expression, Func<Dictionary<string, AttributeValue>, TResult> resolver) where TResult : class =>
            ScanAsync(tableName, attributes, expression, resolver, CancellationToken.None);

        internal async Task<List<TResult>> ScanAsync<TResult>(string tableName, Dictionary<string, AttributeValue> attributes, string expression, Func<Dictionary<string, AttributeValue>, TResult> resolver, CancellationToken cancellationToken) where TResult : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(resolver);

            // 根据 Amazon 文档：
            // “单次 Scan 操作最多读取指定数量的条目
            // （使用 Limit 参数时）或最多 1 MB 的数据，然后
            // 使用 FilterExpression 对结果进行筛选。”
            // https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/DynamoDBv2/MDynamoDBScanAsyncStringDictionary!String,%20Condition!CancellationToken.html

            try
            {
                var resultList = new List<TResult>();

                Dictionary<string, AttributeValue>? exclusiveStartKey = null;

                while (true)
                {
                    var request = new ScanRequest
                    {
                        TableName = tableName,
                        ConsistentRead = true,
                        FilterExpression = expression,
                        ExpressionAttributeValues = attributes,
                        Select = Select.ALL_ATTRIBUTES
                    };

                    if (exclusiveStartKey is not null && exclusiveStartKey.Count > 0)
                    {
                        request.ExclusiveStartKey = exclusiveStartKey;
                    }

                    var response = await _ddbClient.ScanAsync(request, cancellationToken);

                    if (response.Items != null)
                    {
                        foreach (var item in response.Items)
                        {
                            resultList.Add(resolver(item));
                        }
                    }

                    if (response.LastEvaluatedKey == null || response.LastEvaluatedKey.Count == 0)
                    {
                        break;
                    }
                    else
                    {
                        exclusiveStartKey = response.LastEvaluatedKey;
                    }
                }

                return resultList;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                LogWarningFailedToReadTable(_logger, exc, tableName);
                throw new OrleansException($"Failed to read table {tableName}: {exc.Message}", exc);
            }
        }

        /// <summary>
        /// 在 DynamoDB 表中创建或替换多个条目（批量写入）。
        /// </summary>
        /// <param name="tableName">要查找条目的表名称。</param>
        /// <param name="toCreate">批次中每个待创建或替换条目的键值列表。</param>
        /// <returns></returns>
        public Task PutEntriesAsync(string tableName, IReadOnlyCollection<Dictionary<string, AttributeValue>> toCreate)
        {
            LogTracePutEntries(_logger, tableName);

            if (toCreate == null) throw new ArgumentNullException(nameof(toCreate));

            if (toCreate.Count == 0)
                return Task.CompletedTask;

            try
            {
                var request = new BatchWriteItemRequest();
                request.RequestItems = new Dictionary<string, List<WriteRequest>>();
                var batch = new List<WriteRequest>();

                foreach (var item in toCreate)
                {
                    var writeRequest = new WriteRequest();
                    writeRequest.PutRequest = new PutRequest();
                    writeRequest.PutRequest.Item = item;
                    batch.Add(writeRequest);
                }
                request.RequestItems.Add(tableName, batch);
                return _ddbClient.BatchWriteItemAsync(request);
            }
            catch (Exception exc)
            {
                LogWarningIntermediateBulkInsert(_logger, exc, tableName);
                throw;
            }
        }

        /// <summary>
        /// 以事务方式从 DynamoDB 表中读取条目。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">要查找条目的表名称。</param>
        /// <param name="keys">要查找的表条目键。</param>
        /// <param name="resolver">将返回的字段转换为具体类型的函数。仅当结果不为 null 时调用此函数。</param>
        /// <returns>由解析函数转换得到的对象。</returns>
        public Task<IEnumerable<TResult>> GetEntriesTxAsync<TResult>(string tableName, IEnumerable<Dictionary<string, AttributeValue>> keys, Func<Dictionary<string, AttributeValue>, TResult> resolver) where TResult : class
            => GetEntriesTxAsync(tableName, keys, resolver, CancellationToken.None);

        /// <summary>使用指定的取消令牌以事务方式读取条目。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表名称。</param>
        /// <param name="keys">待读取条目的键。</param>
        /// <param name="resolver">将每个条目转换为结果类型。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task<IEnumerable<TResult>> GetEntriesTxAsync<TResult>(string tableName, IEnumerable<Dictionary<string, AttributeValue>> keys, Func<Dictionary<string, AttributeValue>, TResult> resolver, CancellationToken cancellationToken) where TResult : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var request = new TransactGetItemsRequest
                {
                    TransactItems = keys.Select(key => new TransactGetItem
                    {
                        Get = new Get
                        {
                            TableName = tableName,
                            Key = key
                        }
                    }).ToList()
                };

                var response = await _ddbClient.TransactGetItemsAsync(request, cancellationToken);

                return response.Responses.Where(r => r?.Item?.Count > 0).Select(r => resolver(r.Item!));
            }
            catch (Exception)
            {
                LogDebugUnableToFindTableEntries(_logger, new(keys));
                throw;
            }
        }

        /// <summary>
        /// 以事务方式执行写入请求。
        /// </summary>
        /// <param name="puts">要执行的写入操作。</param>
        /// <param name="updates">要执行的更新操作。</param>
        /// <param name="deletes">要执行的删除操作。</param>
        /// <param name="conditionChecks">要执行的条件检查。</param>
        /// <returns></returns>
        public Task WriteTxAsync(IEnumerable<Put>? puts = null, IEnumerable<Update>? updates = null, IEnumerable<Delete>? deletes = null, IEnumerable<ConditionCheck>? conditionChecks = null)
            => WriteTxAsync(CancellationToken.None, puts, updates, deletes, conditionChecks);

        /// <summary>使用指定的取消令牌以事务方式写入条目。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="puts">要写入的条目。</param>
        /// <param name="updates">要执行的更新操作。</param>
        /// <param name="deletes">要执行的删除操作。</param>
        /// <param name="conditionChecks">要检查的条件。</param>
        public Task WriteTxAsync(CancellationToken cancellationToken, IEnumerable<Put>? puts = null, IEnumerable<Update>? updates = null, IEnumerable<Delete>? deletes = null, IEnumerable<ConditionCheck>? conditionChecks = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var transactItems = new List<TransactWriteItem>();
                if (puts != null)
                {
                    transactItems.AddRange(puts.Select(p => new TransactWriteItem { Put = p }));
                }
                if (updates != null)
                {
                    transactItems.AddRange(updates.Select(u => new TransactWriteItem { Update = u }));
                }
                if (deletes != null)
                {
                    transactItems.AddRange(deletes.Select(d => new TransactWriteItem { Delete = d }));
                }
                if (conditionChecks != null)
                {
                    transactItems.AddRange(conditionChecks.Select(c => new TransactWriteItem { ConditionCheck = c }));
                }

                var request = new TransactWriteItemsRequest
                {
                    TransactItems = transactItems
                };

                return _ddbClient.TransactWriteItemsAsync(request, cancellationToken);
            }
            catch (Exception exc)
            {
                LogDebugUnableToWrite(_logger, exc);
                throw;
            }
        }

        /// <summary>
        /// 以事务方式执行写入请求。
        /// </summary>
        /// <param name="transactItems">要执行的事务写入条目。</param>
        /// <returns></returns>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists",
            Justification = "Retains the shipped List<TransactWriteItem> signature and passes the AWS SDK request collection unchanged.")]
        public Task WriteTxAsync(List<TransactWriteItem> transactItems)
        {
            try
            {
                var request = new TransactWriteItemsRequest
                {
                    TransactItems = transactItems
                };

                return _ddbClient.TransactWriteItemsAsync(request);
            }
            catch (Exception exc)
            {
                LogDebugUnableToWrite(_logger, exc);
                throw;
            }
        }

        private readonly struct DictionaryLogRecord(Dictionary<string, AttributeValue> dictionary)
        {
            public override string ToString() => Utils.DictionaryToString(dictionary);
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "The config values for 'createIfNotExists' and 'updateIfExists' are false. The table '{TableName}' will not be created or updated."
        )]
        private static partial void LogInformationTableNotCreatedOrUpdated(ILogger logger, string tableName);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Could not initialize connection to storage table {TableName}"
        )]
        private static partial void LogErrorCouldNotInitializeTable(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The config value 'createIfNotExists' is false. The table '{TableName}' does not exist and it will not get created."
        )]
        private static partial void LogWarningTableNotCreated(ILogger logger, string tableName);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Could not create table {TableName}"
        )]
        private static partial void LogErrorCouldNotCreateTable(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The config value 'updateIfExists' is false. The table structure for table '{TableName}' will not be updated."
        )]
        private static partial void LogWarningTableNotUpdated(ILogger logger, string tableName);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Could not update table {TableName}"
        )]
        private static partial void LogErrorCouldNotUpdateTable(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "TTL is not DISABLED. Cannot update table TTL for table {TableName}. Please update manually."
        )]
        private static partial void LogWarningTtlNotDisabled(ILogger logger, string tableName);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Exception occured while updating table {TableName} TTL attribute to {TtlAttributeName}. Please update manually."
        )]
        private static partial void LogErrorUpdateTtlException(ILogger logger, Exception exception, string tableName, string ttlAttributeName);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Could not delete table {TableName}"
        )]
        private static partial void LogErrorCouldNotDeleteTable(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Creating {TableName} table entry: {TableEntry}"
        )]
        private static partial void LogTraceCreatingTableEntry(ILogger logger, string tableName, DictionaryLogRecord tableEntry);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Unable to create item to table {TableName}"
        )]
        private static partial void LogErrorUnableToCreateItem(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Upserting entry {Entry} with key(s) {Keys} into table {TableName}"
        )]
        private static partial void LogTraceUpsertingEntry(ILogger logger, DictionaryLogRecord entry, DictionaryLogRecord keys, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Intermediate error upserting to the table {TableName}"
        )]
        private static partial void LogWarningIntermediateUpsert(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Deleting table {TableName} entry with key(s) {Keys}"
        )]
        private static partial void LogTraceDeletingTableEntry(ILogger logger, string tableName, DictionaryLogRecord keys);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Intermediate error deleting entry from the table {TableName}."
        )]
        private static partial void LogWarningIntermediateDelete(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Deleting {TableName} table entries"
        )]
        private static partial void LogTraceDeletingTableEntries(ILogger logger, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Intermediate error deleting entries from the table {TableName}."
        )]
        private static partial void LogWarningIntermediateDeleteEntries(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Put entries {TableName} table"
        )]
        private static partial void LogTracePutEntries(ILogger logger, string tableName);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Intermediate error bulk inserting entries to table {TableName}."
        )]
        private static partial void LogWarningIntermediateBulkInsert(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Unable to find table entry for Keys = {Keys}"
        )]
        private static partial void LogDebugUnableToFindTableEntry(ILogger logger, DictionaryLogRecord keys);

        private readonly struct DictionariesLogRecord(IEnumerable<Dictionary<string, AttributeValue>> keys)
        {
            public override string ToString() => Utils.EnumerableToString(keys, d => Utils.DictionaryToString(d));
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Unable to find table entry for Keys = {Keys}"
        )]
        private static partial void LogDebugUnableToFindTableEntries(ILogger logger, DictionariesLogRecord keys);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to read table {TableName}"
        )]
        private static partial void LogWarningFailedToReadTable(ILogger logger, Exception exception, string tableName);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Unable to write"
        )]
        private static partial void LogDebugUnableToWrite(ILogger logger, Exception exception);
    }
}
