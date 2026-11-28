using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>
/// 一个消息传递集群，其投递调用、日志写入、Durable Jobs 调度及 Durable Jobs 时钟均
/// 受测试控制，以便在投递链各边界确定性地注入故障。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Deliveries"/> 向发件箱对接收方的调用注入故障；<see cref="MessagingClusterFixture.Storage"/>
/// 在日志写入持久化之前或之后使其失败或阻塞；<see cref="MessagingClusterFixture.JobManagerProbe"/>
/// 使唤醒调度失败或阻塞。<see cref="JobClock"/> 控制发件箱的重试退避、过期及计划
/// 投递时间，并仅在测试显式推进时变化。
/// </para>
/// <para>
/// Durable Jobs 会立即触发调度时已到期的唤醒，但较晚到期的唤醒会按实际
/// 时间等待；<see cref="RetryInterval"/> 足够长，确保此类唤醒在测试期间不会触发。推进
/// <see cref="JobClock"/> 后，测试通过 <see cref="AdvanceAndRunPumpAsync"/> 显式运行消息泵。
/// </para>
/// <para>
/// <see cref="Configuration.DurableMessagingOptions.InboxRetryTimeout"/> 始终为零，因此如果因
/// 停用而取消的处理程序被错误计为失败，便会立即转为死信。
/// </para>
/// </remarks>
public class FaultInjectionClusterFixture(string journalFormatKey = "orleans-binary") : MessagingClusterFixture(1, receiverOnly: false)
{
    /// <summary>发件箱首次重试的退避时间，每次后续尝试都会翻倍。</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    /// <summary>发件箱在将消息转为死信之前对其进行重试的时长。</summary>
    public static readonly TimeSpan OutboxRetryTimeout = TimeSpan.FromHours(1);

    /// <summary>获取发件箱及计划投递所使用的 Durable Jobs 时钟。</summary>
    public ManualJobClock JobClock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>获取发件箱对接收方调用的故障注入器。</summary>
    public DeliveryFaultInjector Deliveries { get; } = new();

    /// <summary>获取用于 <see cref="Functional.SourceSinkGrain"/> 激活实例的探针。</summary>
    public Functional.SourceSinkProbe SourceSinkProbe { get; } = new();

    /// <summary>将 <see cref="JobClock"/> 推进 <paramref name="delta"/>，然后为 <paramref name="grain"/> 运行一轮消息泵处理。</summary>
    public async Task<DurableJobRunResult> AdvanceAndRunPumpAsync(IAddressable grain, TimeSpan delta)
    {
        JobClock.Advance(delta);
        return await RunPumpAsync(grain);
    }

    /// <summary>等待 <paramref name="sender"/> 的发件箱记录 <paramref name="attempts"/> 次针对 <paramref name="messageId"/> 的失败尝试。</summary>
    internal Task WaitForFailedAttemptsAsync(IAddressable sender, MessageId messageId, int attempts) =>
        WaitUntilAsync(sender, () =>
            GetOutboxState(sender) is var state
            && state.AttemptKeys.Contains(messageId)
            && state.Attempt(messageId).AttemptCount >= attempts);

    /// <summary>获取对活动 <paramref name="grain"/> 发件箱状态的测试访问入口。</summary>
    internal OutboxStateTestAccess GetOutboxState(IAddressable grain) =>
        new(GetGrainContext(grain).ActivationServices.GetRequiredService<IOutbox>());

    /// <summary>
    /// 按消息传递失败时的方式停用 <paramref name="grain"/>，取消其进行中的投递和
    /// 处理程序，并等待激活实例消失。
    /// </summary>
    public async Task CrashAsync(IAddressable grain)
    {
        var context = GetGrainContext(grain);
        await OnGrainAsync(grain, () => context.Deactivate(
            new DeactivationReason(DeactivationReasonCode.ApplicationError, "Injected activation crash.")));
        await context.Deactivated.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddKeyedSingleton<TimeProvider>(DurableJobTimeProviderNames.DurableJobs, JobClock);
        services.AddSingleton(SourceSinkProbe);
        services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = journalFormatKey);
        services.Configure<JsonJournalOptions>(options => options.AddTypeInfoResolver(Functional.SourceSinkJsonContext.Default));
        services.AddSingleton<IOutboxTransport>(sp => Deliveries.Wrap(new GrainOutboxTransport(sp.GetRequiredService<IGrainFactory>())));
    }

    protected override void ConfigureOptions(DurableMessagingOptions options)
    {
        base.ConfigureOptions(options);
        options.MaxCapacity = 8;
        options.DeduplicationWindow = TimeSpan.FromDays(1);
        options.OutboxRetryTimeout = OutboxRetryTimeout;
        options.RetryInitialInterval = RetryInterval;
    }
}
