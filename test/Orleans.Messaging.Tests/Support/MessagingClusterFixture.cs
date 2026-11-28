using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Orleans.Configuration;
using Orleans.Durable.Jobs;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Configuration;
using Orleans.TestingHost;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>
/// 承载进程内消息传递测试集群。
/// </summary>
/// <remarks>
/// 测试夹具在所有派生类构造函数运行之后，于 <see cref="InitializeAsync"/> 中构建并部署集群，因此
/// <see cref="ConfigureServices"/> 和 <see cref="ConfigureOptions"/> 的重写实现观察到的是完整构造后的派生类状态。
/// </remarks>
public class MessagingClusterFixture : IAsyncLifetime
{
    private readonly int _initialSilos;
    private readonly bool _receiverOnly;
    private readonly bool _useServiceCollection;
    private InProcessTestCluster? _cluster;

    public MessagingClusterFixture() : this(receiverOnly: true)
    {
    }

    public MessagingClusterFixture(bool receiverOnly) : this(1, receiverOnly)
    {
    }

    protected MessagingClusterFixture(int initialSilos, bool receiverOnly = false, bool useServiceCollection = false)
    {
        Clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Storage = new ControlledJournalStorageProvider();
        Metrics = new MessagingMetricProbe();
        JobManagerProbe = new DurableJobManagerProbe();
        HandlerProbe = new HandlerProbe();
        SnapshotProbe = new SnapshotProbe();
        ActivationProbe = new ActivationValidationProbe();
        _initialSilos = initialSilos;
        _receiverOnly = receiverOnly;
        _useServiceCollection = useServiceCollection;
    }

    public InProcessTestCluster Cluster =>
        _cluster ?? throw new InvalidOperationException($"The cluster is available after {nameof(InitializeAsync)} builds it.");

    public IClusterClient Client => Cluster.Client!;
    public DateTimeOffset JobNow => Cluster.Silos[0].ServiceProvider
        .GetRequiredKeyedService<TimeProvider>(DurableJobTimeProviderNames.DurableJobs).GetUtcNow();
    public FakeTimeProvider Clock { get; }
    public ControlledJournalStorageProvider Storage { get; }
    public MessagingMetricProbe Metrics { get; }
    public DurableJobManagerProbe JobManagerProbe { get; }
    public HandlerProbe HandlerProbe { get; }
    public SnapshotProbe SnapshotProbe { get; }
    public ActivationValidationProbe ActivationProbe { get; }

    private InProcessTestCluster BuildCluster()
    {
        var clusterId = $"messaging-{Guid.NewGuid():N}";
        var serviceId = $"messaging-service-{Guid.NewGuid():N}";
        var builder = new InProcessTestClusterBuilder((short)_initialSilos);
        builder.ConfigureClient(clientBuilder =>
            clientBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            }));
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            });
            siloBuilder.Services.AddSingleton<TimeProvider>(Clock);
            siloBuilder.Services.UseTimeProviderForBackgroundAreas(TimeProvider.System);
            // 推进测试时钟也会推进 Durable Jobs 时钟，收件箱的重试截止时间以该时钟为准。
            siloBuilder.Services.AddKeyedSingleton<TimeProvider>(
                DurableJobTimeProviderNames.DurableJobs,
                new FollowingJobClock(Clock, Clock.GetUtcNow()));
            siloBuilder.Services.AddSingleton(HandlerProbe);
            siloBuilder.Services.AddSingleton(new TestHandlerConfiguration());
            siloBuilder.Services.AddSingleton(SnapshotProbe);
            siloBuilder.Services.AddSingleton(ActivationProbe);
            siloBuilder.UseInMemoryDurableJobs();
            // 测试 Grain 将使用 Orleans 序列化的类型记入日志。
            siloBuilder.Services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = "orleans-binary");
            if (_receiverOnly)
            {
                ReceiverTestServices.Add(siloBuilder.Services, ConfigureOptions);
            }
            else if (_useServiceCollection)
            {
                siloBuilder.Services.AddMessaging(ConfigureOptions);
            }
            else
            {
                siloBuilder.AddMessaging(ConfigureOptions);
            }
            siloBuilder.ConfigureServices(services =>
                ControlledDurableJobManager.Decorate(services, JobManagerProbe));
            siloBuilder.Services.RemoveAll<IJournalStorageProvider>();
            siloBuilder.Services.RemoveAll<IJournalStorageCatalog>();
            siloBuilder.Services.AddSingleton(Storage);
            siloBuilder.Services.AddSingleton<IJournalStorageProvider>(serviceProvider =>
            {
                Storage.Configure(serviceProvider.GetRequiredService<IOptions<JournaledStateManagerOptions>>());
                return Storage;
            });
            siloBuilder.Services.AddSingleton<IJournalStorageCatalog>(
                serviceProvider => (IJournalStorageCatalog)serviceProvider.GetRequiredService<IJournalStorageProvider>());
            siloBuilder.Services.UseDocumentationPaymentGrain();
            ConfigureServices(siloBuilder.Services);
        });
        return builder.Build();
    }

    /// <summary>
    /// 使 Durable Jobs 时钟读取 <see cref="Clock"/>，因此重试截止时间是精确的测试时钟时间。Durable Jobs 的
    /// 定时器仍以实际时间为准，在测试时钟达到相应到期时间后执行各次唤醒。
    /// </summary>
    protected void UseTestClockForDurableJobs(IServiceCollection services) =>
        services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs, new TestJobClock(Clock));

    /// <summary>一个 Durable Jobs 时钟，其时间等于实际时间加上测试夹具启动以来测试时钟的推进量。</summary>
    internal sealed class FollowingJobClock(TimeProvider clock, DateTimeOffset clockStart) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + (clock.GetUtcNow() - clockStart);
    }

    private sealed class TestJobClock(TimeProvider clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
    }

    public Task<EndpointSnapshot> WaitForEffectCountAsync(IMessagingTestGrain grain, int expected) =>
        SnapshotProbe.WaitAsync(
            grain.GetGrainId(),
            snapshot => snapshot.Effects.Sum(static effect => effect.Count) >= expected);

    public Task<EndpointSnapshot> WaitForInboxCountAsync(IMessagingTestGrain grain, int expected) =>
        SnapshotProbe.WaitAsync(grain.GetGrainId(), snapshot => snapshot.InboxCount == expected);

    public Task<EndpointSnapshot> WaitForDeadLetterCountAsync(IMessagingTestGrain grain, int expected) =>
        SnapshotProbe.WaitAsync(
            grain.GetGrainId(),
            snapshot => snapshot.InboxDeadLetters.Count + snapshot.OutboxDeadLetters.Count >= expected);

    public Task<EndpointSnapshot> WaitForOutboxCountAsync(IMessagingTestGrain grain, int expected) =>
        SnapshotProbe.WaitAsync(grain.GetGrainId(), snapshot => snapshot.OutboxCount == expected);

    /// <summary>获取集群中各 Silo 通过发件箱深度指标报告的发件箱深度。</summary>
    public long GetOutboxDepth() =>
        Cluster.Silos.Sum(silo => Metrics.GetDepth(
            silo.ServiceProvider.GetRequiredService<OrleansInstruments>().Meter,
            "orleans-messaging-outbox-depth"));

    /// <summary>等待集群各 Silo 报告的发件箱深度总和等于 <paramref name="expected"/>。</summary>
    public Task WaitForOutboxDepthAsync(long expected) =>
        Metrics.WaitUntilAsync(() => GetOutboxDepth() == expected, TestContext.Current.CancellationToken);

    public EndpointSnapshot GetSnapshot(IMessagingTestGrain grain) =>
        GetGrainInstance(grain).GetSnapshotForTest();

    public OutboxMessage[] GetStagedOutput(IMessagingTestGrain grain) =>
        GetGrainContext(grain).ActivationServices.GetRequiredService<IOutbox>().Messages().ToArray();

    public ValueTask WriteStateAsync(IMessagingTestGrain grain) =>
        GetGrainContext(grain).ActivationServices
            .GetRequiredService<IJournaledStateManager>()
            .WriteStateAsync(TestContext.Current.CancellationToken);

    internal Inbox GetInbox(IAddressable grain) =>
        GetGrainContext(grain).ActivationServices.GetRequiredService<Inbox>();

    internal MessagingPump GetPump(IAddressable grain) =>
        GetGrainContext(grain).ActivationServices.GetRequiredService<MessagingPump>();

    /// <summary>
    /// 调度一次额外的消息泵唤醒。唤醒是至少一次的提示，因此额外唤醒必须无害。
    /// </summary>
    public async Task WakeAsync(IAddressable grain)
    {
        var services = Cluster.TryGetGrainContext(grain.GetGrainId(), out var context)
            ? context.ActivationServices
            : Cluster.Silos[0].ServiceProvider;
        await services.GetRequiredService<ILocalDurableJobManager>().ScheduleJobAsync(
            new ScheduleJobRequest
            {
                Target = grain.GetGrainId(),
                JobName = MessagingPump.JobName,
                DueTime = DateTimeOffset.UtcNow
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// 以当前唤醒作业的身份直接执行消息泵的持久化作业回调，模拟该作业提前触发或
    /// 虚假触发。
    /// </summary>
    internal Task<DurableJobRunResult> RunPumpAsync(IAddressable grain) =>
        RunCancelablePumpAsync(grain, TestContext.Current.CancellationToken);

    /// <summary>以不同于当前唤醒作业的作业身份直接执行消息泵的持久化作业回调。</summary>
    internal Task<DurableJobRunResult> RunDuplicatePumpAsync(IAddressable grain) =>
        RunPumpAsync(grain, TestContext.Current.CancellationToken, duplicate: true);

    /// <summary>
    /// 直接执行消息泵的持久化作业回调，以 <paramref name="runCancellation"/> 作为此次运行的令牌，从而使
    /// 测试能够像 Durable Jobs 一样取消该次运行。
    /// </summary>
    internal Task<DurableJobRunResult> RunCancelablePumpAsync(IAddressable grain, CancellationToken runCancellation) =>
        RunPumpAsync(grain, runCancellation, duplicate: false);

    private async Task<DurableJobRunResult> RunPumpAsync(IAddressable grain, CancellationToken runCancellation, bool duplicate)
    {
        var pump = GetPump(grain);
        var run = await OnGrainAsync(grain, () =>
        {
            var job = new DurableJob
            {
                Id = (duplicate ? null : InboxTestAccess.WakeupJobId(pump)) ?? Guid.NewGuid().ToString("N"),
                Name = MessagingPump.JobName,
                ShardId = "test",
                TargetGrainId = grain.GetGrainId(),
                DueTime = DateTimeOffset.UtcNow
            };
            return pump.ExecuteJobAsync(new PumpRunContext(job), runCancellation).AsTask();
        });
        return await run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    /// <summary>在 Grain 的激活实例调度器上运行 <paramref name="func"/>。</summary>
    internal Task<T> OnGrainAsync<T>(IAddressable grain, Func<T> func)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        GetGrainContext(grain).Scheduler.QueueAction(() =>
        {
            try
            {
                done.SetResult(func());
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
        return done.Task;
    }

    internal Task OnGrainAsync(IAddressable grain, Action action) =>
        OnGrainAsync(grain, () =>
        {
            action();
            return true;
        });

    /// <summary>在 Grain 的调度器上轮询 <paramref name="predicate"/>，直至条件成立。</summary>
    internal async Task WaitUntilAsync(IAddressable grain, Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!await OnGrainAsync(grain, predicate))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition was not met for grain '{grain.GetGrainId()}'.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>强制进行空闲激活实例回收，直至 <paramref name="context"/> 停用。</summary>
    internal async Task CollectAsync(IGrainContext context)
    {
        var management = Client.GetGrain<IManagementGrain>(0);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!context.Deactivated.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Activation '{context.GrainId}' was not collected.");
            }

            await management.ForceActivationCollection(TimeSpan.Zero, TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    internal IGrainContext GetGrainContext(IAddressable grain)
    {
        if (!Cluster.TryGetGrainContext(grain.GetGrainId(), out var context))
        {
            throw new InvalidOperationException($"Grain '{grain.GetGrainId()}' is not active.");
        }

        return context;
    }

    private MessagingTestGrain GetGrainInstance(IMessagingTestGrain grain) =>
        GetGrainContext(grain).GrainInstance as MessagingTestGrain
        ?? throw new InvalidOperationException($"Grain '{grain.GetGrainId()}' has an unexpected implementation.");

    private sealed class PumpRunContext(DurableJob job) : IJobRunContext
    {
        public DurableJob Job { get; } = job;
        public string RunId { get; } = Guid.NewGuid().ToString("N");
        public int DequeueCount => 1;
    }

    protected virtual void ConfigureServices(IServiceCollection services) { }

    protected virtual void ConfigureOptions(DurableMessagingOptions options)
    {
        options.MaxCapacity = 2;
        options.DeduplicationWindow = TimeSpan.FromMinutes(10);
        options.OutboxRetryTimeout = TimeSpan.FromSeconds(3);
        options.InboxRetryTimeout = TimeSpan.Zero;
        options.MaxRetainedDeadLetters = 2;
        options.DeadLetterRetentionPeriod = TimeSpan.FromHours(1);
        options.RetryInitialInterval = TimeSpan.FromMilliseconds(25);
        options.InboxBatchSize = 8;
        options.OutboxBatchSize = 8;
    }

    public ValueTask InitializeAsync()
    {
        if (_cluster is not null) throw new InvalidOperationException("The cluster is already initialized.");
        _cluster = BuildCluster();
        return new(_cluster.DeployAsync());
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_cluster is not null) await _cluster.DisposeAsync();
        Metrics.Dispose();
    }
}

public sealed class MultiSiloMessagingClusterFixture : MessagingClusterFixture
{
    public MultiSiloMessagingClusterFixture() : base(2)
    {
    }
}

public sealed class DedupeExpiryClusterFixture : MessagingClusterFixture
{
}

public sealed class InboxCapacityClusterFixture : MessagingClusterFixture
{
    protected override void ConfigureOptions(DurableMessagingOptions options)
    {
        base.ConfigureOptions(options);
        options.MaxCapacity = 1;
        options.InboxRetryTimeout = TimeSpan.FromHours(1);
        options.RetryInitialInterval = TimeSpan.FromHours(1);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        UseTestClockForDurableJobs(services);
    }
}
