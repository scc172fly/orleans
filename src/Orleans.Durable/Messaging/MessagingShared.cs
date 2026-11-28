using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging;

/// <summary>
/// Silo 内所有消息传递激活实例共享的服务和设置。
/// </summary>
internal sealed class MessagingShared
{
    private readonly ConcurrentDictionary<GrainType, string> _grainTypeTags = new();

    public MessagingShared(IServiceProvider services)
    {
        Options = services.GetRequiredService<IOptions<DurableMessagingOptions>>().Value;
        TimeProvider = services.GetRequiredService<TimeProvider>();
        JobTimeProvider = services.GetRequiredKeyedService<TimeProvider>(DurableJobTimeProviderNames.DurableJobs);
        JobManager = services.GetRequiredService<ILocalDurableJobManager>();
        Transport = services.GetRequiredService<IOutboxTransport>();
        Instruments = services.GetRequiredService<DurableMessagingInstruments>();
        var loggers = services.GetRequiredService<ILoggerFactory>();
        InboxLogger = loggers.CreateLogger<Inbox>();
        OutboxLogger = loggers.CreateLogger<Outbox>();
        PumpLogger = loggers.CreateLogger<MessagingPump>();
        var format = services.GetRequiredService<IOptions<JournaledStateManagerOptions>>().Value.JournalFormatKey;
        InboxCodec = services.GetRequiredKeyedService<IDurableValueCommandCodec<InboxCommand>>(format);
        OutboxCodec = services.GetRequiredKeyedService<IDurableValueCommandCodec<OutboxCommand>>(format);
    }


    public DurableMessagingOptions Options { get; }

    public TimeProvider TimeProvider { get; }

    /// <summary>获取 Durable Jobs 时钟，该时钟定义唤醒到期时间的时间域。</summary>
    public TimeProvider JobTimeProvider { get; }

    public ILocalDurableJobManager JobManager { get; }

    public IOutboxTransport Transport { get; }

    public DurableMessagingInstruments Instruments { get; }

    public ILogger InboxLogger { get; }

    public ILogger OutboxLogger { get; }

    public ILogger PumpLogger { get; }

    public IDurableValueCommandCodec<InboxCommand> InboxCodec { get; }

    /// <summary>获取某个 Grain 类型的指标标签，由其所有激活实例共享。</summary>
    public string GetGrainTypeTag(GrainType grainType) => _grainTypeTags.GetOrAdd(grainType, static type => type.ToString());

    public IDurableValueCommandCodec<OutboxCommand> OutboxCodec { get; }
}
