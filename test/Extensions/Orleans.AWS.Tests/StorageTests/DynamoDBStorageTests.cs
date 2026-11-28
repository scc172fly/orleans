using Amazon.DynamoDBv2.Model;
using Xunit;

namespace AWSUtils.Tests.StorageTests.AWSUtils
{
    /// <summary>
    /// DynamoDB 存储操作测试。
    /// 验证 CRUD 操作、条件更新及基于 ETag 的并发控制。
    /// </summary>
    [TestCategory("Storage"), TestCategory("AWS"), TestCategory("DynamoDb")]
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
    [TestArea("Persistence")]
    public class DynamoDBStorageTests : IClassFixture<DynamoDBStorageTestsFixture>
    {
        private readonly string PartitionKey;
        private readonly UnitTestDynamoDBStorage manager;

        public DynamoDBStorageTests(DynamoDBStorageTestsFixture fixture)
        {
            if (!AWSTestConstants.IsDynamoDbAvailable)
                throw Xunit.Sdk.SkipException.ForSkip("Unable to connect to AWS DynamoDB simulator");

            manager = fixture.DataManager;
            PartitionKey = "PK-DynamoDBDataManagerTests-" + Guid.NewGuid();
        }

        private UnitTestDynamoDBTableData GenerateNewData()
        {
            return new UnitTestDynamoDBTableData("JustData", PartitionKey, "RK-" + Guid.NewGuid());
        }

        /// <summary>
        /// 测试创建新数据项时使用条件检查以防止重复。
        /// 验证条件表达式能够防止覆盖现有数据项。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task DynamoDBDataManager_CreateItemAsync()
        {
            var expression = "attribute_not_exists(PartitionKey) AND attribute_not_exists(RowKey)";
            var toPersist = GenerateNewData();
            await manager.PutEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetValues(toPersist, true), expression);
            var originalEtag = toPersist.ETag;
            var persisted = await manager.ReadSingleEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist), response => new UnitTestDynamoDBTableData(response));
            Assert.NotNull(persisted);
            Assert.Equal(toPersist.StringData, persisted.StringData);
            Assert.True(persisted.ETag == 0);
            Assert.Equal(originalEtag, persisted.ETag);

            await Assert.ThrowsAsync<ConditionalCheckFailedException>(async () =>
            {
                var toPersist2 = toPersist.Clone();
                await manager.PutEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetValues(toPersist, true), expression);
            });
        }

        /// <summary>
        /// 测试数据项的插入或更新（upsert）。
        /// 验证数据项创建后能够在后续操作中更新。
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task DynamoDBDataManager_UpsertItemAsync()
        {
            var expression = "attribute_not_exists(PartitionKey) AND attribute_not_exists(RowKey)";
            var toPersist = GenerateNewData();
            toPersist.StringData = "Create";
            await manager.PutEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetValues(toPersist, true), expression);

            toPersist.StringData = "Replaced";
            await manager.UpsertEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist), GetValues(toPersist));
            var persisted = await manager.ReadSingleEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist), response => new UnitTestDynamoDBTableData(response));
            Assert.NotNull(persisted);
            Assert.Equal("Replaced", persisted.StringData);
            Assert.True(persisted.ETag == 0); //是的，ETag 没有改变，因为我们没有修改它。

            persisted.StringData = "Updated";
            var persistedEtag = persisted.ETag;
            expression = $"ETag = :OldETag";
            persisted.ETag++; //递增 ETag
            var expValues = new Dictionary<string, AttributeValue> { { ":OldETag", new AttributeValue { N = persistedEtag.ToString() } } };
            await manager.UpsertEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(persisted), GetValues(persisted), expression, expValues);
            persisted = await manager.ReadSingleEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist), response => new UnitTestDynamoDBTableData(response));
            Assert.NotNull(persisted);
            Assert.Equal("Updated", persisted.StringData);
            Assert.NotEqual(persistedEtag, persisted.ETag); //现在 ETag 已改变，因为我们手动修改了它。

            await Assert.ThrowsAsync<ConditionalCheckFailedException>(async () =>
            {
                await manager.UpsertEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist), GetValues(toPersist), expression, expValues);
            });
        }

        [Fact, TestCategory("Functional")]
        public async Task DynamoDBDataManager_DeleteItemAsync()
        {
            var toPersist = GenerateNewData();
            await manager.PutEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetValues(toPersist, true));
            await manager.DeleteEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist));
            var persisted = await manager.ReadSingleEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist), response => new UnitTestDynamoDBTableData(response));
            Assert.Null(persisted);
        }

        [Fact, TestCategory("Functional")]
        public async Task DynamoDBDataManager_ReadSingleTableEntryAsync()
        {
            var toPersist = GenerateNewData();
            await manager.PutEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetValues(toPersist, true));
            var persisted = await manager.ReadSingleEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(toPersist), response => new UnitTestDynamoDBTableData(response));
            Assert.NotNull(persisted);

            var data = GenerateNewData();
            var notFound = await manager.ReadSingleEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetKeys(data), response => new UnitTestDynamoDBTableData(response));
            Assert.Null(notFound);
        }

        [Fact, TestCategory("Functional")]
        public async Task DynamoDBDataManager_ReadAllTableEntryByPartitionAsync()
        {
            var toPersist = GenerateNewData();
            await manager.PutEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetValues(toPersist, true));
            var toPersist2 = toPersist.Clone();
            toPersist2.RowKey += "otherKey";
            await manager.PutEntryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, GetValues(toPersist2, true));
            var keys = new Dictionary<string, AttributeValue> { { ":PK", new AttributeValue(toPersist.PartitionKey) } };
            var found = await manager.QueryAsync(UnitTestDynamoDBStorage.INSTANCE_TABLE_NAME, keys, $"PartitionKey = :PK", item => new UnitTestDynamoDBTableData(item));
            Assert.NotNull(found.results);
            Assert.True(found.results.Count == 2);
        }

        internal static Dictionary<string, AttributeValue> GetKeys(UnitTestDynamoDBTableData data)
        {
            var keys = new Dictionary<string, AttributeValue>();
            keys.Add("PartitionKey", new AttributeValue(data.PartitionKey));
            keys.Add("RowKey", new AttributeValue(data.RowKey));
            return keys;
        }

        internal static Dictionary<string, AttributeValue> GetValues(UnitTestDynamoDBTableData data, bool includeKeys = false)
        {
            var values = new Dictionary<string, AttributeValue>();
            if (!string.IsNullOrWhiteSpace(data.StringData))
            {
                values.Add("StringData", new AttributeValue(data.StringData));
            }
            if (data.BinaryData != null && data.BinaryData.Length > 0)
            {
                values.Add("BinaryData", new AttributeValue { B = new MemoryStream(data.BinaryData) });
            }

            if (includeKeys)
            {
                values.Add("PartitionKey", new AttributeValue(data.PartitionKey));
                values.Add("RowKey", new AttributeValue(data.RowKey));
            }

            values.Add("ETag", new AttributeValue { N = data.ETag.ToString() });
            return values;
        }
    }
}
