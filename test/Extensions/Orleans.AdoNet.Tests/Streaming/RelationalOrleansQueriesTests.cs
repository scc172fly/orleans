using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Orleans.Configuration;
using Orleans.Streaming.AdoNet;
using Orleans.Streaming.AdoNet.Storage;
using UnitTests.General;
using static System.String;

namespace Tester.AdoNet.Streaming;

/// <summary>
/// 通过 Sql 服务器测试 <see cref="RelationalOrleansQueries"/> 的关联存储层。
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerRelationalOrleansQueriesTests() : RelationalOrleansQueriesTests(AdoNetInvariants.InvariantNameSqlServer, 90)
{
    [Fact]
    public Task RelationalOrleansQueries_SerializesQueueMessageCommits() =>
        VerifySqlServerQueueMessageCommitsAreSerialized(TestContext.Current.CancellationToken);
}

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/> 测试 MySQL的关联储存层。
/// </summary>
[TestCategory("MySql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlRelationalOrleansQueriesTests : RelationalOrleansQueriesTests
{
    public MySqlRelationalOrleansQueriesTests() : base(AdoNetInvariants.InvariantNameMySql, 20)
    {
        MySqlConnection.ClearAllPools();
    }

    [Fact]
    public Task RelationalOrleansQueries_OrdersProviderResults() =>
        VerifyProviderResultsAreOrdered(TestContext.Current.CancellationToken);
}

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/> 测试 PostgreSQL的关联储存层。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlRelationalOrleansQueriesTests : RelationalOrleansQueriesTests
{
    public PostgreSqlRelationalOrleansQueriesTests() : base(AdoNetInvariants.InvariantNamePostgreSql, 99)
    {
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/>测试相关储存层。
/// </summary>
[TestCategory("AdoNet"), TestCategory("Streaming")]
[TestSuite("Functional")]
[TestArea("Streaming")]
public abstract class RelationalOrleansQueriesTests(string invariant, int concurrency = 100) : IAsyncLifetime
{
    private const string TestDatabaseName = "OrleansStreamTest";

    private IRelationalStorage _storage = null!;
    private RelationalOrleansQueries _queries = null!;

    public async ValueTask InitializeAsync()
    {
        var testing = await RelationalStorageForTesting.SetupInstance(
            invariant,
            TestDatabaseName,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.SkipWhen(IsNullOrEmpty(testing.CurrentConnectionString), $"Database '{TestDatabaseName}' not initialized");

        _storage = RelationalStorage.CreateInstance(invariant, testing.CurrentConnectionString);

        _queries = await RelationalOrleansQueries.CreateInstance(invariant, testing.CurrentConnectionString);
    }

    private static string RandomServiceId(int max = 10) => $"ServiceId{Random.Shared.Next(max)}";

    private static string RandomProviderId(int max = 10) => $"ProviderId{Random.Shared.Next(max)}";

    private static string RandomQueueId(int max = 10) => $"QueueId{Random.Shared.Next(max)}";

    private static int RandomExpiryTimeout(int max = 100) => Random.Shared.Next(max);

    private static byte[] RandomPayload(int size = 1_000_000)
    {
        var payload = new byte[size];
        Random.Shared.NextBytes(payload);
        return payload;
    }

    private async Task<AdoNetStreamMessageAck[]> QueueMessagesAsync(
        string serviceId,
        string providerId,
        string queueId,
        byte[] payload,
        int expiryTimeout,
        int count,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(concurrency);
        return await Task.WhenAll(Enumerable.Range(0, count).Select(async _ =>
        {
            return await ExecuteProviderOperationAsync(
                semaphore,
                () => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout),
                cancellationToken);
        }));
    }

    private static async Task<T> ExecuteProviderOperationAsync<T>(
        SemaphoreSlim semaphore,
        Func<Task<T>> operationFactory,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        Task<T>? operation = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation = operationFactory();
            return await operation.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (operation is not null)
            {
                if (operation.IsCompleted)
                {
                    await ObserveLateCompletionAsync(operation);
                }
                else
                {
                    _ = ObserveLateCompletionAsync(operation);
                }
            }

            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task ObserveLateCompletionAsync(Task operation)
    {
        try
        {
            await operation;
        }
        catch
        {
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected async Task VerifyProviderResultsAreOrdered(CancellationToken cancellationToken)
    {
        const string reverseQuery = """
            SELECT ServiceId, ProviderId, QueueId, MessageId, Dequeued, VisibleOn, ExpiresOn, CreatedOn, ModifiedOn, Payload
            FROM OrleansStreamMessage
            WHERE ServiceId = @ServiceId AND ProviderId = @ProviderId AND QueueId = @QueueId
            ORDER BY MessageId DESC
            LIMIT @MaxCount
            """;

        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = new byte[] { 0xFF };

        await QueueMessagesAsync(serviceId, providerId, queueId, payload, 100, 100, cancellationToken);

        await _storage.ExecuteAsync(
            "UPDATE OrleansQuery SET QueryText = @QueryText WHERE QueryKey = 'GetStreamMessagesKey'",
            command =>
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "QueryText";
                parameter.Value = reverseQuery;
                command.Parameters.Add(parameter);
            },
            cancellationToken: cancellationToken);

        var queries = await RelationalOrleansQueries.CreateInstance(invariant, _storage.ConnectionString);
        var messages = await queries.GetStreamMessagesAsync(serviceId, providerId, queueId, 100, 3, 10, 100, 10, 1000);

        Assert.Equal(messages.OrderBy(message => message.MessageId), messages);
    }

    protected async Task VerifySqlServerQueueMessageCommitsAreSerialized(CancellationToken cancellationToken)
    {
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = new byte[] { 0xFF };
        const int expiryTimeout = 100;

        await using var firstConnection = new SqlConnection(_storage.ConnectionString);
        await firstConnection.OpenAsync(cancellationToken);
        await using var firstTransaction = (SqlTransaction)await firstConnection.BeginTransactionAsync(cancellationToken);
        await using var firstCommand = CreateQueueCommand(firstConnection, firstTransaction, serviceId, providerId, queueId, payload, expiryTimeout);
        var firstMessageId = await ReadMessageId(firstCommand, cancellationToken);

        await using (var secondConnection = new SqlConnection(_storage.ConnectionString))
        {
            await secondConnection.OpenAsync(cancellationToken);
            await using var secondCommand = CreateQueueCommand(secondConnection, transaction: null, serviceId, providerId, queueId, payload, expiryTimeout);
            secondCommand.CommandType = CommandType.Text;
            secondCommand.CommandText = "SET LOCK_TIMEOUT 0; EXECUTE QueueStreamMessage @ServiceId, @ProviderId, @QueueId, @Payload, @ExpiryTimeout;";

            var exception = await Assert.ThrowsAsync<SqlException>(() => secondCommand.ExecuteReaderAsync(cancellationToken));
            Assert.Equal(51000, exception.Number);
        }

        await firstTransaction.CommitAsync(cancellationToken);

        var secondAck = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, 2, 3, 10, 100, 10, 1000);

        Assert.Equal([firstMessageId, secondAck.MessageId], messages.Select(message => message.MessageId));
    }

    private static SqlCommand CreateQueueCommand(
        SqlConnection connection,
        SqlTransaction? transaction,
        string serviceId,
        string providerId,
        string queueId,
        byte[] payload,
        int expiryTimeout)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "QueueStreamMessage";
        command.Parameters.AddWithValue("ServiceId", serviceId);
        command.Parameters.AddWithValue("ProviderId", providerId);
        command.Parameters.AddWithValue("QueueId", queueId);
        command.Parameters.AddWithValue("Payload", payload);
        command.Parameters.AddWithValue("ExpiryTimeout", expiryTimeout);
        return command;
    }

    private static async Task<long> ReadMessageId(SqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return reader.GetInt64(reader.GetOrdinal(nameof(AdoNetStreamMessage.MessageId)));
    }

    /// <summary>
    /// 测试是否排队了单个信件 。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_QueuesMessage()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var expiryTimeout = RandomExpiryTimeout();
        var payload = RandomPayload();

        // 行为
        var before = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var after = DateTime.UtcNow.AddSeconds(1);

        // 索赔 - ack
        Assert.NotNull(ack);
        Assert.Equal(serviceId, ack.ServiceId);
        Assert.Equal(providerId, ack.ProviderId);
        Assert.Equal(queueId, ack.QueueId);
        Assert.Equal(1, ack.MessageId);

        // - 储存
        var messages = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken);
        var message = Assert.Single(messages);
        Assert.Equal(serviceId, message.ServiceId);
        Assert.Equal(providerId, message.ProviderId);
        Assert.Equal(queueId, message.QueueId);
        Assert.Equal(ack.MessageId, message.MessageId);
        Assert.Equal(0, message.Dequeued);
        Assert.True(message.VisibleOn >= before);
        Assert.True(message.VisibleOn <= after);
        Assert.True(message.ExpiresOn >= before.AddSeconds(expiryTimeout));
        Assert.True(message.ExpiresOn <= after.AddSeconds(expiryTimeout));
        Assert.Equal(message.VisibleOn, message.CreatedOn);
        Assert.Equal(message.VisibleOn, message.ModifiedOn);
        Assert.Equal(payload, message.Payload);
    }

    /// <summary>
    /// 测试许多信件在相同的队列上平行排队 。
    /// </summary>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_QueuesManyMessagesInParallel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var expiryTimeout = RandomExpiryTimeout();
        var payload = RandomPayload(1000);
        var count = 10000;

        // 将请求保留在默认连接集合限制下,以避免因连接超时而进行不相干测试
        using var semaphore = new SemaphoreSlim(concurrency);

        // 行为
        var before = DateTime.UtcNow.AddSeconds(-1);
        var acks = await Task.WhenAll(Enumerable
            .Range(0, count)
            .Select(i => Task.Run(async () =>
            {
                return await ExecuteProviderOperationAsync(
                    semaphore,
                    () => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout),
                    cancellationToken);
            }, cancellationToken))
            .ToList());
        var after = DateTime.UtcNow.AddSeconds(1);

        // 自动输入 - 顺序插入信件。
        var ordered = acks
            .OrderBy(x => x.ServiceId)
            .ThenBy(x => x.ProviderId)
            .ThenBy(x => x.QueueId)
            .ThenBy(x => x.MessageId)
            .ToList();
        var messageId = ordered[0].MessageId;
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(serviceId, ordered[i].ServiceId);
            Assert.Equal(providerId, ordered[i].ProviderId);
            Assert.Equal(queueId, ordered[i].QueueId);
            Assert.Equal(messageId++, ordered[i].MessageId);
        }

        // 平面 - 信件按预期存储
        var stored = (await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            cancellationToken))
            .OrderBy(x => x.ServiceId)
            .ThenBy(x => x.ProviderId)
            .ThenBy(x => x.QueueId)
            .ThenBy(x => x.MessageId)
            .ToList();
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(ordered[i].ServiceId, stored[i].ServiceId);
            Assert.Equal(ordered[i].ProviderId, stored[i].ProviderId);
            Assert.Equal(ordered[i].QueueId, stored[i].QueueId);
            Assert.Equal(ordered[i].MessageId, stored[i].MessageId);
            Assert.Equal(0, stored[i].Dequeued);
            Assert.True(stored[i].VisibleOn >= before);
            Assert.True(stored[i].VisibleOn <= after);
            Assert.True(stored[i].ExpiresOn >= before.AddSeconds(expiryTimeout));
            Assert.True(stored[i].ExpiresOn <= after.AddSeconds(expiryTimeout));
            Assert.Equal(stored[i].VisibleOn, stored[i].CreatedOn);
            Assert.Equal(stored[i].VisibleOn, stored[i].ModifiedOn);
            Assert.Equal(payload, stored[i].Payload);
        }
    }

    /// <summary>
    /// 测试许多信件在多个队列中同时排队。
    /// </summary>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_QueuesManyMessagesInParallelOnManyQueues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // - 安排创建最多27个随机分区密钥,每个分区以随机顺序创建约1000个随机信件
        var expiryTimeout = RandomExpiryTimeout();
        var count = 3 * 3 * 3 * 1000;
        var partitions = Enumerable
            .Range(0, count)
            .Select(i =>
            (
                ServiceId: RandomServiceId(3),
                ProviderId: RandomProviderId(3),
                QueueId: RandomQueueId(3),
                Payload: RandomPayload(1000)
            ))
            .ToList();

        // 将请求保留在默认连接集合限制下,以避免因连接超时而进行不相干测试
        using var semaphore = new SemaphoreSlim(concurrency);

        // 动作 - 将随机消息平行排列
        var before = DateTime.UtcNow.AddSeconds(-1);
        var results = await Task.WhenAll(partitions
            .Select(p => Task.Run(async () =>
            {
                var ack = await ExecuteProviderOperationAsync(
                    semaphore,
                    () => _queries.QueueStreamMessageAsync(p.ServiceId, p.ProviderId, p.QueueId, p.Payload, expiryTimeout),
                    cancellationToken);
                return (Partition: p, Ack: ack);
            }, cancellationToken))
            .ToList());
        var after = DateTime.UtcNow.AddSeconds(1);

        // - 所有信息均得到确认
        var messageIds = new SortedSet<long>();
        foreach (var (partition, ack) in results)
        {
            Assert.Equal(ack.ServiceId, partition.ServiceId);
            Assert.Equal(ack.ProviderId, partition.ProviderId);
            Assert.Equal(ack.QueueId, partition.QueueId);
            Assert.True(messageIds.Add(ack.MessageId), $"Duplicate {ack.MessageId}");
        }

        // 即正对 - 生成的电文代号一致
        Assert.Equal(count, messageIds.Count);
        Assert.Equal(1, messageIds.Min);
        Assert.Equal(messageIds.Count, messageIds.Max);

        // 平面 - 信件按预期存储
        var stored = (await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            cancellationToken))
            .ToDictionary(x => (x.ServiceId, x.ProviderId, x.QueueId, x.MessageId));

        foreach (var (partition, ack) in results)
        {
            Assert.True(stored.TryGetValue((ack.ServiceId, ack.ProviderId, ack.QueueId, ack.MessageId), out var message), $"Message not found in storage");

            Assert.Equal(0, message.Dequeued);
            Assert.True(message.VisibleOn >= before);
            Assert.True(message.VisibleOn <= after);
            Assert.True(message.ExpiresOn >= before.AddSeconds(expiryTimeout));
            Assert.True(message.ExpiresOn <= after.AddSeconds(expiryTimeout));
            Assert.Equal(message.VisibleOn, message.CreatedOn);
            Assert.Equal(message.VisibleOn, message.ModifiedOn);
            Assert.Equal(partition.Payload, message.Payload);

            stored.Remove((ack.ServiceId, ack.ProviderId, ack.QueueId, ack.MessageId));
        }
    }

    /// <summary>
    /// 测试单个信件是否正确解密 。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DequeuesSingleMessage()
    {
        // 安排
        await _storage.ExecuteAsync("DELETE FROM OrleansStreamMessage", TestContext.Current.CancellationToken);
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = RandomPayload();
        var expiryTimeout = 100;
        var maxCount = 1;
        var maxAttempts = 3;
        var visibilityTimeout = 10;
        var removalTimeout = 100;
        var evictionInterval = 10;
        var evictionBatchSize = 1000;

        // - 安排信件的后排
        var beforeQueueing = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var afterQueueing = DateTime.UtcNow.AddSeconds(1);

        // - 行为 - 删除消息
        var beforeDequeuing = DateTime.UtcNow.AddSeconds(-1);
        var message = Assert.Single(await _queries.GetStreamMessagesAsync(
            serviceId,
            providerId,
            queueId,
            maxCount,
            maxAttempts,
            visibilityTimeout,
            removalTimeout,
            evictionInterval,
            evictionBatchSize));
        var afterDequeuing = DateTime.UtcNow.AddSeconds(1);

        // 声明 - 电文相同
        Assert.Equal(ack.ServiceId, message.ServiceId);
        Assert.Equal(ack.ProviderId, message.ProviderId);
        Assert.Equal(ack.QueueId, message.QueueId);
        Assert.Equal(ack.MessageId, message.MessageId);
        Assert.Equal(1, message.Dequeued);
        Assert.True(message.VisibleOn >= beforeDequeuing.AddSeconds(visibilityTimeout));
        Assert.True(message.VisibleOn <= afterDequeuing.AddSeconds(visibilityTimeout));
        Assert.True(message.ExpiresOn >= beforeQueueing.AddSeconds(expiryTimeout));
        Assert.True(message.ExpiresOn <= afterQueueing.AddSeconds(expiryTimeout));
        Assert.True(message.CreatedOn >= beforeQueueing);
        Assert.True(message.CreatedOn <= afterQueueing);
        Assert.True(message.ModifiedOn >= beforeDequeuing);
        Assert.True(message.ModifiedOn <= afterDequeuing);
        Assert.Equal(payload, message.Payload);

        // 已经更改 - 服务器上的信件
        var stored = Assert.Single(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));
        Assert.Equal(message.ServiceId, stored.ServiceId);
        Assert.Equal(message.ProviderId, stored.ProviderId);
        Assert.Equal(message.QueueId, stored.QueueId);
        Assert.Equal(message.MessageId, stored.MessageId);
        Assert.Equal(message.Dequeued, stored.Dequeued);
        Assert.Equal(message.VisibleOn, stored.VisibleOn);
        Assert.Equal(message.ExpiresOn, stored.ExpiresOn);
        Assert.Equal(message.CreatedOn, stored.CreatedOn);
        Assert.Equal(message.ModifiedOn, stored.ModifiedOn);
        Assert.Equal(message.Payload, stored.Payload);
    }

    /// <summary>
    /// 测试信件的排队分批清除
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DequeuesMessageBatches()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = new byte[] { 0xFF };
        var expiryTimeout = 100;
        var maxCount = 60;
        var maxAttempts = 3;
        var visibilityTimeout = 10;
        var removalTimeout = 100;
        var evictionInterval = 10;
        var evictionBatchSize = 1000;
        var total = 100;

        // - 同时安排对信件的排列
        var beforeQueueing = DateTime.UtcNow.AddSeconds(-1);
        var acks = await QueueMessagesAsync(
            serviceId,
            providerId,
            queueId,
            payload,
            expiryTimeout,
            total,
            TestContext.Current.CancellationToken);
        var afterQueueing = DateTime.UtcNow.AddSeconds(1);

        // 动作 - 删除所有信件
        var beforeDequeuing = DateTime.UtcNow.AddSeconds(-1);
        var first = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);
        var second = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);
        var third = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);
        var afterDequeuing = DateTime.UtcNow.AddSeconds(1);

        // - 批次计数
        Assert.Equal(maxCount, first.Count);
        Assert.Equal(total - maxCount, second.Count);
        Assert.Empty(third);

        var messages = first.Concat(second).Concat(third).ToList();
        Assert.Equal(messages.OrderBy(message => message.MessageId), messages);

        // 即 - 解排信件与 acks 一致
        var ackLookup = acks.ToDictionary(x => (x.ServiceId, x.ProviderId, x.QueueId, x.MessageId));
        foreach (var message in messages)
        {
            Assert.True(ackLookup.TryGetValue((message.ServiceId, message.ProviderId, message.QueueId, message.MessageId), out var ack), "Ack not found");
            Assert.Equal(ack.ServiceId, message.ServiceId);
            Assert.Equal(ack.ProviderId, message.ProviderId);
            Assert.Equal(ack.QueueId, message.QueueId);
            Assert.Equal(ack.MessageId, message.MessageId);
            Assert.Equal(1, message.Dequeued);
            Assert.True(message.VisibleOn >= beforeDequeuing.AddSeconds(visibilityTimeout));
            Assert.True(message.VisibleOn <= afterDequeuing.AddSeconds(visibilityTimeout));
            Assert.True(message.ExpiresOn >= beforeQueueing.AddSeconds(expiryTimeout));
            Assert.True(message.ExpiresOn <= afterQueueing.AddSeconds(expiryTimeout));
            Assert.True(message.CreatedOn >= beforeQueueing);
            Assert.True(message.CreatedOn <= afterQueueing);
            Assert.True(message.ModifiedOn >= beforeDequeuing);
            Assert.True(message.ModifiedOn <= afterDequeuing);
            Assert.Equal(payload, message.Payload);

            ackLookup.Remove((message.ServiceId, message.ProviderId, message.QueueId, message.MessageId));
        }

        // 维护 - 存储的邮件与已解排的邮件一致
        var messageLookup = messages.ToDictionary(x => (x.ServiceId, x.ProviderId, x.QueueId, x.MessageId));
        var stored = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken);
        foreach (var item in stored)
        {
            Assert.True(messageLookup.TryGetValue((item.ServiceId, item.ProviderId, item.QueueId, item.MessageId), out var message), "Message not found");

            Assert.Equal(message.ServiceId, item.ServiceId);
            Assert.Equal(message.ProviderId, item.ProviderId);
            Assert.Equal(message.QueueId, item.QueueId);
            Assert.Equal(message.MessageId, item.MessageId);
            Assert.Equal(message.Dequeued, item.Dequeued);
            Assert.Equal(message.VisibleOn, item.VisibleOn);
            Assert.Equal(message.ExpiresOn, item.ExpiresOn);
            Assert.Equal(message.CreatedOn, item.CreatedOn);
            Assert.Equal(message.ModifiedOn, item.ModifiedOn);
            Assert.Equal(message.Payload, item.Payload);
        }
    }

    /// <summary>
    /// 测试在可见度超时后, 单条信件会重新删除, 直到尝试最大一次 。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DequeuesSingleMessageAgainAfterVisibilityTimeout()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = RandomPayload();
        var expiryTimeout = 100;
        var maxCount = 1;
        var maxAttempts = 3;
        var visibilityTimeout = 0;
        var removalTimeout = 100;
        var evictionInterval = 100;
        var evictionBatchSize = 0;

        // - 安排信件的后排
        var beforeQueueing = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var afterQueueing = DateTime.UtcNow.AddSeconds(1);

        // 动作 - 删除信件,直到最大尝试加一次
        var beforeDequeuing = DateTime.UtcNow.AddSeconds(-1);
        var results = new List<IList<AdoNetStreamMessage>>();
        for (var i = 0; i < maxAttempts + 1; i++)
        {
            results.Add(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));
        }
        var afterDequeuing = DateTime.UtcNow.AddSeconds(1);

        // - 分批处理符合预期要求
        for (var i = 0; i < maxAttempts; i++)
        {
            var message = Assert.Single(results[i]);

            Assert.Equal(ack.ServiceId, message.ServiceId);
            Assert.Equal(ack.ProviderId, message.ProviderId);
            Assert.Equal(ack.QueueId, message.QueueId);
            Assert.Equal(ack.MessageId, message.MessageId);
            Assert.Equal(i + 1, message.Dequeued);
            Assert.True(message.VisibleOn >= beforeDequeuing.AddSeconds(visibilityTimeout));
            Assert.True(message.VisibleOn <= afterDequeuing.AddSeconds(visibilityTimeout));
            Assert.True(message.ExpiresOn >= beforeQueueing.AddSeconds(expiryTimeout));
            Assert.True(message.ExpiresOn <= afterQueueing.AddSeconds(expiryTimeout));
            Assert.True(message.CreatedOn >= beforeQueueing);
            Assert.True(message.CreatedOn <= afterQueueing);
            Assert.True(message.ModifiedOn >= beforeDequeuing);
            Assert.True(message.ModifiedOn <= afterDequeuing);
            Assert.Equal(payload, message.Payload);
        }

        // 确定 - 最后批次为空
        Assert.Empty(results[maxAttempts]);

        // - 最终存储的电文与最后解排的电文一致
        var stored = Assert.Single(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));
        var final = Assert.Single(results[maxAttempts - 1]);
        Assert.Equal(final.ServiceId, stored.ServiceId);
        Assert.Equal(final.ProviderId, stored.ProviderId);
        Assert.Equal(final.QueueId, stored.QueueId);
        Assert.Equal(final.MessageId, stored.MessageId);
        Assert.Equal(final.Dequeued, stored.Dequeued);
        Assert.Equal(final.VisibleOn, stored.VisibleOn);
        Assert.Equal(final.ExpiresOn, stored.ExpiresOn);
        Assert.Equal(final.CreatedOn, stored.CreatedOn);
        Assert.Equal(final.ModifiedOn, stored.ModifiedOn);
        Assert.Equal(final.Payload, stored.Payload);
    }

    /// <summary>
    /// 测试在可见度超时前, 单个信息不会再次被解密 。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotDequeueSingleMessageBeforeVisibilityTimeout()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = RandomPayload();
        var expiryTimeout = 100;
        var maxCount = 3;
        var maxAttempts = 3;
        var visibilityTimeout = 10;
        var removalTimeout = 100;
        var evictionInterval = 10;
        var evictionBatchSize = 1000;

        // - 安排信件的后排
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);

        // 动作 - 删除信件
        var first = Assert.Single(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));
        var second = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 声明 - 首次解排的邮件符合 ack
        Assert.Equal(ack.ServiceId, first.ServiceId);
        Assert.Equal(ack.ProviderId, first.ProviderId);
        Assert.Equal(ack.QueueId, first.QueueId);
        Assert.Equal(ack.MessageId, first.MessageId);

        // 即时 - 已存储的电文与第一封电文一致
        var stored = Assert.Single(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));
        Assert.Equal(first.ServiceId, stored.ServiceId);
        Assert.Equal(first.ProviderId, stored.ProviderId);
        Assert.Equal(first.QueueId, stored.QueueId);
        Assert.Equal(first.MessageId, stored.MessageId);
        Assert.Equal(first.Dequeued, stored.Dequeued);
        Assert.Equal(first.VisibleOn, stored.VisibleOn);
        Assert.Equal(first.ExpiresOn, stored.ExpiresOn);
        Assert.Equal(first.CreatedOn, stored.CreatedOn);
        Assert.Equal(first.ModifiedOn, stored.ModifiedOn);
        Assert.Equal(first.Payload, stored.Payload);

        // 指定 - 信件不再取消排队
        Assert.Empty(second);
    }

    /// <summary>
    /// 测试信息能通过解结接收器 被发布以立即再投送
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_ReleasesMessageUsingDequeueReceipt()
    {
        var serviceId = $"Service-{Guid.NewGuid()}";
        var providerId = $"Provider-{Guid.NewGuid()}";
        var queueId = $"Queue-{Guid.NewGuid()}";
        var payload = RandomPayload();
        var expiryTimeout = 100;
        var maxCount = 1;
        var maxAttempts = 5;
        var visibilityTimeout = 100;
        var removalTimeout = 100;
        var evictionInterval = 100;
        var evictionBatchSize = 0;

        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var first = Assert.Single(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));
        var firstReceipt = new AdoNetStreamConfirmation(first.MessageId, first.Dequeued);

        var released = Assert.Single(await _queries.ReleaseStreamMessagesAsync(serviceId, providerId, queueId, [firstReceipt]));
        Assert.Equal(ack.MessageId, released.MessageId);

        var second = Assert.Single(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));
        Assert.Equal(first.Dequeued + 1, second.Dequeued);

        Assert.Empty(await _queries.ReleaseStreamMessagesAsync(serviceId, providerId, queueId, [firstReceipt]));
        Assert.Empty(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));

        var secondReceipt = new AdoNetStreamConfirmation(second.MessageId, second.Dequeued);
        Assert.Single(await _queries.ReleaseStreamMessagesAsync(serviceId, providerId, queueId, [secondReceipt]));
        var third = Assert.Single(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));
        await _queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, [new AdoNetStreamConfirmation(third.MessageId, third.Dequeued)]);
    }

    /// <summary>
    /// 测试单一条电文在过期后不再解排
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotDequeueSingleMessageAfterExpiry()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = RandomPayload();
        var expiryTimeout = 0;
        var maxCount = 3;
        var maxAttempts = 3;
        var visibilityTimeout = 0;
        var removalTimeout = 100;
        var evictionInterval = 10;
        var evictionBatchSize = 0;

        // - 安排信件的后排
        var before = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var after = DateTime.UtcNow.AddSeconds(1);

        // 动作 - 删除信件
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 平面 - 没有取消信件排队
        Assert.Empty(messages);

        // 声明 - 存储的邮件如预期
        var stored = Assert.Single(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));
        Assert.Equal(ack.ServiceId, stored.ServiceId);
        Assert.Equal(ack.ProviderId, stored.ProviderId);
        Assert.Equal(ack.QueueId, stored.QueueId);
        Assert.Equal(ack.MessageId, stored.MessageId);
        Assert.Equal(0, stored.Dequeued);
        Assert.True(stored.VisibleOn >= before);
        Assert.True(stored.VisibleOn <= after);
        Assert.True(stored.ExpiresOn >= before);
        Assert.True(stored.ExpiresOn <= after);
        Assert.True(stored.CreatedOn >= before);
        Assert.True(stored.CreatedOn <= after);
        Assert.True(stored.ModifiedOn >= before);
        Assert.True(stored.ModifiedOn <= after);
        Assert.Equal(payload, stored.Payload);
    }

    /// <summary>
    /// 测试信件是否得到确认。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_ConfirmsMessages()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = RandomPayload();
        var expiryTimeout = 100;
        var maxCount = 10;
        var maxAttempts = 3;
        var visibilityTimeout = 10;
        var removalTimeout = 100;
        var evictionInterval = 10;
        var evictionBatchSize = 1000;

        // - 安排对许多信件进行排列
        var acks = await Task.WhenAll(Enumerable
            .Range(0, maxCount)
            .Select(i => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout))
            .ToList());

        // - 安排取消所有信件的排队
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 动作 - 确认所有消息
        var items = messages.Select(x => new AdoNetStreamConfirmation(x.MessageId, x.Dequeued)).ToList();
        var results = await _queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, items);

        // - 肯定书与预期相同
        Assert.Equal(maxCount, acks.Length);
        Assert.Equal(maxCount, messages.Count);
        Assert.Equal(maxCount, results.Count);

        var lookup = acks.Select(x => (x.ServiceId, x.ProviderId, x.QueueId, x.MessageId)).ToHashSet();
        foreach (var result in results)
        {
            Assert.True(lookup.Remove((result.ServiceId, result.ProviderId, result.QueueId, result.MessageId)), "Unexpected Confirmation");
        }

        // - 数据没有留存
        var stored = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken);
        Assert.Empty(stored);
    }

    /// <summary>
    /// 测试信件接收不正确时未被确认 。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotConfirmMessagesWithWrongReceipt()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = RandomPayload();
        var expiryTimeout = 100;
        var maxCount = 10;
        var maxAttempts = 3;
        var visibilityTimeout = 10;
        var removalTimeout = 100;
        var evictionInterval = 10;
        var evictionBatchSize = 1000;

        // - 安排对许多信件进行排列
        var acks = await Task.WhenAll(Enumerable
            .Range(0, maxCount)
            .Select(i => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout))
            .ToList());

        // - 安排取消所有信件的排队
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 动作 - 以错误的方式确认所有信件
        var faulty = messages.Select(x => new AdoNetStreamConfirmation(x.MessageId, x.Dequeued - 1)).ToList();
        var results = await _queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, faulty);

        // - 肯定书与预期相同
        Assert.Equal(maxCount, acks.Length);
        Assert.Equal(maxCount, messages.Count);
        Assert.Empty(results);

        // - 数据仍在储存中
        var stored = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken);
        Assert.Equal(maxCount, stored.Count());
    }

    /// <summary>
    /// 测试有些信息可以确认,而另一些则不能证实。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_ConfirmsSomeMessagesAndNotOthers()
    {
        // 安排
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var payload = RandomPayload(1000);
        var expiryTimeout = 100;
        var maxCount = 100;
        var maxAttempts = 3;
        var visibilityTimeout = 10;
        var removalTimeout = 100;
        var evictionInterval = 10;
        var evictionBatchSize = 1000;
        var partial = 30;

        // - 安排对许多信件进行排列
        var acks = await Task.WhenAll(Enumerable
            .Range(0, maxCount)
            .Select(i => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout))
            .ToList());

        // - 安排全部信件的排队
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // - 随机确认一些信件
        var completed = Randomize(messages).Take(partial).Select(x => new AdoNetStreamConfirmation(x.MessageId, x.Dequeued)).ToList();
        var confirmed = await _queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, completed);

        // - 索赔要求---- 各项索赔都如预期
        Assert.Equal(maxCount, acks.Length);
        Assert.Equal(maxCount, messages.Count);
        Assert.Equal(partial, confirmed.Count);

        // 声明 - 确认信息与预期的相同
        var lookup = acks.ToDictionary(x => (x.ServiceId, x.ProviderId, x.QueueId, x.MessageId));
        var stored = (await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken))
            .ToDictionary(x => (x.ServiceId, x.ProviderId, x.QueueId, x.MessageId));
        foreach (var item in confirmed)
        {
            Assert.True(lookup.Remove((item.ServiceId, item.ProviderId, item.QueueId, item.MessageId)), "Unexpected Confirmation");
            Assert.False(stored.TryGetValue((item.ServiceId, item.ProviderId, item.QueueId, item.MessageId), out _), "Message still in storage");
        }

        // - 保留未证实的信息
        Assert.Equal(maxCount - partial, stored.Count);
        Assert.Equal(lookup.Keys.Order(), stored.Keys.Order());
    }

    /// <summary>
    /// 混杂的测试在复杂的随机情况下同时进行排队、脱队、确认和驱逐工作。
    /// 这需要同时出现易碎,特别是容易陷入数据库僵局,而不是一个具体的条件。
    /// 如果这一测试失误是由于僵局造成的,那么很可能存在一些需要调查的执行问题。
    /// </summary>
    /// <remarks>
    /// 在早期,这一试验一直导致僵局,直到基本查询完成。
    /// 这是一项昂贵的运行测试,
    /// 特别是对于 MySQL ,这一测试还发现驱动器连接池本身陷入僵局,需要升级包件。
    /// 见: https://bugs.mysql.com/bug.php?id=114272
    /// </remarks>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_ChaosTest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // - 安排 - 生成测试数据
        var total = 10000;
        var serviceIds = Enumerable.Range(0, 3).Select(x => $"ServiceId{x}").ToList();
        var providerIds = Enumerable.Range(0, 3).Select(x => $"ProviderId{x}").ToList();
        var queueIds = Enumerable.Range(0, 3).Select(x => $"QueueId{x}").ToList();
        var payload = RandomPayload(1000);
        var maxCount = 10;
        var maxAttempts = 3;
        var visibilityTimeout = 1;
        var removalTimeout = 1;
        var evictionInterval = 1;
        var evictionBatchSize = 1000;

        // 将请求保留在默认连接集合限制下,以避免因连接超时而进行不相干测试
        using var semaphore = new SemaphoreSlim(concurrency);

        // - 混乱的登记、取消、确认
        // 如下任务预计不会产生计划成果,但预期会产生一致的成果
        var acks = new ConcurrentBag<AdoNetStreamMessageAck>();
        var dequeued1 = new ConcurrentBag<AdoNetStreamMessage>();
        var dequeued2 = new ConcurrentBag<AdoNetStreamMessage>();
        var confirmed = new ConcurrentBag<AdoNetStreamConfirmationAck>();
        await Task.WhenAll(Enumerable
            .Range(0, total)
            .Select(async i =>
            {
                // 旋转随机排队任务
                var enqueue = Task.Run(async () =>
                {
                    var serviceId = serviceIds[Random.Shared.Next(serviceIds.Count)];
                    var providerId = providerIds[Random.Shared.Next(providerIds.Count)];
                    var queueId = queueIds[Random.Shared.Next(queueIds.Count)];

                    var ack = await ExecuteProviderOperationAsync(
                        semaphore,
                        () => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, visibilityTimeout),
                        cancellationToken);

                    acks.Add(ack);
                }, cancellationToken);

                // 随机脱排任务, 无法确认
                var dequeue = Task.Run(async () =>
                {
                    var serviceId = serviceIds[Random.Shared.Next(serviceIds.Count)];
                    var providerId = providerIds[Random.Shared.Next(providerIds.Count)];
                    var queueId = queueIds[Random.Shared.Next(queueIds.Count)];

                    var messages = await ExecuteProviderOperationAsync(
                        semaphore,
                        () => _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize),
                        cancellationToken);

                    foreach (var item in messages)
                    {
                        dequeued1.Add(item);
                    }
                }, cancellationToken);

                // 随机脱排任务,该任务也确认
                var confirm = Task.Run(async () =>
                {
                    var serviceId = serviceIds[Random.Shared.Next(serviceIds.Count)];
                    var providerId = providerIds[Random.Shared.Next(providerIds.Count)];
                    var queueId = queueIds[Random.Shared.Next(queueIds.Count)];

                    var messages = await ExecuteProviderOperationAsync(
                        semaphore,
                        () => _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize),
                        cancellationToken);

                    foreach (var item in messages)
                    {
                        dequeued2.Add(item);
                    }

                    var confirmation = await ExecuteProviderOperationAsync(
                        semaphore,
                        () => _queries.ConfirmStreamMessagesAsync(
                            serviceId,
                            providerId,
                            queueId,
                            messages.Select(x => new AdoNetStreamConfirmation(x.MessageId, x.Dequeued)).ToList()),
                        cancellationToken);

                    foreach (var item in confirmation)
                    {
                        confirmed.Add(item);
                    }
                }, cancellationToken);

                // 等待全部完成
                await Task.WhenAll(enqueue, dequeue, confirm);
            })
            .ToList());

        // 平面 - 所有信件都排入了队列
        Assert.Equal(total, acks.Count);

        // - 某些信息被解密(rng 依附者,如果不注意则删除)
        Assert.NotEmpty(dequeued1);
        Assert.NotEmpty(dequeued2);

        // - 某些信息得到确认(rng受扶养人,如果不确定则删除)
        Assert.NotEmpty(confirmed);

        // - 一些信息被遗漏(rng受扶养人,如果不注意就删除)
        var stored = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            cancellationToken);
        Assert.NotEmpty(stored);

        // - 确认信息没有被遗漏
        Assert.Empty(confirmed.IntersectBy(stored.Select(x => x.MessageId), x => x.MessageId));

        // 指定 - 确认信件所有匹配 acks
        Assert.Empty(confirmed.ExceptBy(acks.Select(x => x.MessageId), x => x.MessageId));
    }

    /// <summary>
    /// 测试毒物信息可以移到死字
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_MovesPoisonedMessageToDeadLetters()
    {
        // 安排
        var serviceId = "ServiceId";
        var providerId = "ProviderId";
        var streamOptions = new AdoNetStreamOptions();
        var cacheOptions = new SimpleQueueCacheOptions();

        // 排列过期信件的队列
        var queueId = "QueueId";
        var payload = new byte[] { 0xFF };

        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, streamOptions.ExpiryTimeout.TotalSecondsCeiling());

        // - 安排将信件解排并立即提供
        await _queries.GetStreamMessagesAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, cacheOptions.CacheSize, streamOptions.MaxAttempts, 0, streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling(), streamOptions.EvictionInterval.TotalSecondsCeiling(), streamOptions.EvictionBatchSize);
        Assert.Empty(await _storage.ReadAsync<AdoNetStreamDeadLetter>(
            "SELECT * FROM OrleansStreamDeadLetter",
            TestContext.Current.CancellationToken));

        // - 清理最大尝试的操作, 这样上面的信息会被标记
        await _queries.FailStreamMessageAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, ack.MessageId, 1, streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling());

        // 中指定 - 消息已不在信息表格中
        Assert.Empty(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));

        // 平面 - 信件被移动
        var dead = Assert.Single(await _storage.ReadAsync<AdoNetStreamDeadLetter>(
            "SELECT * FROM OrleansStreamDeadLetter",
            TestContext.Current.CancellationToken));
        Assert.Equal(serviceId, dead.ServiceId);
        Assert.Equal(providerId, dead.ProviderId);
        Assert.Equal(queueId, dead.QueueId);
        Assert.Equal(ack.MessageId, dead.MessageId);
        Assert.Equal(1, dead.Dequeued);
        Assert.Equal(dead.CreatedOn.Add(streamOptions.ExpiryTimeout.SecondsCeiling()), dead.ExpiresOn);
        Assert.Equal(dead.ModifiedOn, dead.VisibleOn);
        Assert.Equal(dead.DeadOn.Add(streamOptions.DeadLetterEvictionTimeout.SecondsCeiling()), dead.RemoveOn);
        Assert.Equal(payload, dead.Payload);
    }

    /// <summary>
    /// 测试健康信息不会被移动到死字
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotMoveHealthyMessageToDeadLetters()
    {
        // 安排
        var serviceId = "ServiceId";
        var providerId = "ProviderId";
        var queueId = "QueueId";
        var streamOptions = new AdoNetStreamOptions();
        var cacheOptions = new SimpleQueueCacheOptions();

        // - 排列普通信件
        var payload = new byte[] { 0xFF };
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, streamOptions.ExpiryTimeout.TotalSecondsCeiling());

        // - 安排取消消息的排队
        await _queries.GetStreamMessagesAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, cacheOptions.CacheSize, streamOptions.MaxAttempts, streamOptions.VisibilityTimeout.TotalSecondsCeiling(), streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling(), streamOptions.EvictionInterval.TotalSecondsCeiling(), streamOptions.EvictionBatchSize);

        // - 行为失败信息
        await _queries.FailStreamMessageAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, ack.MessageId, streamOptions.MaxAttempts, streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling());

        // e. 电文仍然在表格中,并且再次可见
        var saved = Assert.Single(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));
        Assert.Equal(ack.ServiceId, saved.ServiceId);
        Assert.Equal(ack.ProviderId, saved.ProviderId);
        Assert.Equal(ack.QueueId, saved.QueueId);
        Assert.Equal(ack.MessageId, saved.MessageId);
        Assert.Equal(1, saved.Dequeued);
        Assert.Equal(saved.ModifiedOn, saved.VisibleOn);

        // - 没有发出过任何信息,
        Assert.Empty(await _storage.ReadAsync<AdoNetStreamDeadLetter>(
            "SELECT * FROM OrleansStreamDeadLetter",
            TestContext.Current.CancellationToken));
    }

    private static List<T> Randomize<T>(IEnumerable<T> source)
    {
        var list = new List<T>(source.TryGetNonEnumeratedCount(out var count) ? count : 0);

        foreach (var item in source)
        {
            var index = Random.Shared.Next(list.Count + 1);
            if (index == list.Count)
            {
                list.Add(item);
            }
            else
            {
                list.Add(list[index]);
                list[index] = item;
            }
        }

        return list;
    }
}
