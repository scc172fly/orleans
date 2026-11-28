using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Streaming.AdoNet;
using Orleans.Streams;
using Orleans.Tests.SqlUtils;
using UnitTests.General;
using static System.String;
using RelationalOrleansQueries = Orleans.Streaming.AdoNet.Storage.RelationalOrleansQueries;

namespace Tester.AdoNet.Streaming;

/// <summary>
/// 针对 SQL Server 的 <see cref="AdoNetStreamFailureHandler"/> 测试。
/// </summary>
[TestCategory("SqlServer"), TestCategory("BVT"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerAdoNetStreamFailureHandlerTests() : AdoNetStreamFailureHandlerTests(AdoNetInvariants.InvariantNameSqlServer)
{
}

/// <summary>
/// 针对 MySQL 的 <see cref="AdoNetStreamFailureHandler"/> 测试。
/// </summary>
[TestCategory("MySql"), TestCategory("BVT"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlAdoNetStreamFailureHandlerTests : AdoNetStreamFailureHandlerTests
{
    public MySqlAdoNetStreamFailureHandlerTests() : base(AdoNetInvariants.InvariantNameMySql)
    {
        MySqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 针对 PostgreSQL 的 <see cref="AdoNetStreamFailureHandler"/> 测试。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("BVT"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlAdoNetStreamFailureHandlerTests() : AdoNetStreamFailureHandlerTests(AdoNetInvariants.InvariantNamePostgreSql)
{
}

/// <summary>
/// <see cref="AdoNetStreamFailureHandler"/> 的测试。
/// </summary>
[TestCategory("AdoNet"), TestCategory("Streaming")]
[TestSuite("Functional")]
[TestArea("Streaming")]
public abstract class AdoNetStreamFailureHandlerTests(string invariant) : IAsyncLifetime
{
    private RelationalStorageForTesting _testing = null!;
    private IRelationalStorage _storage = null!;
    private RelationalOrleansQueries _queries = null!;

    private const string TestDatabaseName = "OrleansStreamTest";

    public async ValueTask InitializeAsync()
    {
        _testing = await RelationalStorageForTesting.SetupInstance(
            invariant,
            TestDatabaseName,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.SkipWhen(IsNullOrEmpty(_testing.CurrentConnectionString), $"Database '{TestDatabaseName}' not initialized");

        _storage = _testing.Storage;
        _queries = await RelationalOrleansQueries.CreateInstance(invariant, _testing.CurrentConnectionString);
    }

    /// <summary>
    /// 测试能否构造 <see cref="AdoNetStreamFailureHandler"/>。
    /// </summary>
    [Fact]
    public void AdoNetStreamFailureHandler_Constructs()
    {
        // 准备
        var faultOnFailure = false;
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString
        };
        var clusterOptions = new ClusterOptions
        {
            ServiceId = "MyServiceId"
        };
        var mapper = new AdoNetStreamQueueMapper(new HashRingBasedStreamQueueMapper(new HashRingStreamQueueMapperOptions(), "MyQueuePrefix"));
        var logger = NullLogger<AdoNetStreamFailureHandler>.Instance;

        // 执行
        var positions = new AdoNetStreamPositions();
        var handler = new AdoNetStreamFailureHandler(faultOnFailure, streamOptions, clusterOptions, mapper, _queries, positions, logger);

        // 验证
        Assert.Equal(faultOnFailure, handler.ShouldFaultSubsriptionOnError);
    }

    /// <summary>
    /// 测试 <see cref="AdoNetStreamFailureHandler"/> 能否将毒性消息移入死信队列。
    /// </summary>
    [Fact]
    public async Task AdoNetStreamFailureHandler_OnDeliveryFailure_MovesPoisonedMessageToDeadLetters()
    {
        // 准备 - 处理器
        var providerId = "MyProviderId";
        var faultOnFailure = false;
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString,
            MaxAttempts = 1
        };
        var cacheOptions = new SimpleQueueCacheOptions();
        var agentOptions = new StreamPullingAgentOptions
        {
            MaxEventDeliveryTime = TimeSpan.FromSeconds(0)
        };
        var clusterOptions = new ClusterOptions
        {
            ServiceId = "MyServiceId"
        };
        var mapper = new AdoNetStreamQueueMapper(new HashRingBasedStreamQueueMapper(new HashRingStreamQueueMapperOptions(), "MyQueuePrefix"));
        var logger = NullLogger<AdoNetStreamFailureHandler>.Instance;
        var positions = new AdoNetStreamPositions();
        var handler = new AdoNetStreamFailureHandler(faultOnFailure, streamOptions, clusterOptions, mapper, _queries, positions, logger);

        // 准备 - 将一条已过期的消息入队
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var queueId = mapper.GetAdoNetQueueId(streamId);
        var payload = new byte[] { 0xFF };

        var ack = await _queries.QueueStreamMessageAsync(clusterOptions.ServiceId, providerId, queueId, payload, streamOptions.ExpiryTimeout.TotalSecondsCeiling());

        // 准备 - 将消息出队并使其立即可再次投递
        await _queries.GetStreamMessagesAsync(
            ack.ServiceId,
            ack.ProviderId,
            ack.QueueId,
            cacheOptions.CacheSize,
            streamOptions.MaxAttempts,
            agentOptions.MaxEventDeliveryTime.TotalSecondsCeiling(),
            streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling(),
            streamOptions.EvictionInterval.TotalSecondsCeiling(),
            streamOptions.EvictionBatchSize);
        Assert.Empty(await _storage.ReadAsync<AdoNetStreamDeadLetter>(
            "SELECT * FROM OrleansStreamDeadLetter",
            TestContext.Current.CancellationToken));

        // 执行 - 以最大尝试次数为 1 执行清理，使上述消息被标记
        await handler.OnDeliveryFailure(GuidId.GetNewGuidId(), providerId, streamId, new EventSequenceTokenV2(positions.Add(ack.MessageId)));

        // 验证
        var dead = Assert.Single(await _storage.ReadAsync<AdoNetStreamDeadLetter>(
            "SELECT * FROM OrleansStreamDeadLetter",
            TestContext.Current.CancellationToken));
        Assert.Equal(clusterOptions.ServiceId, dead.ServiceId);
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
    /// 测试 <see cref="AdoNetStreamFailureHandler"/> 能否将毒性消息移入死信队列。
    /// </summary>
    [Fact]
    public async Task AdoNetStreamFailureHandler_OnDeliveryFailure_DoesNotMoveHealthyMessageToDeadLetters()
    {
        // 准备 - 处理器
        var providerId = "MyProviderId";
        var faultOnFailure = false;
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString
        };
        var cacheOptions = new SimpleQueueCacheOptions();
        var agentOptions = new StreamPullingAgentOptions();
        var clusterOptions = new ClusterOptions
        {
            ServiceId = "MyServiceId"
        };
        var mapper = new AdoNetStreamQueueMapper(new HashRingBasedStreamQueueMapper(new HashRingStreamQueueMapperOptions(), "MyQueuePrefix"));
        var logger = NullLogger<AdoNetStreamFailureHandler>.Instance;
        var positions = new AdoNetStreamPositions();
        var handler = new AdoNetStreamFailureHandler(faultOnFailure, streamOptions, clusterOptions, mapper, _queries, positions, logger);

        // 准备 - 将一条已过期的消息入队
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var queueId = mapper.GetAdoNetQueueId(streamId);
        var payload = new byte[] { 0xFF };
        var ack = await _queries.QueueStreamMessageAsync(clusterOptions.ServiceId, providerId, queueId, payload, streamOptions.ExpiryTimeout.TotalSecondsCeiling());

        // 准备 - 将消息出队并使其立即可再次投递
        await _queries.GetStreamMessagesAsync(
            ack.ServiceId,
            ack.ProviderId,
            ack.QueueId,
            cacheOptions.CacheSize,
            streamOptions.MaxAttempts,
            agentOptions.MaxEventDeliveryTime.TotalSecondsCeiling(),
            streamOptions.DeadLetterEvictionTimeout.TotalSecondsCeiling(),
            streamOptions.EvictionInterval.TotalSecondsCeiling(),
            streamOptions.EvictionBatchSize);

        // 执行 - 以最大尝试次数为 1 执行清理，使上述消息被标记
        await handler.OnDeliveryFailure(GuidId.GetNewGuidId(), providerId, streamId, new EventSequenceTokenV2(positions.Add(ack.MessageId)));

        // 验证
        Assert.Empty(await _storage.ReadAsync<AdoNetStreamDeadLetter>(
            "SELECT * FROM OrleansStreamDeadLetter",
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 测试 <see cref="AdoNetStreamFailureHandler"/> 在无法再解析消息位置时是否保持该消息不变。
    /// </summary>
    [Fact]
    public async Task AdoNetStreamFailureHandler_OnDeliveryFailure_SkipsRemovedPosition()
    {
        var providerId = $"Provider-{Guid.NewGuid()}";
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString,
            MaxAttempts = 1
        };
        var clusterOptions = new ClusterOptions { ServiceId = $"Service-{Guid.NewGuid()}" };
        var mapper = new AdoNetStreamQueueMapper(new HashRingBasedStreamQueueMapper(new HashRingStreamQueueMapperOptions(), "MyQueuePrefix"));
        var positions = new AdoNetStreamPositions();
        var handler = new AdoNetStreamFailureHandler(false, streamOptions, clusterOptions, mapper, _queries, positions, NullLogger<AdoNetStreamFailureHandler>.Instance);
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var queueId = mapper.GetAdoNetQueueId(streamId);
        var ack = await _queries.QueueStreamMessageAsync(clusterOptions.ServiceId, providerId, queueId, [0xFF], streamOptions.ExpiryTimeout.TotalSecondsCeiling());
        await _queries.GetStreamMessagesAsync(ack.ServiceId, ack.ProviderId, ack.QueueId, 1, streamOptions.MaxAttempts, 0, 600, 600, 0);
        var position = positions.Add(ack.MessageId);
        positions.Remove(position);

        await handler.OnDeliveryFailure(GuidId.GetNewGuidId(), providerId, streamId, new EventSequenceTokenV2(position));

        Assert.DoesNotContain(
            await _storage.ReadAsync<AdoNetStreamDeadLetter>("SELECT * FROM OrleansStreamDeadLetter", TestContext.Current.CancellationToken),
            dead => dead.ServiceId == clusterOptions.ServiceId);
        Assert.Contains(
            await _storage.ReadAsync<AdoNetStreamMessage>("SELECT * FROM OrleansStreamMessage", TestContext.Current.CancellationToken),
            message => message.ServiceId == clusterOptions.ServiceId && message.MessageId == ack.MessageId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
