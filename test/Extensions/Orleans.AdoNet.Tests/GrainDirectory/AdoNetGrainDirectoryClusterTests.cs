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
/// 集束试验 ADO.NET Grain 对照 SQL 服务器
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("GrainDirectory")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerAdoNetGrainDirectoryClusterTests() : AdoNetGrainDirectoryClusterTests(AdoNetInvariants.InvariantNameSqlServer)
{
}

/// <summary>
/// ADO.NET Grain 目录对 PostgreSQL的集束测试。
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
/// ADO.NET Grain 目录对 MySQL的集束测试。
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
/// ADO.NET Grain 目录的集束测试基类。
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
        // 在基初始化前设置 adonet 环境
        _testing = await RelationalStorageForTesting.SetupInstance(
            _invariant,
            TestDatabaseName,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.SkipWhen(IsNullOrEmpty(_testing.CurrentConnectionString), $"Database '{TestDatabaseName}' not initialized");

        // 初始化必须在上述时间之后才开始
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
