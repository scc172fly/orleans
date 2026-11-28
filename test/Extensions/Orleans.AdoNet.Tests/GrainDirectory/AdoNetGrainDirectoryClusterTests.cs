using MySql.Data.MySqlClient;
using Npgsql;
using Orleans.TestingHost;
using Orleans.Tests.SqlUtils;
using Tester.Directories;
using UnitTests.General;
using UnitTests.Grains.Directories;
using static System.String;

namespace Tester.AdoNet.GrainDirectory;

/// <summary>
/// 针对 SQL Server 的 ADO.NET Grain 目录集群测试。
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerAdoNetGrainDirectoryClusterTests() : AdoNetGrainDirectoryClusterTests(AdoNetInvariants.InvariantNameSqlServer)
{
}

/// <summary>
/// 针对 PostgreSQL 的 ADO.NET Grain 目录集群测试。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlAdoNetGrainDirectoryClusterTests : AdoNetGrainDirectoryClusterTests
{
    public PostgreSqlAdoNetGrainDirectoryClusterTests() : base(AdoNetInvariants.InvariantNamePostgreSql)
    {
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>
/// 针对 MySQL 的 ADO.NET Grain 目录集群测试。
/// </summary>
[TestCategory("MySql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlAdoNetGrainDirectoryClusterTests : AdoNetGrainDirectoryClusterTests
{
    public MySqlAdoNetGrainDirectoryClusterTests() : base(AdoNetInvariants.InvariantNameMySql)
    {
        MySqlConnection.ClearAllPools();
    }
}

/// <summary>
/// ADO.NET Grain 目录集群测试的基类。
/// </summary>
[TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestSuite("Functional")]
[TestArea("GrainDirectory")]
public abstract class AdoNetGrainDirectoryClusterTests : MultipleGrainDirectoriesTests
{
    private const string TestDatabaseName = "OrleansGrainDirectoryTest";

    private static RelationalStorageForTesting _testing = null!;
    private static string _invariant = null!;

    public AdoNetGrainDirectoryClusterTests(string invariant)
    {
        _invariant = invariant;
    }

    public override async ValueTask InitializeAsync()
    {
        // 在基类初始化之前设置 ADO.NET 环境。
        _testing = await RelationalStorageForTesting.SetupInstance(
            _invariant,
            TestDatabaseName,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.SkipWhen(IsNullOrEmpty(_testing.CurrentConnectionString), $"Database '{TestDatabaseName}' not initialized");

        // 基类初始化必须在上述操作完成之后进行。
        await base.InitializeAsync();
        if (!PreconditionsMet)
        {
            return;
        }
    }

    public class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddAdoNetGrainDirectory(CustomDirectoryGrain.DIRECTORY, options =>
            {
                options.Invariant = _invariant;
                options.ConnectionString = _testing.CurrentConnectionString;
            });
        }
    }

    protected override void ConfigureTestCluster(TestClusterBuilder builder)
    {
        base.ConfigureTestCluster(builder);

        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
    }
}
