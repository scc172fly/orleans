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
    /// 获得可持久的工作钟,而通信则比较 <c>Envelope.DeliverAt</c> 并发送框重试时间。
    /// 时间、延迟到期时间和任务时间戳使用它,所以它们共享一个时间域与信件列表。
    /// </summary>
    public TimeProvider TimeProvider { get; } = timeProvider;
    public ILogger<DurableTaskGrainRuntime> Logger { get; } = logger;
    public DurableTaskOptions Options { get; } = options.Value;

    /// <summary>
    /// 收到发件人首次提出援引请求后多久才开始被接受: <c>DeduplicationWindow</c>.
    /// </summary>
    /// <remarks>
    /// 通信要求 <c>OutboxRetryTimeout</c> 的长度较短,因此允许其重试完成的交货。
    /// 反压或输油管挡在窗口的反压或输送管道上,是用字条写成的。
    /// </remarks>
    public TimeSpan InvocationAdmissionWindow { get; } = messagingOptions.Value.DeduplicationWindow;

    /// <summary>
    /// 收到一个取消或信号后多久 才能收到它被援引之前的信号记录:两次
    /// <see cref="InvocationAdmissionWindow"/>,因此,发送人在该电文发现之前其发件人所实施的承认的援引
    /// 不管发件人的钟表和这个 grain's.
    /// </summary>
    /// <remarks>
    /// 将发送者时钟上的传讯时间与 grain时钟进行比较,而记录则比
    /// 以 grain时钟开始。 发送时间超过
    /// <c>DeduplicationWindow</c> 接收器的时钟前, 所以记录的信息, 发送者没有提前播放
    /// 而不是在到达前发送的援引, 且该援引只在多一个窗口内被接纳
    /// 后,该窗口。
    /// </remarks>
    public TimeSpan EarlyRecordRetention { get; } = 2 * messagingOptions.Value.DeduplicationWindow;

    public Serializer Serializer { get; } = serializer;
}
