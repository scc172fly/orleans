using MySql.Data.MySqlClient;
using Npgsql;
using Orleans.GrainDirectory.AdoNet;
using Orleans.GrainDirectory.AdoNet.Storage;
using UnitTests.General;
using static System.String;

namespace Tester.AdoNet.GrainDirectory;

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/> 针对 SQL Server 测试关系存储层。
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerRelationalOrleansQueriesTests() : RelationalOrleansQueriesTests(AdoNetInvariants.InvariantNameSqlServer, 90)
{
}

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/> 针对 PostgreSQL 测试关系存储层。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlRelationalOrleansQueriesTests : RelationalOrleansQueriesTests
{
    public PostgreSqlRelationalOrleansQueriesTests() : base(AdoNetInvariants.InvariantNamePostgreSql, 90)
    {
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/> 针对 MySQL 测试关系存储层。
/// </summary>
[TestCategory("MySql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlRelationalOrleansQueriesTests : RelationalOrleansQueriesTests
{
    public MySqlRelationalOrleansQueriesTests() : base(AdoNetInvariants.InvariantNameMySql, 20)
    {
        MySqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/> 测试关系存储层。
/// </summary>
[TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestSuite("Functional")]
[TestArea("GrainDirectory")]
public abstract class RelationalOrleansQueriesTests(string invariant, int concurrency = 100) : IAsyncLifetime
{
    private const string TestDatabaseName = "OrleansGrainDirectoryTest";

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

    private static string RandomClusterId(int max = 10) => $"ClusterId{Random.Shared.Next(max)}";

    private static string RandomProviderId(int max = 10) => $"ProviderId{Random.Shared.Next(max)}";

    private static string RandomGrainId(int max = 10) => $"GrainId{Random.Shared.Next(max)}";

    private static string RandomSiloAddress(int max = 10) => $"SiloAddress{Random.Shared.Next(max)}";

    private static string RandomActivationId(int max = 10) => $"ActivationId{Random.Shared.Next(max)}";

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// 验证 Grain 激活实例能够注册。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_RegistersActivation()
    {
        // 准备
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();
        var grainId = RandomGrainId();
        var siloAddress = RandomSiloAddress();
        var activationId = RandomActivationId();

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 执行
        var entry = await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);

        // 验证
        Assert.NotNull(entry);
        Assert.Equal(clusterId, entry.ClusterId);
        Assert.Equal(providerId, entry.ProviderId);
        Assert.Equal(grainId, entry.GrainId);
        Assert.Equal(siloAddress, entry.SiloAddress);
        Assert.Equal(activationId, entry.ActivationId);

        var results = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        var result = Assert.Single(results);
        Assert.Equal(clusterId, result.ClusterId);
        Assert.Equal(providerId, result.ProviderId);
        Assert.Equal(grainId, result.GrainId);
        Assert.Equal(siloAddress, result.SiloAddress);
        Assert.Equal(activationId, result.ActivationId);
    }

    /// <summary>
    /// 验证 Grain 激活实例能够注销。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_UnregistersActivation()
    {
        // 准备
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();
        var grainId = RandomGrainId();
        var siloAddress = RandomSiloAddress();
        var activationId = RandomActivationId();

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 执行
        var entry = await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);
        var count = await _queries.UnregisterGrainActivationAsync(clusterId, providerId, grainId, activationId);

        // 验证
        Assert.NotNull(entry);
        Assert.Equal(1, count);

        var results = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Empty(results);
    }

    /// <summary>
    /// 验证能够查找 Grain 激活实例。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_LooksUpActivation()
    {
        // 准备
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();
        var grainId = RandomGrainId();
        var siloAddress = RandomSiloAddress();
        var activationId = RandomActivationId();

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 执行
        var entry = await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);
        var result = await _queries.LookupGrainActivationAsync(clusterId, providerId, grainId);

        // 验证
        Assert.NotNull(entry);
        Assert.NotNull(result);
        Assert.Equal(clusterId, result.ClusterId);
        Assert.Equal(providerId, result.ProviderId);
        Assert.Equal(grainId, result.GrainId);
        Assert.Equal(siloAddress, result.SiloAddress);
        Assert.Equal(activationId, result.ActivationId);
    }

    /// <summary>
    /// 验证一组 Silo 上的 Grain 激活实例能够注销。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_UnregistersActivationsForSilos()
    {
        // 准备
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();
        var grainId = RandomGrainId();
        var activationId = RandomActivationId();

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        await _queries.RegisterGrainActivationAsync(clusterId, providerId, "G1", "A", "A1");
        await _queries.RegisterGrainActivationAsync(clusterId, providerId, "G2", "B", "A2");
        await _queries.RegisterGrainActivationAsync(clusterId, providerId, "G3", "C", "A3");
        await _queries.RegisterGrainActivationAsync(clusterId, providerId, "G4", "D", "A4");
        await _queries.RegisterGrainActivationAsync(clusterId, providerId, "G5", "E", "A5");

        // 执行
        var count = await _queries.UnregisterGrainActivationsAsync(clusterId, providerId, "A|C|E");

        // 验证
        Assert.Equal(3, count);

        var remaining = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Collection(remaining.OrderBy(x => x.GrainId),
            entry => Assert.Equal("G2", entry.GrainId),
            entry => Assert.Equal("G4", entry.GrainId));
    }

    /// <summary>
    /// 针对并发 Grain 目录查询的混沌测试。
    /// </summary>
    /// <remarks>
    /// 此测试检查是否容易发生数据库死锁及其他并发问题。
    /// 开发期间，此测试持续触发死锁，直到查询经过修改以防止死锁。
    /// 如果此测试结果不稳定，很可能需要检查查询语句。
    /// </remarks>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_ChaosTest()
    {
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();

        // 执行
        await Parallel.ForAsync(0, 10000, new ParallelOptions
        {
            MaxDegreeOfParallelism = concurrency,
            CancellationToken = TestContext.Current.CancellationToken
        }, async (x, ct) =>
        {
            var grainId = RandomGrainId();
            var siloAddress = RandomSiloAddress();
            var activationId = RandomActivationId();
            var randomLookups = Random.Shared.Next(10);

            // 模拟包含不稳定的重叠操作的生命周期
            await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);
            for (var i = 0; i < randomLookups; i++)
            {
                await _queries.LookupGrainActivationAsync(clusterId, providerId, grainId);
            }
            await _queries.UnregisterGrainActivationAsync(clusterId, providerId, grainId, activationId);
        });

        // 验证
        var remaining = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Empty(remaining);
    }
}
