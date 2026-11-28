using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Orleans.Diagnostics;
using Orleans.Durable.Jobs;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests;

/// <summary>
/// 验证持久化作业的调度、执行、处理和持久化均共享
/// 原始调用者 Activity 的 W3C 跟踪上下文。这些测试验证端到端的跟踪
/// 连续性，Aspire Dashboard 的持久化作业视图依赖于此。
/// </summary>
public class DurableJobsTracingTests : IClassFixture<DurableJobsTracingTests.Fixture>
{
    private static readonly ConcurrentBag<Activity> CapturedActivities = new();
    private static readonly ActivityListener Listener;
    private static readonly ActivitySource TestSource = new("DurableJobsTracingTests");

    static DurableJobsTracingTests()
    {
        Listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == ActivitySources.DurableJobsActivitySourceName
                || source.Name == ActivitySources.ApplicationGrainActivitySourceName
                || source.Name == ActivitySources.RuntimeActivitySourceName
                || source.Name == ActivitySources.LifecycleActivitySourceName
                || source.Name == ActivitySources.StorageActivitySourceName
                || source.Name == TestSource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                var _ = options.TraceId;
                return ActivitySamplingResult.AllDataAndRecorded;
            },
            SampleUsingParentId = static (ref ActivityCreationOptions<string> options) =>
            {
                var _ = options.TraceId;
                return ActivitySamplingResult.AllDataAndRecorded;
            },
            ActivityStopped = static activity => CapturedActivities.Add(activity)
        };

        ActivitySource.AddActivityListener(Listener);
    }

    /// <summary>
    /// 与 <see cref="DefaultClusterFixture"/> 相同的集群测试夹具，但额外启用
    /// Activity 传播，使调度时的跟踪上下文通过接收方 Grain RPC 传播。
    /// </summary>
    public class Fixture : BaseTestClusterFixture
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            builder.AddClientBuilderConfigurator<ClientConfigurator>();
        }

        private class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder) =>
                hostBuilder
                    .AddActivityPropagation()
                    .UseInMemoryReminderService()
                    .UseInMemoryDurableJobs()
                    .AddMemoryGrainStorageAsDefault()
                    .AddMemoryGrainStorage("MemoryStore");
        }

        private class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
                clientBuilder.AddActivityPropagation();
        }
    }

    private readonly Fixture _fixture;

    public DurableJobsTracingTests(Fixture fixture)
    {
        _fixture = fixture;
        CapturedActivities.Clear();
    }

    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("DurableJobs")]
    [Fact, TestCategory("BVT"), TestCategory("DurableJobs")]
    public async Task ScheduleAndExecuteShareTraceId()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;

        var grain = _fixture.GrainFactory.GetGrain<IDurableJobGrain>(Guid.NewGuid().ToString());

        string expectedTraceId;
        DurableJob job;
        using (var parentActivity = TestSource.StartActivity("durablejobs.tracing.parent", ActivityKind.Internal))
        {
            Assert.NotNull(parentActivity);
            expectedTraceId = parentActivity.TraceId.ToString();

            job = await grain.ScheduleJobAsync(
                jobName: "tracing-test-job",
                scheduledTime: DateTimeOffset.UtcNow.AddMilliseconds(250));
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(testCancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(1));
        await grain.WaitForJobToRun(job.Id).WaitAsync(cts.Token);

        Assert.False(string.IsNullOrEmpty(job.TraceParent), "TraceParent should be persisted on the scheduled job.");
        Assert.Contains(expectedTraceId, job.TraceParent);

        var handlerTraceId = await grain.GetJobTraceId(job.Id);
        Assert.Equal(expectedTraceId, handlerTraceId);

        // 采集快照前，等待执行后的后续存储写入（RemoveJob）结束。
        await Task.Delay(250, testCancellationToken);

        var traceActivities = CapturedActivities
            .Where(a => a.TraceId.ToString() == expectedTraceId)
            .ToArray();

        Assert.Contains(traceActivities, a => a.Source.Name == ActivitySources.DurableJobsActivitySourceName
            && a.OperationName == "schedule durable job");
        Assert.Contains(traceActivities, a => a.Source.Name == ActivitySources.DurableJobsActivitySourceName
            && a.OperationName == "execute durable job");
        Assert.Contains(traceActivities, a => a.Source.Name == ActivitySources.DurableJobsActivitySourceName
            && a.OperationName == "execute durable job handler");
        Assert.Contains(traceActivities, a => a.Source.Name == ActivitySources.StorageActivitySourceName
            && a.OperationName.StartsWith("journal ", StringComparison.Ordinal));
    }
}
