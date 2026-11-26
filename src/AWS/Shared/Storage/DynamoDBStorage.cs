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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 环绕AWS DynamoDB SDK。
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
        /// <summary> 此表 dynamoDB 的密钥 </summary>
        protected string? secretKey;
        private readonly string _service;

        /// <summary>
        /// 提供 DynamoDB 表格的默认读能力单位数。
        /// </summary>
        public const int DefaultReadCapacityUnits = 10;

        /// <summary>
        /// 提供 DynamoDB 表格的默认写能力单位数。
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
        /// 创建 DynamoDBStorage 实例
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
        /// 如果不存在 DynamoDB 表格, 创建该表
        /// </summary>
        /// <param name="tableName">表格的名称</param>
        /// <param name="keys">关键定义</param>
        /// <param name="attributes">关键定义</param>所用属性
        /// <param name="secondaryIndexes">(可选择)次级指数定义</param>
        /// <param name="ttlAttributeName">(可选择的)项目属性的名称,该属性表示项目TTL(如果无效,ttl将不启用)</param>
        /// <param name="cancellationToken">取消牌。</param>
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
                    // 表格已经创建 。
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
                if ((request.ProvisionedThroughput?.ReadCapacityUnits ?? 0) != tableDescription.ProvisionedThroughput?.ReadCapacityUnits        // 能力变化
                    || (request.ProvisionedThroughput?.WriteCapacityUnits ?? 0) != tableDescription.ProvisionedThroughput?.WriteCapacityUnits   // 设计投入书写能力变化
                    || (tableDescription.ProvisionedThroughput?.ReadCapacityUnits != 0 && tableDescription.ProvisionedThroughput?.WriteCapacityUnits != 0 && this._useProvisionedThroughput == false /* 至支付款 */))
                {
                    await _ddbClient.UpdateTableAsync(request, cancellationToken);
                    tableDescription = await TableWaitOnStatusAsync(tableDescription.TableName, TableStatus.UPDATING, TableStatus.ACTIVE, cancellationToken: cancellationToken);
                }

                tableDescription = await TableUpdateTtlAsync(tableDescription, ttlAttributeName, cancellationToken);

                // 等待所有表格指数成为主动。
                // 我们只能同时有一个在 侵蚀状态的GSI
                // 我们还等待所有索引完成更新,因为在所有索引建立之前,表格无法接收 Orleans 的查询。
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

            // 将GSI添加到表格中,最终是一种一致的操作,如果我们在表格更新电话之后即时查询表格状态,我们可能会错过表格更新状态。
            // 建立GSI需要的时间大大超过1秒,因此这一拖延不会增加这种方法总持续时间。
            await Task.Delay(1000, cancellationToken);

            // 在添加 GSI 时,表格简单将其状态改为更新。 GIS 创建过程通常需要更长的时间。
            // 因此,我们将等待表格和索引成为主动的指数,然后将行动标记为完整。
            await TableWaitOnStatusAsync(tableName, TableStatus.UPDATING, TableStatus.ACTIVE, cancellationToken: cancellationToken);
            await TableIndexWaitOnStatusAsync(tableName, secondaryIndex.IndexName, IndexStatus.CREATING, IndexStatus.ACTIVE, cancellationToken: cancellationToken);
        }

        private async ValueTask<TableDescription> TableUpdateTtlAsync(TableDescription tableDescription, string? ttlAttributeName, CancellationToken cancellationToken)
        {
            var describeTimeToLive = (await _ddbClient.DescribeTimeToLiveAsync(tableDescription.TableName, cancellationToken)).TimeToLiveDescription;

            // 我们只能处理从无法满足到适应的TTL更新。
            // 这是因为更新TTL属性需要(1) 使表TTL失效,(2) 用新的TTL属性重新启用。
            // 依此 API的以下详细页:“更改可以花一个小时才能完全处理。在此一小时期间,任何额外的 UpdateTimeToLive 要求在同一张表格中填写,结果产生 ValidationException”。
            // https://docs.aws.amazon.com/amazondynamodb/latest/APIReference/API_UpdateTimeToLive.html
            // 但是如果TTL已经设置在正确的属性上,则每次写入记录时都会更新属性值,因此配置已经正确,无需登录警告。
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
                // 我们需要吞下这一例外,因为没有 API 接触以确定在调用 UpdateTimeToLive(Async)之前是否将出现以下问题
                // "在固定间隔内,生命时间被多次修改"
                // 如果目标表格上最近有TTL功能被禁用,我们就可以得出这种情况。
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
        /// 删除 DynamoDB中的表格
        /// </summary>
        /// <param name="tableName">删除表格的名称</param>
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
        /// 创建或替换 DynamoDB 表格中的条目
        /// </summary>
        /// <param name="tableName">用于输入条目的表格名称</param>
        /// <param name="fields">表格中添加或替换的字段/属性</param>
        /// <param name="conditionExpression">选择性有条件表达式</param>
        /// <param name="conditionValues">有条件表达式</param>中使用的任择字段/属性值
        /// <returns></returns>
        public Task PutEntryAsync(string tableName, Dictionary<string, AttributeValue> fields, string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null)
            => PutEntryAsync(tableName, fields, CancellationToken.None, conditionExpression, conditionValues);

        /// <summary>使用所提供取消的标记创建或替换条目。</summary>
        /// <param name="tableName">表格名称。</param>
        /// <param name="fields">要写入的字段。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <param name="conditionExpression">任择条件表达式。</param>
        /// <param name="conditionValues">条件表达式值。</param>
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
        /// 在 DynamoDB 表格中创建或更新条目
        /// </summary>
        /// <param name="tableName">上调条目的表格名称</param>
        /// <param name="keys">条目的表格条目键</param>
        /// <param name="fields">表格中要添加或更新的字段/属性</param>
        /// <param name="conditionExpression">选择性有条件表达式</param>
        /// <param name="conditionValues">有条件表达式</param>中使用的任择字段/属性值
        /// <param name="extraExpression">上升标准表达式结尾处添加的额外表达式</param>
        /// <param name="extraExpressionValues">用于 extraExpression</param>
        /// <remarks>字段字典项目值将更新,并使用从 DynamoDB</remarks>返回的数值
        /// <returns></returns>
        public Task UpsertEntryAsync(string tableName, Dictionary<string, AttributeValue> keys, Dictionary<string, AttributeValue> fields,
            string conditionExpression = "", Dictionary<string, AttributeValue>? conditionValues = null, string extraExpression = "",
            Dictionary<string, AttributeValue>? extraExpressionValues = null)
            => UpsertEntryAsync(tableName, keys, fields, CancellationToken.None, conditionExpression, conditionValues, extraExpression, extraExpressionValues);

        /// <summary>使用已提供的取消符创建或更新条目。</summary>
        /// <param name="tableName">表格名称。</param>
        /// <param name="keys">条目键。</param>
        /// <param name="fields">需要写入的字段,以 DynamoDB返回的数值更新。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <param name="conditionExpression">任择条件表达式。</param>
        /// <param name="conditionValues">条件表达式值。</param>
        /// <param name="extraExpression">附加更新表达式。</param>
        /// <param name="extraExpressionValues">附加表达式值。</param>
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
        /// <param name="fields">更新表达式中指定属性。</param>
        /// <param name="conditionValues">条件表达式引用的值。</param>
        /// <param name="extraExpression">属性分配后附加的另外一个更新表达式。</param>
        /// <param name="extraExpressionValues">引用的数值 <paramref name="extraExpression"/>.</param>
        /// <returns>更新表达式和所有表达式属性值,用于更新和条件表达式。</returns>
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
        /// 删除 DynamoDB 表格中的条目
        /// </summary>
        /// <param name="tableName">删除条目的表格名称</param>
        /// <param name="keys">删除条目的表格项键</param>
        /// <param name="conditionExpression">选择性有条件表达式</param>
        /// <param name="conditionValues">有条件表达式</param>中使用的任择字段/属性值
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
        /// 删除 DynamoDB 表格中的多个条目(批次删除)
        /// </summary>
        /// <param name="tableName">删除条目的表格名称</param>
        /// <param name="toDelete">批批中必须删除的每个条目的关键值列表</param>
        /// <returns></returns>
        public Task DeleteEntriesAsync(string tableName, IReadOnlyCollection<Dictionary<string, AttributeValue>> toDelete)
            => DeleteEntriesAsync(tableName, toDelete, CancellationToken.None);

        /// <summary>使用已提供的取消符号删除一批条目。</summary>
        /// <param name="tableName">表格名称。</param>
        /// <param name="toDelete">要删除条目的密钥。</param>
        /// <param name="cancellationToken">取消牌。</param>
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
        /// 从 DynamoDB 表格中读取条目
        /// </summary>
        /// <typeparam name="TResult">结果类型</typeparam>
        /// <param name="tableName">查找条目的表格名称</param>
        /// <param name="keys">搜索</param>的表格条目键
        /// <param name="resolver">函数将被调用,将返回的字段转换为具体类型。此函数仅在结果为 != null</param>时才被调用。
        /// <returns>由解析函数翻译的对象</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 是 <see langword="null"/>.</exception>
        public Task<TResult?> ReadSingleEntryAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys, Func<Dictionary<string, AttributeValue>, TResult> resolver) where TResult : class
            => ReadSingleEntryAsync(tableName, keys, resolver, CancellationToken.None);

        /// <summary>使用已提供的取消符读取条目。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表格名称。</param>
        /// <param name="keys">条目键。</param>
        /// <param name="resolver">将返回的字段转换为结果。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 是 <see langword="null"/>.</exception>
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
        /// 通过过滤其密钥查询 DynamoDB 表格中多个条目
        /// </summary>
        /// <typeparam name="TResult">结果类型</typeparam>
        /// <param name="tableName">查找条目的表格名称</param>
        /// <param name="keys">搜索</param>的表格条目键
        /// <param name="keyConditionExpression">将过滤密钥的表达式</param>
        /// <param name="resolver">函数将被调用,将返回的字段转换成具体类型。此函数仅在结果为 != null时才被调用,并且每个条目都将被调用,因为每一个条目与查询匹配并添加到结果列表</param>
        /// <param name="indexName">使用二级指数的 keyConditionExpression</param>
        /// <param name="scanIndexForward">如果使用索引,请显示请求令是升(正)升(正)还是降(假)</param>
        /// <param name="lastEvaluatedKey">此操作将评价的第一个项目的主要键。 使用上次操作中返回的 LastEvaluatedKey 数值</param>
        /// <param name="consistentRead">确定读取一致性模式。请注意,如果使用 GSII, 则该模型必须是假的。</param>
        /// <returns>收藏品,其中列有由解析器函数翻译的对象和按页标结果的 LastEvaluatedKey 的</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 是 <see langword="null"/>.</exception>
        public Task<(List<TResult> results, Dictionary<string, AttributeValue>? lastEvaluatedKey)> QueryAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys, string keyConditionExpression, Func<Dictionary<string, AttributeValue>, TResult> resolver, string indexName = "", bool scanIndexForward = true, Dictionary<string, AttributeValue>? lastEvaluatedKey = null, bool consistentRead = true) where TResult : class
            => QueryAsync(tableName, keys, keyConditionExpression, resolver, CancellationToken.None, indexName, scanIndexForward, lastEvaluatedKey, consistentRead);

        /// <summary>使用所提供取消的符号查询条目的页面。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表格名称。</param>
        /// <param name="keys">关键条件值。</param>
        /// <param name="keyConditionExpression">键的表达式过滤。</param>
        /// <param name="resolver">将每个条目转换为结果类型。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <param name="indexName">选用的次要索引名称。</param>
        /// <param name="scanIndexForward">索引是否按升序阅读。</param>
        /// <param name="lastEvaluatedKey">上页的续页密钥 。</param>
        /// <param name="consistentRead">读作必须严格一致。</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 是 <see langword="null"/>.</exception>
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
        /// 通过过滤其密钥查询 DynamoDB 表格中多个条目
        /// </summary>
        /// <typeparam name="TResult">结果类型</typeparam>
        /// <param name="tableName">查找条目的表格名称</param>
        /// <param name="keys">搜索</param>的表格条目键
        /// <param name="keyConditionExpression">将过滤密钥的表达式</param>
        /// <param name="resolver">函数将被调用,将返回的字段转换成具体类型。此函数仅在结果为 != null时才被调用,并且每个条目都将被调用,因为每一个条目与查询匹配并添加到结果列表</param>
        /// <param name="indexName">使用二级指数的 keyConditionExpression</param>
        /// <param name="scanIndexForward">如果使用索引,请显示请求令是升(正)升(正)还是降(假)</param>
        /// <param name="consistentRead">确定读取一致性模式。请注意,如果使用 GSII, 则该模型必须是假的。</param>
        /// <returns>收藏含有由分辨率函数翻译的物体列表</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 是 <see langword="null"/>.</exception>
        public Task<List<TResult>> QueryAllAsync<TResult>(string tableName, Dictionary<string, AttributeValue> keys,
                string keyConditionExpression, Func<Dictionary<string, AttributeValue>, TResult> resolver,
                string indexName = "", bool scanIndexForward = true, bool consistentRead = true) where TResult : class
            => QueryAllAsync(tableName, keys, keyConditionExpression, resolver, CancellationToken.None, indexName, scanIndexForward, consistentRead);

        /// <summary>使用已提供的取消符号查询所有匹配条目。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表格名称。</param>
        /// <param name="keys">关键条件值。</param>
        /// <param name="keyConditionExpression">键的表达式过滤。</param>
        /// <param name="resolver">将每个条目转换为结果类型。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <param name="indexName">选用的次要索引名称。</param>
        /// <param name="scanIndexForward">索引是否按升序阅读。</param>
        /// <param name="consistentRead">读作必须严格一致。</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 是 <see langword="null"/>.</exception>
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
        /// 通过查询条目字段扫描 DynamoDB 表格。
        /// </summary>
        /// <typeparam name="TResult">结果类型</typeparam>
        /// <param name="tableName">查找条目的表格名称</param>
        /// <param name="attributes">表达式</param>上使用的属性
        /// <param name="expression">过滤表达式</param>
        /// <param name="resolver">函数将被调用,将返回的字段转换成具体类型。此函数仅在结果为 != null时才被调用,并且每个条目都将被调用,因为每一个条目与查询匹配并添加到结果列表</param>
        /// <returns>收藏含有由分辨率函数翻译的物体列表</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resolver"/> 是 <see langword="null"/>.</exception>
        public Task<List<TResult>> ScanAsync<TResult>(string tableName, Dictionary<string, AttributeValue> attributes, string expression, Func<Dictionary<string, AttributeValue>, TResult> resolver) where TResult : class =>
            ScanAsync(tableName, attributes, expression, resolver, CancellationToken.None);

        internal async Task<List<TResult>> ScanAsync<TResult>(string tableName, Dictionary<string, AttributeValue> attributes, string expression, Func<Dictionary<string, AttributeValue>, TResult> resolver, CancellationToken cancellationToken) where TResult : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(resolver);

            // 亚马逊文件:
            // 一次扫描操作将读取最多数量的项目
            // (如果使用限制参数)或数据的最大值为1MB,然后应用
            // 使用 FilterExpression过滤结果。”
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
        /// 切换或替换 DynamoDB 表格中的多个条目(批次放)
        /// </summary>
        /// <param name="tableName">查找条目的表格名称</param>
        /// <param name="toCreate">批量中必须创建或替换的每一条目的关键值列表</param>
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
        /// 交易读取 DynamoDB 表格中的条目
        /// </summary>
        /// <typeparam name="TResult">结果类型</typeparam>
        /// <param name="tableName">查找条目的表格名称</param>
        /// <param name="keys">搜索</param>的表格条目键
        /// <param name="resolver">函数将被调用,将返回的字段转换为具体类型。此函数仅在结果为 != null</param>时才被调用。
        /// <returns>由解析函数翻译的对象</returns>
        public Task<IEnumerable<TResult>> GetEntriesTxAsync<TResult>(string tableName, IEnumerable<Dictionary<string, AttributeValue>> keys, Func<Dictionary<string, AttributeValue>, TResult> resolver) where TResult : class
            => GetEntriesTxAsync(tableName, keys, resolver, CancellationToken.None);

        /// <summary>使用已提供的取消符号对条目进行交易读取。</summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="tableName">表格名称。</param>
        /// <param name="keys">条目要读取的密钥。</param>
        /// <param name="resolver">将每个条目转换为结果类型。</param>
        /// <param name="cancellationToken">取消牌。</param>
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
        /// 交易执行写书请求
        /// </summary>
        /// <param name="puts">任何要执行的定额</param>
        /// <param name="updates">任何更新待执行</param>
        /// <param name="deletes">任何要执行的删除</param>
        /// <param name="conditionChecks">任何需要进行的条件检查</param>
        /// <returns></returns>
        public Task WriteTxAsync(IEnumerable<Put>? puts = null, IEnumerable<Update>? updates = null, IEnumerable<Delete>? deletes = null, IEnumerable<ConditionCheck>? conditionChecks = null)
            => WriteTxAsync(CancellationToken.None, puts, updates, deletes, conditionChecks);

        /// <summary>使用提供的取消符号进行交易性写入条目。</summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <param name="puts">要放置的条目。</param>
        /// <param name="updates">要执行的更新。</param>
        /// <param name="deletes">删除要执行的删除。</param>
        /// <param name="conditionChecks">检查的条件。</param>
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
        /// 交易执行写书请求
        /// </summary>
        /// <param name="transactItems">要执行的笔记交易项</param>
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
