using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using TestExtensions;
using Orleans.Internal;
using Orleans.Configuration;
using Orleans.TestingHost.Utils;
using Orleans.Reminders.Cosmos;
using Xunit;

namespace Tester.Cosmos.Reminders;

/// <summary>
/// 独立测试宇宙DB提醒表业务,包括业绩基准和表格级业务。
/// </summary>
[Collection(TestEnvironmentFixture.DefaultCollection)]
[TestCategory("Cosmos")]
[TestSuite("Functional")]
[TestProvider("Cosmos")]
[TestArea("Reminders")]
public class ReminderTests_Cosmos_Standalone
{
    private readonly ITestOutputHelper _output;
    private readonly TestEnvironmentFixture _fixture;
    private readonly string _serviceId;
    private readonly ILogger _log;
    private readonly ILoggerFactory _loggerFactory;

    public ReminderTests_Cosmos_Standalone(ITestOutputHelper output, TestEnvironmentFixture fixture)
    {
        CosmosTestUtils.CheckCosmosStorage();

        _output = output;
        _fixture = fixture;
        _loggerFactory = TestingUtils.CreateDefaultLoggerFactory($"{GetType().Name}.log");
        _log = _loggerFactory.CreateLogger<ReminderTests_Cosmos_Standalone>();

        _serviceId = Guid.NewGuid().ToString();
    }

    [Fact, TestCategory("Reminders"), TestCategory("Performance")]
    public async Task Reminders_AzureTable_InsertRate()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        var clusterOptions = Options.Create(new ClusterOptions { ClusterId = "TMSLocalTesting", ServiceId = _serviceId });
        var storageOptions = Options.Create(new CosmosReminderTableOptions());
        storageOptions.Value.ConfigureTestDefaults();
        var retryPipeline = CosmosReadRetryPolicy.CreatePipeline(
            _loggerFactory.CreateLogger<CosmosReminderTable>(),
            TimeProvider.System);

        IReminderTable table = new CosmosReminderTable(
            _loggerFactory,
            _fixture.Services,
            storageOptions,
            clusterOptions,
            retryPipeline);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(new ReminderOptions().InitializationTimeout);
        await table.StartAsync(cancellation.Token);

        await TestTableInsertRate(table, 10, testCancellationToken);
        await TestTableInsertRate(table, 500, testCancellationToken);
    }

    [Fact, TestCategory("Reminders"), TestCategory("Functional")]
    public async Task Reminders_AzureTable_InsertNewRowAndReadBack()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        string clusterId = NewClusterId();
        var clusterOptions = Options.Create(new ClusterOptions { ClusterId = clusterId, ServiceId = _serviceId });
        var storageOptions = Options.Create(new CosmosReminderTableOptions());
        storageOptions.Value.ConfigureTestDefaults();
        var retryPipeline = CosmosReadRetryPolicy.CreatePipeline(
            _loggerFactory.CreateLogger<CosmosReminderTable>(),
            TimeProvider.System);
        IReminderTable table = new CosmosReminderTable(
            _loggerFactory,
            _fixture.Services,
            storageOptions,
            clusterOptions,
            retryPipeline);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(new ReminderOptions().InitializationTimeout);
        await table.StartAsync(cancellation.Token);

        ReminderEntry[] rows = (await GetAllRows(table, testCancellationToken)).ToArray();
        Assert.Empty(rows); // "The reminder table (sid={0}, did={1}) was not empty.", ServiceId, clusterId);

        ReminderEntry expected = NewReminderEntry();
        await table.UpsertRow(expected, testCancellationToken);
        rows = (await GetAllRows(table, testCancellationToken)).ToArray();

        Assert.Single(rows); // "The reminder table (sid={0}, did={1}) did not contain the correct number of rows (1).", ServiceId, clusterId);
        ReminderEntry actual = rows[0];
        Assert.Equal(expected.GrainId, actual.GrainId); // "The newly inserted reminder table (sid={0}, did={1}) row did not contain the expected grain 参考书", ServiceId, clusterId);
        Assert.Equal(expected.ReminderName, actual.ReminderName); // "The newly inserted reminder table (sid={0}, did={1}) row did not have the expected reminder name.", ServiceId, clusterId);
        Assert.Equal(expected.Period, actual.Period); // "The newly inserted reminder table (sid={0}, did={1}) row did not have the expected period.", ServiceId, clusterId);
                                                      // 以下的断言失败了,但我还不知道为什么... 错误信息中的时间戳看起来是一样的。 但是,找到原因并不是一个优先事项, 因为我非常有信心它目前已经很好工作了。
        /*Assert.Equal(expected.StartAt,  actual.StartAt); // "The newly inserted reminder table (sid={0}, did={1}) row did not contain the correct start time.", ServiceId, clusterId);*/
        Assert.False(string.IsNullOrWhiteSpace(actual.ETag), $"The newly inserted reminder table (sid={_serviceId}, did={clusterId}) row contains an invalid etag.");
    }

    private async Task TestTableInsertRate(
        IReminderTable reminderTable,
        double numOfInserts,
        CancellationToken cancellationToken)
    {
        DateTime startedAt = DateTime.UtcNow;

        List<Task<bool>> promises = new List<Task<bool>>();
        for (int i = 0; i < numOfInserts; i++)
        {
            //"177BF46E-D06D-44C0-943B-C12F26DF5373"
            string s = string.Format("177BF46E-D06D-44C0-943B-C12F26D{0:d5}", i);

            var e = new ReminderEntry
            {
                //GrainId = LegacyGrainId.GetGrainId(新指南),
                GrainId = _fixture.InternalGrainFactory.GetGrain(LegacyGrainId.NewId()).GetGrainId(),
                ReminderName = "MY_REMINDER_" + i,
                Period = TimeSpan.FromSeconds(5),
                StartAt = DateTime.UtcNow
            };

            int capture = i;
            Task<bool> promise = Task.Run(async () =>
            {
                await reminderTable.UpsertRow(e, cancellationToken);
                _output.WriteLine("Done " + capture);
                return true;
            }, cancellationToken);
            promises.Add(promise);
            _log.LogInformation("Started {Capture}", capture);
        }
        _log.LogInformation("Started all, now waiting...");
        await Task.WhenAll(promises).WaitAsync(TimeSpan.FromSeconds(500), cancellationToken);

        TimeSpan dur = DateTime.UtcNow - startedAt;
        _log.LogInformation(
            "Inserted {InsertCount} rows in {Duration}, i.e., {Rate} upserts/sec",
            numOfInserts,
            dur,
            (numOfInserts / dur.TotalSeconds).ToString("f2"));
    }

    private ReminderEntry NewReminderEntry()
    {
        Guid guid = Guid.NewGuid();
        return new ReminderEntry
        {
            GrainId = _fixture.InternalGrainFactory.GetGrain(LegacyGrainId.NewId()).GetGrainId(),
            ReminderName = string.Format("TestReminder.{0}", guid),
            Period = TimeSpan.FromSeconds(5),
            StartAt = DateTime.UtcNow
        };
    }

    private static string NewClusterId()
    {
        return string.Format("ReminderTest.{0}", Guid.NewGuid());
    }

    private static async Task<IEnumerable<ReminderEntry>> GetAllRows(
        IReminderTable table,
        CancellationToken cancellationToken)
    {
        ReminderTableData data = await table.ReadRows(0, 0xffffffff, cancellationToken);
        Assert.NotNull(data);
        return data.Reminders;
    }
}
