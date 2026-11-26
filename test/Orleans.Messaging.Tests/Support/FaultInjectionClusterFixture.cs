using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Support;

/// <summary>
/// 信息传递群组的发送电话、日记、持久工作日程安排和持久工作钟都
/// 测试控制下,在输送链的每个边界注射确定性断层。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Deliveries"/> 将错误输入发件箱呼叫接收器的调用; <see cref="MessagingClusterFixture.Storage"/>
/// 无法或持有日记在时间长于或超过时间长于该日记本的前后写作; <see cref="MessagingClusterFixture.JobManagerProbe"/>
/// 或保持提醒时间安排。 <see cref="JobClock"/> 驱动发件箱的回溯和过期时间以及预定时间重新尝试
/// 交付时间, 并且仅在测试前移动 。
/// </para>
/// <para>
/// 持久就业点燃了当同时安排时应出现的警醒,但晚晚应到的警醒则实际等待
/// 时间; <see cref="RetryInterval"/> 足够长,使这样的唤醒在试验中不会起火。推进后
/// <see cref="JobClock"/>,测试将泵清晰运行到 <see cref="AdvanceAndRunPumpAsync"/>。
/// </para>
/// <para>
/// <see cref="Configuration.DurableMessagingOptions.InboxRetryTimeout"/> 保持零为零,因此处理器取消
/// 被错误地算作失败的解除功能 将会同时失去意义
/// </para>
/// </remarks>
public class FaultInjectionClusterFixture(string journalFormatKey = "orleans-binary") : MessagingClusterFixture(1, receiverOnly: false)
{
    /// <summary>第一个发件箱重试回转, 与每次尝试的次尝试翻倍。</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    /// <summary>发件箱多久重写信息 之前它用死字来写信息。</summary>
    public static readonly TimeSpan OutboxRetryTimeout = TimeSpan.FromHours(1);

    /// <summary>提供发箱和定期交货使用的“持久工作钟”。</summary>
    public ManualJobClock JobClock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>获取发件箱呼叫接收器时错误的注射器。</summary>
    public DeliveryFaultInjector Deliveries { get; } = new();

    /// <summary>获取 <see cref="Functional.SourceSinkGrain"/> 激活的探测器。</summary>
    public Functional.SourceSinkProbe SourceSinkProbe { get; } = new();

    /// <summary>预付款 <see cref="JobClock"/> 年 <paramref name="delta"/>,然后运行一个泵通行证 <paramref name="grain"/>.</summary>
    public async Task<DurableJobRunResult> AdvanceAndRunPumpAsync(IAddressable grain, TimeSpan delta)
    {
        JobClock.Advance(delta);
        return await RunPumpAsync(grain);
    }

    /// <summary>等待 <paramref name="sender"/> 的发件箱记录了 <paramref name="attempts"/> 尝试 <paramref name="messageId"/>失败。</summary>
    internal Task WaitForFailedAttemptsAsync(IAddressable sender, MessageId messageId, int attempts) =>
        WaitUntilAsync(sender, () =>
            GetOutboxState(sender) is var state
            && state.AttemptKeys.Contains(messageId)
            && state.Attempt(messageId).AttemptCount >= attempts);

    /// <summary>获得测试访问活动 <paramref name="grain"/>的发箱状态。</summary>
    internal OutboxStateTestAccess GetOutboxState(IAddressable grain) =>
        new(GetGrainContext(grain).ActivationServices.GetRequiredService<IOutbox>());

    /// <summary>
    /// 取消 <paramref name="grain"/> ,因为通信失败,取消了航班内交付和
    /// 处理器,等待直到启动器消失
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
