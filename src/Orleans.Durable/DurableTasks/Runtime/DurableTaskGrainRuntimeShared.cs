using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Durable.Jobs;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks.Runtime;

internal sealed class DurableTaskGrainRuntimeShared(
    IGrainContextAccessor grainContextAccessor,
    [FromKeyedServices(DurableJobTimeProviderNames.DurableJobs)] TimeProvider timeProvider,
    ILogger<DurableTaskGrainRuntime> logger,
    IOptions<DurableTaskOptions> options,
    IOptions<DurableMessagingOptions> messagingOptions,
    Serializer serializer)
{
    public IGrainContextAccessor GrainContextAccessor { get; } = grainContextAccessor;
    /// <summary>
    /// 获取 Durable Jobs 的时钟，Messaging 将 <c>Envelope.DeliverAt</c> 和发件箱重试时间与该时钟进行比较。逻辑
    /// 时间、延迟到期时间及任务时间戳使用此时钟，因此它们与消息调度使用同一时间域。
    /// </summary>
    public TimeProvider TimeProvider { get; } = timeProvider;
    public ILogger<DurableTaskGrainRuntime> Logger { get; } = logger;
    public DurableTaskOptions Options { get; } = options.Value;

    /// <summary>
    /// 获取自发送方首次暂存调用起，允许调用准入的时长，即 Messaging 的 <c>DeduplicationWindow</c>。
    /// </summary>
    /// <remarks>
    /// Messaging 要求 <c>OutboxRetryTimeout</c> 更短，因此其重试能够完成的投递会被准入。
    /// 因背压或投递管道的阻滞而超出窗口的调用会转为死信。
    /// </remarks>
    public TimeSpan InvocationAdmissionWindow { get; } = messagingOptions.Value.DeduplicationWindow;

    /// <summary>
    /// 获取先于调用到达的取消或信号在到达后被保留记录的时长，即
    /// <see cref="InvocationAdmissionWindow"/> 的两倍。因此，对于发送方在该消息之前暂存且被准入的调用，无论
    /// 发送方与本 Grain 的时钟偏差如何，都可以找到该记录。
    /// </summary>
    /// <remarks>
    /// 准入将发送方时钟上的调用发送时间与本 Grain 的时钟比较，而记录的
    /// 生存期从本 Grain 的时钟开始计算。Messaging 拒绝发送时间比接收方时钟超前超过
    /// <c>DeduplicationWindow</c> 的投递，因此该记录的消息（发送方暂存它的时间不早于
    /// 调用）是在其到达时间加一个窗口之前发送的；而调用只有在此之后另一个窗口内才会被
    /// 准入。
    /// </remarks>
    public TimeSpan EarlyRecordRetention { get; } = 2 * messagingOptions.Value.DeduplicationWindow;

    public Serializer Serializer { get; } = serializer;
}
