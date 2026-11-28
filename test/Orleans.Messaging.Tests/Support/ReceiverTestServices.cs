using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging.Tests.Support;

internal static class ReceiverTestServices
{
    private static readonly Func<IGrainContext?> ReadCurrentContext = typeof(IGrainContext).Assembly
        .GetType("Orleans.Runtime.RuntimeContext", throwOnError: true)!
        .GetProperty("Current")!.GetMethod!.CreateDelegate<Func<IGrainContext?>>();

    public static IGrainContext? CurrentGrainContext => ReadCurrentContext();

    // 使用生产环境注册方式，将面向应用程序的发件箱替换为日志化的捕获实现。
    public static void Add(IServiceCollection services, Action<DurableMessagingOptions> configure)
    {
        services.AddMessaging(configure);
        services.RemoveAll<IOutbox>();
        services.AddScoped<IOutbox>(static sp =>
            new JournaledTestOutbox(
                sp.GetRequiredKeyedService<IDurableDictionary<MessageId, OutboxMessage>>("test-handler-output")));
    }
}
