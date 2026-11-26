using MySql.Data.MySqlClient;
using Npgsql;
using Orleans.GrainDirectory.AdoNet;
using Orleans.GrainDirectory.AdoNet.Storage;
using UnitTests.General;
using static System.String;

namespace Tester.AdoNet.GrainDirectory;

/// <summary>
/// 通过 Sql 服务器测试 <see cref="RelationalOrleansQueries"/> 的关联存储层。
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerRelationalOrleansQueriesTests() : RelationalOrleansQueriesTests(AdoNetInvariants.InvariantNameSqlServer, 90)
{
}

/// <summary>
/// 通过 <see cref="RelationalOrleansQueries"/> 测试 PostgreSQL的关联储存层。
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
/// 通过 <see cref="RelationalOrleansQueries"/> 测试 MySQL的关联储存层。
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
/// 通过 <see cref="RelationalOrleansQueries"/>测试相关储存层。
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
    /// 测试 grain 激活是否被注册。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_RegistersActivation()
    {
        // 安排
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();
        var grainId = RandomGrainId();
        var siloAddress = RandomSiloAddress();
        var activationId = RandomActivationId();

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 行为
        var entry = await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);

        // 平面
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
    /// 测试 grain 激活是否未注册。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_UnregistersActivation()
    {
        // 安排
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();
        var grainId = RandomGrainId();
        var siloAddress = RandomSiloAddress();
        var activationId = RandomActivationId();

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 行为
        var entry = await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);
        var count = await _queries.UnregisterGrainActivationAsync(clusterId, providerId, grainId, activationId);

        // 平面
        Assert.NotNull(entry);
        Assert.Equal(1, count);

        var results = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Empty(results);
    }

    /// <summary>
    /// 测试 grain 激活可被检查。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_LooksUpActivation()
    {
        // 安排
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();
        var grainId = RandomGrainId();
        var siloAddress = RandomSiloAddress();
        var activationId = RandomActivationId();

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 行为
        var entry = await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);
        var result = await _queries.LookupGrainActivationAsync(clusterId, providerId, grainId);

        // 平面
        Assert.NotNull(entry);
        Assert.NotNull(result);
        Assert.Equal(clusterId, result.ClusterId);
        Assert.Equal(providerId, result.ProviderId);
        Assert.Equal(grainId, result.GrainId);
        Assert.Equal(siloAddress, result.SiloAddress);
        Assert.Equal(activationId, result.ActivationId);
    }

    /// <summary>
    /// 测试 grain 一组 silos 未经登记。
    /// </summary>
    [Fact]
    public async Task RelationalOrleansQueries_UnregistersActivationsForSilos()
    {
        // 安排
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

        // 行为
        var count = await _queries.UnregisterGrainActivationsAsync(clusterId, providerId, "A|C|E");

        // 平面
        Assert.Equal(3, count);

        var remaining = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Collection(remaining.OrderBy(x => x.GrainId),
            entry => Assert.Equal("G2", entry.GrainId),
            entry => Assert.Equal("G4", entry.GrainId));
    }

    /// <summary>
    /// 测试同时查询 grain 目录时的混乱。
    /// </summary>
    /// <remarks>
    /// 这一测试旨在研究是否容易出现数据库僵局和其他同货币问题。
    /// 在开发过程中,这一试验一直引发僵局,直到提出问题以防止它发生。
    /// 如果测试显示不耐用, 则可能需要查看查询 。
    /// </remarks>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task RelationalOrleansQueries_ChaosTest()
    {
        var clusterId = RandomClusterId();
        var providerId = RandomProviderId();

        // 行为
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

            // 模拟生命周期,包括不稳定的重叠操作
            await _queries.RegisterGrainActivationAsync(clusterId, providerId, grainId, siloAddress, activationId);
            for (var i = 0; i < randomLookups; i++)
            {
                await _queries.LookupGrainActivationAsync(clusterId, providerId, grainId);
            }
            await _queries.UnregisterGrainActivationAsync(clusterId, providerId, grainId, activationId);
        });

        // 平面
        var remaining = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Empty(remaining);
    }
}
