using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Streaming.AdoNet;
using Orleans.Streams;
using Orleans.Tests.SqlUtils;
using Tester.AdoNet.Fakes;
using TestExtensions;
using UnitTests.General;
using static System.String;

namespace Tester.AdoNet.Streaming;

/// <summary>
/// 针对 SQL Server 的 <see cref="AdoNetQueueAdapterFactory"/> 测试。
/// </summary>
[TestCategory("SqlServer"), TestCategory("BVT"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerAdoNetQueueAdapterFactoryTests(TestEnvironmentFixture fixture) : AdoNetQueueAdapterFactoryTests(AdoNetInvariants.InvariantNameSqlServer, fixture)
{
}

/// <summary>
/// 针对 MySQL 的 <see cref="AdoNetQueueAdapterFactory"/> 测试。
/// </summary>
[TestCategory("MySql"), TestCategory("BVT"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlAdoNetQueueAdapterFactoryTests : AdoNetQueueAdapterFactoryTests
{
    public MySqlAdoNetQueueAdapterFactoryTests(TestEnvironmentFixture fixture) : base(AdoNetInvariants.InvariantNameMySql, fixture)
    {
        MySqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 针对 PostgreSQL 的 <see cref="AdoNetQueueAdapterFactory"/> 测试。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("BVT"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlAdoNetQueueAdapterFactoryTests(TestEnvironmentFixture fixture) : AdoNetQueueAdapterFactoryTests(AdoNetInvariants.InvariantNamePostgreSql, fixture)
{
}

/// <summary>
/// <see cref="AdoNetQueueAdapterFactory"/> 的测试。
/// </summary>
[Collection(TestEnvironmentFixture.DefaultCollection)]
[TestCategory("AdoNet"), TestCategory("Streaming")]
[TestSuite("Functional")]
[TestArea("Streaming")]
public abstract class AdoNetQueueAdapterFactoryTests(string invariant, TestEnvironmentFixture fixture) : IAsyncLifetime
{
    private readonly TestEnvironmentFixture _fixture = fixture;
    private RelationalStorageForTesting _testing = null!;
    private IRelationalStorage _storage = null!;

    private const string TestDatabaseName = "OrleansStreamTest";

    public async ValueTask InitializeAsync()
    {
        _testing = await RelationalStorageForTesting.SetupInstance(
            invariant,
            TestDatabaseName,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.SkipWhen(IsNullOrEmpty(_testing.CurrentConnectionString), $"Database '{TestDatabaseName}' not initialized");

        _storage = _testing.Storage;
    }

    /// <summary>
    /// 测试 <see cref="AdoNetQueueAdapterFactory"/> 是否创建 <see cref="AdoNetQueueAdapter"/> 实例。
    /// </summary>
    [Fact]
    public async Task AdoNetQueueAdapterFactory_CreatesAdapter()
    {
        // 准备
        var name = "MyProviderName";
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString
        };
        var clusterOptions = new ClusterOptions
        {
            ServiceId = "MyServiceId"
        };
        var cacheOptions = new SimpleQueueCacheOptions();
        var hashOptions = new HashRingStreamQueueMapperOptions();
        var loggerFactory = NullLoggerFactory.Instance;
        var lifetime = new FakeHostApplicationLifetime();
        var serviceProvider = _fixture.Services;
        var factory = new AdoNetQueueAdapterFactory(name, streamOptions, clusterOptions, cacheOptions, hashOptions, loggerFactory, lifetime, serviceProvider);

        // 执行
        var adapter = await factory.CreateAdapter();

        // 验证
        Assert.NotNull(adapter);
        Assert.IsType<AdoNetQueueAdapter>(adapter);
        Assert.Equal(name, adapter.Name);
        Assert.False(adapter.IsRewindable);
        Assert.Equal(StreamProviderDirection.ReadWrite, adapter.Direction);
    }

    /// <summary>
    /// 测试 <see cref="AdoNetQueueAdapterFactory"/> 是否获取 <see cref="AdoNetStreamFailureHandler"/> 实例。
    /// </summary>
    [Fact]
    public async Task AdoNetQueueAdapterFactory_GetsDeliveryFailureHandler()
    {
        // 准备
        var name = "MyProviderName";
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString
        };
        var clusterOptions = new ClusterOptions
        {
            ServiceId = "MyServiceId"
        };
        var cacheOptions = new SimpleQueueCacheOptions();
        var hashOptions = new HashRingStreamQueueMapperOptions();
        var loggerFactory = NullLoggerFactory.Instance;
        var lifetime = new FakeHostApplicationLifetime();
        var serviceProvider = _fixture.Services;
        var factory = new AdoNetQueueAdapterFactory(name, streamOptions, clusterOptions, cacheOptions, hashOptions, loggerFactory, lifetime, serviceProvider);
        var queueId = QueueId.GetQueueId("MyQueueName", 1, 2);

        // 执行
        var handler = await factory.GetDeliveryFailureHandler(queueId);

        // 验证
        Assert.NotNull(handler);
        Assert.IsType<AdoNetStreamFailureHandler>(handler);
        Assert.False(handler.ShouldFaultSubsriptionOnError);
    }

    /// <summary>
    /// 测试 <see cref="AdoNetQueueAdapterFactory"/> 是否获取 <see cref="SimpleQueueCache"/> 实例。
    /// </summary>
    [Fact]
    public void AdoNetQueueAdapterFactory_GetsQueueAdapterCache()
    {
        // 准备
        var name = "MyProviderName";
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString
        };
        var clusterOptions = new ClusterOptions
        {
            ServiceId = "MyServiceId"
        };
        var cacheOptions = new SimpleQueueCacheOptions();
        var hashOptions = new HashRingStreamQueueMapperOptions();
        var loggerFactory = NullLoggerFactory.Instance;
        var lifetime = new FakeHostApplicationLifetime();
        var serviceProvider = _fixture.Services;
        var factory = new AdoNetQueueAdapterFactory(name, streamOptions, clusterOptions, cacheOptions, hashOptions, loggerFactory, lifetime, serviceProvider);

        // 执行
        var cache = factory.GetQueueAdapterCache();

        // 验证
        Assert.NotNull(cache);
        Assert.IsType<SimpleQueueAdapterCache>(cache);
    }

    /// <summary>
    /// 测试 <see cref="AdoNetQueueAdapterFactory"/> 是否获取 <see cref="HashRingBasedStreamQueueMapper"/> 实例。
    /// </summary>
    [Fact]
    public void AdoNetQueueAdapterFactory_GetsStreamQueueMapper()
    {
        // 准备
        var name = "MyProviderName";
        var streamOptions = new AdoNetStreamOptions
        {
            Invariant = invariant,
            ConnectionString = _storage.ConnectionString
        };
        var clusterOptions = new ClusterOptions
        {
            ServiceId = "MyServiceId"
        };
        var cacheOptions = new SimpleQueueCacheOptions();
        var hashOptions = new HashRingStreamQueueMapperOptions();
        var loggerFactory = NullLoggerFactory.Instance;
        var lifetime = new FakeHostApplicationLifetime();
        var serviceProvider = _fixture.Services;
        var factory = new AdoNetQueueAdapterFactory(name, streamOptions, clusterOptions, cacheOptions, hashOptions, loggerFactory, lifetime, serviceProvider);

        // 执行
        var mapper = factory.GetStreamQueueMapper();

        // 验证
        Assert.NotNull(mapper);
        Assert.IsType<HashRingBasedStreamQueueMapper>(mapper);
    }

    /// <summary>
    /// 测试 <see cref="AdoNetQueueAdapterFactory"/> 是否通过其静态工厂方法构造实例。
    /// </summary>
    [Fact]
    public void AdoNetQueueAdapterFactory_ConstructsViaStaticFactory()
    {
        // 准备
        var name = "MyProviderName";

        // 执行
        var factory = AdoNetQueueAdapterFactory.Create(_fixture.Services, name);

        // 验证
        Assert.NotNull(factory);
        Assert.IsType<AdoNetQueueAdapterFactory>(factory);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
