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
/// 通过 <see cref="RelationalOrleansQueries"/> 测试 SQL Server 上的关系型存储层。
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
/// 通过 <see cref="RelationalOrleansQueries"/> 测试 MySQL 上的关系型存储层。
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
/// 通过 <see cref="RelationalOrleansQueries"/> 测试 PostgreSQL 上的关系型存储层。
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
/// 通过 <see cref="RelationalOrleansQueries"/> 测试关系型存储层。
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
    /// 测试单条消息是否入队。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_QueuesMessage()
    {
        // 准备
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var expiryTimeout = RandomExpiryTimeout();
        var payload = RandomPayload();

        // 执行
        var before = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var after = DateTime.UtcNow.AddSeconds(1);

        // 断言 - 入队回执（ack）
        Assert.NotNull(ack);
        Assert.Equal(serviceId, ack.ServiceId);
        Assert.Equal(providerId, ack.ProviderId);
        Assert.Equal(queueId, ack.QueueId);
        Assert.Equal(1, ack.MessageId);

        // 断言 - 存储状态
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
    /// 测试在同一队列上并行将大量消息入队。
    /// </summary>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_QueuesManyMessagesInParallel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // 准备
        var serviceId = RandomServiceId();
        var providerId = RandomProviderId();
        var queueId = RandomQueueId();
        var expiryTimeout = RandomExpiryTimeout();
        var payload = RandomPayload(1000);
        var count = 10000;

        // 将请求数限制在默认连接池容量之内，避免连接超时造成测试不稳定。
        using var semaphore = new SemaphoreSlim(concurrency);

        // 执行
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

        // 断言 - 消息按顺序插入。
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

        // 断言 - 消息按预期存储
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
    /// 测试在多个队列上并行将大量消息入队。
    /// </summary>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_QueuesManyMessagesInParallelOnManyQueues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // 准备 - 以随机顺序创建最多 27 个随机分区键，每个分区包含约 1000 条随机消息
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

        // 将请求数限制在默认连接池容量之内，避免连接超时造成测试不稳定。
        using var semaphore = new SemaphoreSlim(concurrency);

        // 执行 - 并行将随机消息入队
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

        // 断言 - 所有消息均已获得入队确认
        var messageIds = new SortedSet<long>();
        foreach (var (partition, ack) in results)
        {
            Assert.Equal(ack.ServiceId, partition.ServiceId);
            Assert.Equal(ack.ProviderId, partition.ProviderId);
            Assert.Equal(ack.QueueId, partition.QueueId);
            Assert.True(messageIds.Add(ack.MessageId), $"Duplicate {ack.MessageId}");
        }

        // 断言 - 生成的消息 ID 一致
        Assert.Equal(count, messageIds.Count);
        Assert.Equal(1, messageIds.Min);
        Assert.Equal(messageIds.Count, messageIds.Max);

        // 断言 - 消息按预期存储
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
    /// 测试单条消息能否正确出队。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DequeuesSingleMessage()
    {
        // 准备
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

        // 准备：将消息加入队列。
        var beforeQueueing = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var afterQueueing = DateTime.UtcNow.AddSeconds(1);

        // 执行 - 将一条消息出队
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

        // 断言 - 消息一致
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

        // 断言 - 存储中的消息已变更
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
    /// 测试消息能否批量出队。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DequeuesMessageBatches()
    {
        // 准备
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

        // 准备 - 并发将消息入队
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

        // 执行 - 将所有消息出队
        var beforeDequeuing = DateTime.UtcNow.AddSeconds(-1);
        var first = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);
        var second = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);
        var third = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);
        var afterDequeuing = DateTime.UtcNow.AddSeconds(1);

        // 断言 - 各批次的消息数
        Assert.Equal(maxCount, first.Count);
        Assert.Equal(total - maxCount, second.Count);
        Assert.Empty(third);

        var messages = first.Concat(second).Concat(third).ToList();
        Assert.Equal(messages.OrderBy(message => message.MessageId), messages);

        // 断言 - 出队消息与入队回执一致
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

        // 断言 - 存储中的消息与出队消息一致
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
    /// 测试单条消息在可见性超时后再次出队，直到达到最大尝试次数。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DequeuesSingleMessageAgainAfterVisibilityTimeout()
    {
        // 准备
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

        // 准备：将消息加入队列。
        var beforeQueueing = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var afterQueueing = DateTime.UtcNow.AddSeconds(1);

        // 执行 - 反复将消息出队，直至最大尝试次数再加一次
        var beforeDequeuing = DateTime.UtcNow.AddSeconds(-1);
        var results = new List<IList<AdoNetStreamMessage>>();
        for (var i = 0; i < maxAttempts + 1; i++)
        {
            results.Add(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));
        }
        var afterDequeuing = DateTime.UtcNow.AddSeconds(1);

        // 断言 - 各批次符合预期
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

        // 断言 - 最后一批为空
        Assert.Empty(results[maxAttempts]);

        // 断言 - 最终存储的消息与最后一次出队的消息一致
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
    /// 测试单条消息在可见性超时之前不会再次出队。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotDequeueSingleMessageBeforeVisibilityTimeout()
    {
        // 准备
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

        // 准备：将消息加入队列。
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);

        // 执行 - 将消息出队
        var first = Assert.Single(await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize));
        var second = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 断言 - 首次出队的消息与入队回执一致
        Assert.Equal(ack.ServiceId, first.ServiceId);
        Assert.Equal(ack.ProviderId, first.ProviderId);
        Assert.Equal(ack.QueueId, first.QueueId);
        Assert.Equal(ack.MessageId, first.MessageId);

        // 断言 - 存储中的消息与首次出队的消息一致
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

        // 断言 - 消息未再次出队
        Assert.Empty(second);
    }

    /// <summary>
    /// 测试能否使用消息的出队回执将其释放，以便立即重新投递。
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
    /// 测试单条消息过期后不会再次出队。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotDequeueSingleMessageAfterExpiry()
    {
        // 准备
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

        // 准备：将消息加入队列。
        var before = DateTime.UtcNow.AddSeconds(-1);
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout);
        var after = DateTime.UtcNow.AddSeconds(1);

        // 执行 - 将消息出队
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 断言 - 没有消息出队
        Assert.Empty(messages);

        // 断言 - 存储中的消息符合预期
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
    /// 测试消息能否被确认。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_ConfirmsMessages()
    {
        // 准备
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

        // 准备 - 将大量消息入队
        var acks = await Task.WhenAll(Enumerable
            .Range(0, maxCount)
            .Select(i => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout))
            .ToList());

        // 准备 - 将所有消息出队
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 执行 - 确认所有消息
        var items = messages.Select(x => new AdoNetStreamConfirmation(x.MessageId, x.Dequeued)).ToList();
        var results = await _queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, items);

        // 断言 - 确认结果符合预期
        Assert.Equal(maxCount, acks.Length);
        Assert.Equal(maxCount, messages.Count);
        Assert.Equal(maxCount, results.Count);

        var lookup = acks.Select(x => (x.ServiceId, x.ProviderId, x.QueueId, x.MessageId)).ToHashSet();
        foreach (var result in results)
        {
            Assert.True(lookup.Remove((result.ServiceId, result.ProviderId, result.QueueId, result.MessageId)), "Unexpected Confirmation");
        }

        // 断言 - 存储中没有剩余数据
        var stored = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken);
        Assert.Empty(stored);
    }

    /// <summary>
    /// 测试回执不正确时消息不会被确认。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotConfirmMessagesWithWrongReceipt()
    {
        // 准备
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

        // 准备 - 将大量消息入队
        var acks = await Task.WhenAll(Enumerable
            .Range(0, maxCount)
            .Select(i => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout))
            .ToList());

        // 准备 - 将所有消息出队
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 执行 - 使用错误的方式确认所有消息
        var faulty = messages.Select(x => new AdoNetStreamConfirmation(x.MessageId, x.Dequeued - 1)).ToList();
        var results = await _queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, faulty);

        // 断言 - 确认结果符合预期
        Assert.Equal(maxCount, acks.Length);
        Assert.Equal(maxCount, messages.Count);
        Assert.Empty(results);

        // 断言 - 数据仍保留在存储中
        var stored = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken);
        Assert.Equal(maxCount, stored.Count());
    }

    /// <summary>
    /// 混沌测试，验证部分消息可以被确认而其他消息不被确认。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_ConfirmsSomeMessagesAndNotOthers()
    {
        // 准备
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

        // 准备 - 将大量消息入队
        var acks = await Task.WhenAll(Enumerable
            .Range(0, maxCount)
            .Select(i => _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, expiryTimeout))
            .ToList());

        // 准备 - 将所有消息出队
        var messages = await _queries.GetStreamMessagesAsync(serviceId, providerId, queueId, maxCount, maxAttempts, visibilityTimeout, removalTimeout, evictionInterval, evictionBatchSize);

        // 执行 - 随机确认部分消息
        var completed = Randomize(messages).Take(partial).Select(x => new AdoNetStreamConfirmation(x.MessageId, x.Dequeued)).ToList();
        var confirmed = await _queries.ConfirmStreamMessagesAsync(serviceId, providerId, queueId, completed);

        // 断言 - 数量符合预期
        Assert.Equal(maxCount, acks.Length);
        Assert.Equal(maxCount, messages.Count);
        Assert.Equal(partial, confirmed.Count);

        // 断言 - 已确认的消息符合预期
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

        // 断言 - 未确认的消息仍保留在存储中
        Assert.Equal(maxCount - partial, stored.Count);
        Assert.Equal(lookup.Keys.Order(), stored.Keys.Order());
    }

    /// <summary>
    /// 混沌测试，验证在复杂随机场景中入队、出队、确认和逐出能否并行工作。
    /// 此测试旨在发现并发处理的脆弱性，尤其是数据库死锁倾向，而非检查某个特定条件。
    /// 如果此测试因死锁而失败，则实现很可能存在需要调查的问题。
    /// </summary>
    /// <remarks>
    /// 在早期开发阶段，此测试曾持续引发死锁，直至底层查询得到完善。
    /// 此测试运行开销较大，但可防止查询逻辑出现回归。
    /// 特别是 MySQL，此测试还发现了驱动程序连接池本身的死锁，需要升级软件包才能解决。
    /// 参见：https://bugs.mysql.com/bug.php?id=114272
    /// </remarks>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_ChaosTest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // 准备 - 生成测试数据
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

        // 将请求数限制在默认连接池容量之内，避免连接超时造成测试不稳定。
        using var semaphore = new SemaphoreSlim(concurrency);

        // 执行 - 以混沌方式进行入队、出队和确认
        // 下列任务不要求产生预先计划的结果，但预期最终结果应保持一致性。
        var acks = new ConcurrentBag<AdoNetStreamMessageAck>();
        var dequeued1 = new ConcurrentBag<AdoNetStreamMessage>();
        var dequeued2 = new ConcurrentBag<AdoNetStreamMessage>();
        var confirmed = new ConcurrentBag<AdoNetStreamConfirmationAck>();
        await Task.WhenAll(Enumerable
            .Range(0, total)
            .Select(async i =>
            {
                // 启动一个随机入队任务
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

                // 启动一个随机出队但不进行确认的任务
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

                // 启动一个随机出队并进行确认的任务
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

                // 等待全部任务完成
                await Task.WhenAll(enqueue, dequeue, confirm);
            })
            .ToList());

        // 断言 - 所有消息均已入队
        Assert.Equal(total, acks.Count);

        // 断言 - 部分消息已出队（取决于随机数生成器，如果测试不稳定则移除此断言）
        Assert.NotEmpty(dequeued1);
        Assert.NotEmpty(dequeued2);

        // 断言 - 部分消息已确认（取决于随机数生成器，如果测试不稳定则移除此断言）
        Assert.NotEmpty(confirmed);

        // 断言 - 部分消息仍然留存（取决于随机数生成器，如果测试不稳定则移除此断言）
        var stored = await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            cancellationToken);
        Assert.NotEmpty(stored);

        // 断言 - 已确认的消息没有残留
        Assert.Empty(confirmed.IntersectBy(stored.Select(x => x.MessageId), x => x.MessageId));

        // 断言 - 所有已确认的消息均与入队回执匹配
        Assert.Empty(confirmed.ExceptBy(acks.Select(x => x.MessageId), x => x.MessageId));
    }

    /// <summary>
    /// 测试毒性消息能否移入死信队列。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_MovesPoisonedMessageToDeadLetters()
    {
        // 准备
        var serviceId = "ServiceId";
        var providerId = "ProviderId";
        var streamOptions = new AdoNetStreamOptions();
        var cacheOptions = new SimpleQueueCacheOptions();

        // 准备 - 将一条已过期的消息入队
        var queueId = "QueueId";
        var payload = new byte[] { 0xFF };

        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, streamOptions.ExpiryTimeout.TotalSecondsCeiling());

        // 准备 - 将消息出队并使其立即可再次投递
        await _queries.GetStreamMessagesAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, cacheOptions.CacheSize, streamOptions.MaxAttempts, 0, streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling(), streamOptions.EvictionInterval.TotalSecondsCeiling(), streamOptions.EvictionBatchSize);
        Assert.Empty(await _storage.ReadAsync<AdoNetStreamDeadLetter>(
            "SELECT * FROM OrleansStreamDeadLetter",
            TestContext.Current.CancellationToken));

        // 执行 - 以最大尝试次数为 1 执行清理，使上述消息被标记
        await _queries.FailStreamMessageAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, ack.MessageId, 1, streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling());

        // 断言 - 消息已不在消息表中
        Assert.Empty(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));

        // 断言 - 消息已被转移
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
    /// 测试正常消息不会被移入死信队列。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_DoesNotMoveHealthyMessageToDeadLetters()
    {
        // 准备
        var serviceId = "ServiceId";
        var providerId = "ProviderId";
        var queueId = "QueueId";
        var streamOptions = new AdoNetStreamOptions();
        var cacheOptions = new SimpleQueueCacheOptions();

        // 准备 - 将一条正常消息入队
        var payload = new byte[] { 0xFF };
        var ack = await _queries.QueueStreamMessageAsync(serviceId, providerId, queueId, payload, streamOptions.ExpiryTimeout.TotalSecondsCeiling());

        // 准备 - 将消息出队
        await _queries.GetStreamMessagesAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, cacheOptions.CacheSize, streamOptions.MaxAttempts, streamOptions.VisibilityTimeout.TotalSecondsCeiling(), streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling(), streamOptions.EvictionInterval.TotalSecondsCeiling(), streamOptions.EvictionBatchSize);

        // 执行 - 将消息标记为处理失败
        await _queries.FailStreamMessageAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, ack.MessageId, streamOptions.MaxAttempts, streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling());

        // 断言 - 消息仍在表中，并已恢复为可见状态
        var saved = Assert.Single(await _storage.ReadAsync<AdoNetStreamMessage>(
            "SELECT * FROM OrleansStreamMessage",
            TestContext.Current.CancellationToken));
        Assert.Equal(ack.ServiceId, saved.ServiceId);
        Assert.Equal(ack.ProviderId, saved.ProviderId);
        Assert.Equal(ack.QueueId, saved.QueueId);
        Assert.Equal(ack.MessageId, saved.MessageId);
        Assert.Equal(1, saved.Dequeued);
        Assert.Equal(saved.ModifiedOn, saved.VisibleOn);

        // 断言 - 没有消息进入死信队列
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
