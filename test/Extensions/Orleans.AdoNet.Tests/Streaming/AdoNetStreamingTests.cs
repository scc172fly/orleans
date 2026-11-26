using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Orleans.Runtime;
using Orleans.Streaming.AdoNet.Storage;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.General;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using UnitTests.Streaming;
using UnitTests.StreamingTests;
using static System.String;

namespace Tester.AdoNet.Streaming;

/// <summary>
/// 用于以下目的的集群流测试 ADO.NET 与 SQL 服务器
/// </summary>
[TestCategory("SqlServer"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("SqlServer")]
[TestSuite("Functional")]
public class SqlServerAdoNetStreamingTests() : AdoNetStreamingTests(AdoNetInvariants.InvariantNameSqlServer)
{
}

/// <summary>
/// ADO.NET 流对 MySQL的分组流测试。
/// </summary>
[TestCategory("MySql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("MySql")]
[TestSuite("Functional")]
public class MySqlAdoNetStreamingTests : AdoNetStreamingTests
{
    public MySqlAdoNetStreamingTests() : base(AdoNetInvariants.InvariantNameMySql)
    {
        MySqlConnection.ClearAllPools();
    }
}

/// <summary>
/// ADO.NET 流对 PostgreSQL的分组流测试。
/// </summary>
[TestCategory("PostgreSql"), TestCategory("Functional"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestProvider("PostgreSql")]
[TestSuite("Functional")]
public class PostgreSqlAdoNetStreamingTests() : AdoNetStreamingTests(AdoNetInvariants.InvariantNamePostgreSql)
{
}

/// <summary>
/// ADO.NET 流的集群流测试。
/// </summary>
[TestCategory("AdoNet"), TestCategory("Streaming")]
[TestSuite("Functional")]
[TestArea("Streaming")]
public abstract class AdoNetStreamingTests : TestClusterPerTest
{
    private const string TestDatabaseName = "OrleansStreamTest";
    private const string AdoNetStreamProviderName = "AdoNet";
    private static readonly TimeSpan StreamingDiagnosticTimeout = TimeSpan.FromSeconds(30);

    private static string _invariant = null!;

    protected AdoNetStreamingTests(string invariant)
    {
        _invariant = invariant;
        RelationalStorageForTesting.CheckPreconditionsOrThrow(_invariant);
    }

    private static RelationalStorageForTesting _testing = null!;
    private SingleStreamTestRunner _runner = null!;

    public override async ValueTask InitializeAsync()
    {
        // 在基初始化前设置 adonet 环境
        var cancellationToken = TestContext.Current.CancellationToken;
        _testing = await RelationalStorageForTesting.SetupInstance(
            _invariant,
            TestDatabaseName,
            cancellationToken: cancellationToken);

        Assert.SkipWhen(IsNullOrEmpty(_testing.CurrentConnectionString), $"Database '{TestDatabaseName}' not initialized");

        // 初始化必须在上述时间之后才开始
        await base.InitializeAsync();
        if (!PreconditionsMet)
        {
            return;
        }
        await WaitForStreamingProviderReadyAsync(cancellationToken);

        // 仅必须在基初始化后创建运行者
        _runner = new SingleStreamTestRunner(HostedCluster, AdoNetStreamProviderName);
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
                .AddAdoNetStreams(AdoNetStreamProviderName, options =>
                {
                    options.Invariant = _invariant;
                    options.ConnectionString = _testing.CurrentConnectionString;
                })
                .AddMemoryGrainStorage("MemoryStore")
                .AddMemoryGrainStorage("PubSubStore");

            siloBuilder.Services.AddSingleton<StreamingDiagnosticEventRecorder>();
            siloBuilder.Services.AddSingleton<StreamingDiagnosticsProbeSystemTarget>();
            siloBuilder.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(serviceProvider => serviceProvider.GetRequiredService<StreamingDiagnosticsProbeSystemTarget>());
            siloBuilder.AddStartupTask<StreamingDiagnosticEventRecorder>(ServiceLifecycleStage.RuntimeInitialize);
        }
    }

    private class TestClientBuilderConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddAdoNetStreams(AdoNetStreamProviderName, options =>
            {
                options.Invariant = _invariant;
                options.ConnectionString = _testing.CurrentConnectionString;
            });
        }
    }

    private async Task WaitForStreamingProviderReadyAsync(CancellationToken cancellationToken)
    {
        var activeSilos = HostedCluster.GetActiveSilos().Select(static silo => silo.SiloAddress).ToArray();
        var grainFactory = (IInternalGrainFactory)GrainFactory;
        var waits = activeSilos.Select(siloAddress =>
            grainFactory.GetSystemTarget<IStreamingDiagnosticsProbe>(
                StreamingDiagnosticsProbeConstants.SystemTargetType,
                siloAddress)
            .WaitForProviderReady(AdoNetStreamProviderName, StreamingDiagnosticTimeout))
            .ToArray();

        await Task.WhenAll(waits).WaitAsync(cancellationToken);
    }

    //------------------------ One to One -----------------------------------------------------//

    [Fact, TestCategory("Functional")]
    public Task AdoNet_01_OneProducerGrainOneConsumerGrain() => _runner.StreamTest_01_OneProducerGrainOneConsumerGrain(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_02_OneProducerGrainOneConsumerClient() => _runner.StreamTest_02_OneProducerGrainOneConsumerClient(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_03_OneProducerClientOneConsumerGrain() => _runner.StreamTest_03_OneProducerClientOneConsumerGrain(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_04_OneProducerClientOneConsumerClient() => _runner.StreamTest_04_OneProducerClientOneConsumerClient(TestContext.Current.CancellationToken);

    //和很多不同的人 grains ----------------------------------//

    [Fact, TestCategory("Functional")]
    public Task AdoNet_05_ManyDifferent_ManyProducerGrainsManyConsumerGrains() => _runner.StreamTest_05_ManyDifferent_ManyProducerGrainsManyConsumerGrains(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_06_ManyDifferent_ManyProducerGrainManyConsumerClients() => _runner.StreamTest_06_ManyDifferent_ManyProducerGrainManyConsumerClients(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_07_ManyDifferent_ManyProducerClientsManyConsumerGrains() => _runner.StreamTest_07_ManyDifferent_ManyProducerClientsManyConsumerGrains(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_08_ManyDifferent_ManyProducerClientsManyConsumerClients() => _runner.StreamTest_08_ManyDifferent_ManyProducerClientsManyConsumerClients(TestContext.Current.CancellationToken);

    //和... 和很多相同 grains ---------------------------------------//

    [Fact, TestCategory("Functional")]
    public Task AdoNet_09_ManySame_ManyProducerGrainsManyConsumerGrains() => _runner.StreamTest_09_ManySame_ManyProducerGrainsManyConsumerGrains(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_10_ManySame_ManyConsumerGrainsManyProducerGrains() => _runner.StreamTest_10_ManySame_ManyConsumerGrainsManyProducerGrains(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_11_ManySame_ManyProducerGrainsManyConsumerClients() => _runner.StreamTest_11_ManySame_ManyProducerGrainsManyConsumerClients(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_12_ManySame_ManyProducerClientsManyConsumerGrains() => _runner.StreamTest_12_ManySame_ManyProducerClientsManyConsumerGrains(TestContext.Current.CancellationToken);

    //------------------------ MANY to Many producer consumer same grain ----------------------//

    [Fact, TestCategory("Functional")]
    public Task AdoNet_13_SameGrain_ConsumerFirstProducerLater() => _runner.StreamTest_13_SameGrain_ConsumerFirstProducerLater(false, TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public Task AdoNet_14_SameGrain_ProducerFirstConsumerLater() => _runner.StreamTest_14_SameGrain_ProducerFirstConsumerLater(false, TestContext.Current.CancellationToken);

    //-----------------------------------------------------------------------------------------//

    [Fact, TestCategory("Functional")]
    public Task AdoNet_15_ConsumeAtProducersRequest() => _runner.StreamTest_15_ConsumeAtProducersRequest(TestContext.Current.CancellationToken);

    [Fact, TestCategory("Functional")]
    public async Task AdoNet_16_MultipleStreams_ManyDifferent_ManyProducerGrainsManyConsumerGrains()
    {
        var multiRunner = new MultipleStreamsTestRunner(HostedCluster, AdoNetStreamProviderName, 16, false);

        await multiRunner.StreamTest_MultipleStreams_ManyDifferent_ManyProducerGrainsManyConsumerGrains(
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact, TestCategory("Functional")]
    public async Task AdoNet_17_MultipleStreams_1J_ManyProducerGrainsManyConsumerGrains()
    {
        var multiRunner = new MultipleStreamsTestRunner(HostedCluster, AdoNetStreamProviderName, 17, false);

        await multiRunner.StreamTest_MultipleStreams_ManyDifferent_ManyProducerGrainsManyConsumerGrains(
            () => HostedCluster.StartAdditionalSilo(),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
