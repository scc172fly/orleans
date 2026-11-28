using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Orleans.Configuration;
using Orleans.Streaming.AdoNet.Storage;
using Orleans.Streams;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.General;
using UnitTests.GrainInterfaces;
using UnitTests.StreamingTests;
using static System.String;
using Xunit;

namespace Tester.AdoNet.Streaming;

/// <summary>
/// SQL Server ADO.NET 流批处理功能测试。
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerAdoNetStreamsBatchingTests(ITestOutputHelper output) : AdoNetStreamsBatchingTests(new Fixture(AdoNetInvariants.InvariantNameSqlServer), output)
{
}

/// <summary>
/// MySQL ADO.NET 流批处理功能测试。
/// </summary>
[TestCategory("MySql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlAdoNetStreamsBatchingTests : AdoNetStreamsBatchingTests
{
    public MySqlAdoNetStreamsBatchingTests(ITestOutputHelper output) : base(new Fixture(AdoNetInvariants.InvariantNameMySql), output)
    {
        MySqlConnection.ClearAllPools();
    }
}

/// <summary>
/// PostgreSQL ADO.NET 流批处理功能测试。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlAdoNetStreamsBatchingTests(ITestOutputHelper output) : AdoNetStreamsBatchingTests(new Fixture(AdoNetInvariants.InvariantNamePostgreSql), output)
{
}

/// <summary>
/// ADO.NET 流批处理测试的基类。
/// </summary>
[TestCategory("AdoNet"), TestCategory("Streaming")]
[TestSuite("Functional")]
[TestArea("Streaming")]
public abstract class AdoNetStreamsBatchingTests : StreamBatchingTestRunner, IAsyncLifetime
{
    private const string TestDatabaseName = "OrleansStreamTest";
    private static RelationalStorageForTesting _testing = null!;

    protected AdoNetStreamsBatchingTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        fixture.EnsurePreconditionsMet();
    }

    public ValueTask InitializeAsync() => fixture.InitializeAsync();

    public ValueTask DisposeAsync() => fixture.DisposeAsync();

    public class Fixture : BaseTestClusterFixture
    {
        private static string _invariant = null!;

        public Fixture(string invariant)
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

            await base.InitializeAsync();

            if (!PreconditionsMet)

            {

                return;

            }
        }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.AddSiloBuilderConfigurator<TestSiloBuilderConfigurator>();
            builder.AddClientBuilderConfigurator<TestClientBuilderConfigurator>();
        }

        private class TestSiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder
                    .AddAdoNetStreams(StreamBatchingTestConst.ProviderName, sb =>
                    {
                        sb.ConfigureAdoNet(ob => ob.Configure<IOptions<ClusterOptions>>((options, dep) =>
                        {
                            options.Invariant = _invariant;
                            options.ConnectionString = _testing.CurrentConnectionString;
                        }));
                        sb.ConfigurePullingAgent(ob => ob.Configure(options => options.BatchContainerBatchSize = 10));
                        sb.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly);
                    });
            }
        }

        private class TestClientBuilderConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder
                    .AddAdoNetStreams(StreamBatchingTestConst.ProviderName, sb =>
                    {
                        sb.ConfigureAdoNet(ob => ob.Configure<IOptions<ClusterOptions>>((options, dep) =>
                        {
                            options.Invariant = _invariant;
                            options.ConnectionString = _testing.CurrentConnectionString;
                        }));
                        sb.ConfigureStreamPubSub(StreamPubSubType.ImplicitOnly);
                    });
            }
        }
    }
}
