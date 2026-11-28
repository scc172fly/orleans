using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Npgsql;
using Orleans.Configuration;
using Orleans.GrainDirectory.AdoNet;
using Orleans.Tests.SqlUtils;
using Tester.AdoNet.Fakes;
using TestExtensions;
using UnitTests.General;
using static System.String;

namespace Tester.AdoNet.GrainDirectory;

/// <summary>
/// 针对 SQL Server 的 <see cref="AdoNetGrainDirectory"/> 测试。
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerAdoNetGrainDirectoryTests() : AdoNetGrainDirectoryTests(AdoNetInvariants.InvariantNameSqlServer, 90)
{
}

/// <summary>
/// 针对 PostgreSQL 的 <see cref="AdoNetGrainDirectory"/> 测试。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlAdoNetGrainDirectoryTests : AdoNetGrainDirectoryTests
{
    public PostgreSqlAdoNetGrainDirectoryTests() : base(AdoNetInvariants.InvariantNamePostgreSql, 90)
    {
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 针对 MySQL 的 <see cref="AdoNetGrainDirectory"/> 测试。
/// </summary>
[TestCategory("MySql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlAdoNetGrainDirectoryTests : AdoNetGrainDirectoryTests
{
    public MySqlAdoNetGrainDirectoryTests() : base(AdoNetInvariants.InvariantNameMySql, 20)
    {
        MySqlConnection.ClearAllPools();
    }
}

/// <summary>
/// <see cref="AdoNetGrainDirectory"/> 的测试。
/// </summary>
[Collection(TestEnvironmentFixture.DefaultCollection)]
[TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestSuite("Functional")]
[TestArea("GrainDirectory")]
public abstract class AdoNetGrainDirectoryTests(string invariant, int concurrency = 100) : IAsyncLifetime
{
    private RelationalStorageForTesting _testing = null!;
    private IRelationalStorage _storage = null!;

    private const string TestDatabaseName = "OrleansGrainDirectoryTest";

    public async ValueTask InitializeAsync()
    {
        _testing = await RelationalStorageForTesting.SetupInstance(
            invariant,
            TestDatabaseName,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.SkipWhen(IsNullOrEmpty(_testing.CurrentConnectionString), $"Database '{TestDatabaseName}' not initialized");

        _storage = _testing.Storage;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// 验证 Grain 激活实例能够注册。
    /// </summary>
    [Fact]
    public async Task AdoNetGrainDirectory_RegistersActivation()
    {
        // 准备
        var options = new AdoNetGrainDirectoryOptions
        {
            Invariant = invariant,
            ConnectionString = _testing.CurrentConnectionString
        };
        var logger = NullLogger<AdoNetGrainDirectory>.Instance;
        var clusterOptions = Options.Create(new ClusterOptions
        {
            ClusterId = "MyClusterId"
        });
        var lifetime = new FakeHostApplicationLifetime();
        var directory = new AdoNetGrainDirectory("MyProviderId", options, logger, clusterOptions, lifetime);

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 执行
        var address = new GrainAddress
        {
            GrainId = GrainId.Create("MyGrainType", "MyGrainKey"),
            SiloAddress = SiloAddress.New(IPEndPoint.Parse("127.0.0.1:11111"), 123456),
            ActivationId = ActivationId.NewId()
        };
        var result = await directory.Register(address);

        // 验证
        Assert.NotNull(result);
        Assert.Equal(address.GrainId, result.GrainId);
        Assert.Equal(address.SiloAddress, result.SiloAddress);
        Assert.Equal(address.ActivationId, result.ActivationId);

        var saved = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        var entry = Assert.Single(saved);
        Assert.Equal("MyClusterId", entry.ClusterId);
        Assert.Equal("MyProviderId", entry.ProviderId);
        Assert.Equal(address.GrainId.ToString(), entry.GrainId);
        Assert.Equal(address.SiloAddress.ToParsableString(), entry.SiloAddress);
        Assert.Equal(address.ActivationId.ToParsableString(), entry.ActivationId);
    }

    /// <summary>
    /// 验证 Grain 激活实例能够注销。
    /// </summary>
    [Fact]
    public async Task AdoNetGrainDirectory_UnregistersActivation()
    {
        // 准备
        var options = new AdoNetGrainDirectoryOptions
        {
            Invariant = invariant,
            ConnectionString = _testing.CurrentConnectionString
        };
        var logger = NullLogger<AdoNetGrainDirectory>.Instance;
        var clusterOptions = Options.Create(new ClusterOptions
        {
            ClusterId = "MyClusterId"
        });
        var lifetime = new FakeHostApplicationLifetime();
        var directory = new AdoNetGrainDirectory("MyProviderId", options, logger, clusterOptions, lifetime);

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        var address = new GrainAddress
        {
            GrainId = GrainId.Create("MyGrainType", "MyGrainKey"),
            SiloAddress = SiloAddress.New(IPEndPoint.Parse("127.0.0.1:11111"), 123456),
            ActivationId = ActivationId.NewId()
        };
        await directory.Register(address);

        // 执行
        await directory.Unregister(address);

        // 验证
        var saved = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Empty(saved);
    }

    /// <summary>
    /// 验证能够查找 Grain 激活实例。
    /// </summary>
    [Fact]
    public async Task AdoNetGrainDirectory_LooksUpActivation()
    {
        // 准备
        var options = new AdoNetGrainDirectoryOptions
        {
            Invariant = invariant,
            ConnectionString = _testing.CurrentConnectionString
        };
        var logger = NullLogger<AdoNetGrainDirectory>.Instance;
        var clusterOptions = Options.Create(new ClusterOptions
        {
            ClusterId = "MyClusterId"
        });
        var lifetime = new FakeHostApplicationLifetime();
        var directory = new AdoNetGrainDirectory("MyProviderId", options, logger, clusterOptions, lifetime);

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        var grainId = GrainId.Create("MyGrainType", "MyGrainKey");
        var siloAddress = SiloAddress.New(IPEndPoint.Parse("127.0.0.1:11111"), 123456);
        var activationId = ActivationId.NewId();
        var address = new GrainAddress
        {
            GrainId = grainId,
            SiloAddress = siloAddress,
            ActivationId = activationId
        };
        await directory.Register(address);

        // 执行
        var result = await directory.Lookup(grainId);

        // 验证
        Assert.NotNull(result);
        Assert.Equal(grainId, result.GrainId);
        Assert.Equal(siloAddress, result.SiloAddress);
        Assert.Equal(activationId, result.ActivationId);
    }

    /// <summary>
    /// 验证能够注销一组 Silo 上的 Grain 激活实例。
    /// </summary>
    [Fact]
    public async Task AdoNetGrainDirectory_UnregistersActivationsForSilos()
    {
        // 准备
        var options = new AdoNetGrainDirectoryOptions
        {
            Invariant = invariant,
            ConnectionString = _testing.CurrentConnectionString
        };
        var logger = NullLogger<AdoNetGrainDirectory>.Instance;
        var clusterOptions = Options.Create(new ClusterOptions
        {
            ClusterId = "MyClusterId"
        });
        var lifetime = new FakeHostApplicationLifetime();
        var directory = new AdoNetGrainDirectory("MyProviderId", options, logger, clusterOptions, lifetime);

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        var grainIds = Enumerable.Range(0, 5).Select(i => GrainId.Create("MyGrainType", $"MyGrainKey{i}")).ToArray();
        var siloAddresses = Enumerable.Range(0, 5).Select(i => SiloAddress.New(IPEndPoint.Parse($"127.0.0.{i}:11111"), 123456)).ToArray();
        var activationIds = Enumerable.Range(0, 5).Select(_ => ActivationId.NewId()).ToArray();
        var addresses = Enumerable.Range(0, 5).Select(i => new GrainAddress
        {
            GrainId = grainIds[i],
            SiloAddress = siloAddresses[i],
            ActivationId = activationIds[i]
        }).ToArray();

        for (var i = 0; i < 5; i++)
        {
            await directory.Register(addresses[i]);
        }

        // 执行
        await directory.UnregisterSilos([siloAddresses[0], siloAddresses[2], siloAddresses[4]]);

        // 验证
        var results = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory ORDER BY GrainId", TestContext.Current.CancellationToken);
        var resultAddresses = results.Select(entry => entry.ToGrainAddress()).ToArray();
        Assert.Equal([addresses[1], addresses[3]], resultAddresses);
    }

    /// <summary>
    /// 验证能够查找 Grain 激活实例。
    /// </summary>
    [TestSuite("Stress")]
    [TestCategory("Stress")]
    [Fact]
    public async Task AdoNetGrainDirectory_ChaosTest()
    {
        // 准备
        var options = new AdoNetGrainDirectoryOptions
        {
            Invariant = invariant,
            ConnectionString = _testing.CurrentConnectionString
        };
        var logger = NullLogger<AdoNetGrainDirectory>.Instance;
        var clusterOptions = Options.Create(new ClusterOptions
        {
            ClusterId = "MyClusterId"
        });
        var lifetime = new FakeHostApplicationLifetime();
        var directory = new AdoNetGrainDirectory("MyProviderId", options, logger, clusterOptions, lifetime);

        await _storage.ExecuteAsync("DELETE FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);

        // 执行
        await Parallel.ForAsync(0, 10000, new ParallelOptions
        {
            MaxDegreeOfParallelism = concurrency,
            CancellationToken = TestContext.Current.CancellationToken
        }, async (i, ct) =>
        {
            var grainId = GrainId.Create("MyGrainType", $"MyGrainKey{Random.Shared.Next(10)}");
            var siloAddress = SiloAddress.New(IPEndPoint.Parse($"127.0.0.{Random.Shared.Next(10)}:11111"), 123456);
            var activationId = ActivationId.NewId();
            var address = new GrainAddress
            {
                GrainId = grainId,
                SiloAddress = siloAddress,
                ActivationId = activationId
            };
            var lookups = Random.Shared.Next(10);

            // 模拟包含不稳定的重叠操作的生命周期
            var registered = await directory.Register(address);
            for (var j = 0; j < lookups; j++)
            {
                var result = await directory.Lookup(grainId);
            }

            // 注册可能因其他并发操作而失败
            if (registered is not null)
            {
                await directory.Unregister(registered);
            }
        });

        // 验证
        var remaining = await _storage.ReadAsync<AdoNetGrainDirectoryEntry>("SELECT * FROM OrleansGrainDirectory", TestContext.Current.CancellationToken);
        Assert.Empty(remaining);
    }
}
