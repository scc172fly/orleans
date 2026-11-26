using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Durable.Messaging;
using Orleans.Durable.Messaging.Configuration;
using Orleans.Durable.Jobs;
using Orleans.Durable.Journaling.Json;

namespace Orleans.Hosting;

/// <summary>
/// 配置信息扩展
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// 添加框和发箱信息支持到 silo。
    /// </summary>
    /// <remarks>
    /// A grain 在日记收回之前,通过在建造或启动安装过程中解决 <see cref="IInbox"/> 来接收电文。
    /// </remarks>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置框和发件人行为的可选动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddMessaging(this ISiloBuilder builder, Action<DurableMessagingOptions>? configureOptions = null)
    {
        builder.AddDurableJobs();
        return builder.ConfigureServices(services => services.AddMessaging(configureOptions));
    }

    /// <summary>
    /// 添加收件箱和发件箱信息服务。
    /// </summary>
    /// <remarks>
    /// A grain 在日记收回之前,通过在建造或启动安装过程中解决 <see cref="IInbox"/> 来接收电文。
    /// </remarks>
    /// <param name="services">服务收藏。</param>
    /// <param name="configureOptions">用于配置框和发件人行为的可选动作。</param>
    /// <returns>服务收藏。</returns>
    public static IServiceCollection AddMessaging(this IServiceCollection services, Action<DurableMessagingOptions>? configureOptions = null)
    {
        services.AddDurableJobs();
        services.TryAddSingleton(TimeProvider.System);

        // 泵工作是每个 grain唯一的持久提醒, 以提醒投入的邮件工作, 所以它无限期地重复
        // ,而其他任务则保留配置的政策。
        services.AddOptions<DurableJobsOptions>().PostConfigure<IServiceProvider>(static (options, services) =>
        {
            var shouldRetry = options.ShouldRetry;
            var clock = services.GetRequiredKeyedService<TimeProvider>(DurableJobTimeProviderNames.DurableJobs);
            var messaging = services.GetRequiredService<IOptions<DurableMessagingOptions>>();
            options.ShouldRetry = (context, exception) => string.Equals(context.Job.Name, MessagingPump.JobName, StringComparison.Ordinal)
                ? MessagingPump.GetRetryTime(context, clock.GetUtcNow(), messaging.Value.RetryInitialInterval)
                : shouldRetry(context, exception);
        });
        services.Configure<JsonJournalOptions>(options =>
        {
            options.AddTypeInfoResolver(InboxJsonContext.Default);
            options.AddTypeInfoResolver(OutboxJsonContext.Default);
        });

        var optionsBuilder = services.AddOptions<DurableMessagingOptions>();
        if (configureOptions is not null)
        {
            optionsBuilder.Configure(configureOptions);
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DurableMessagingOptions>, DurableMessagingOptionsValidation>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigurationValidator, DurableMessagingOptionsValidator>());

        services.ConfigureNamedOptionForLogging<DurableMessagingOptions>(Options.DefaultName);
        services.TryAddSingleton<DurableMessagingInstruments>();
        services.TryAddSingleton<MessagingShared>();
        services.TryAddSingleton<IOutboxTransport, GrainOutboxTransport>();
        services.TryAddScoped<MessagingPump>();
        services.TryAddScoped<Inbox>();
        services.Replace(ServiceDescriptor.Scoped<IInbox>(static sp => sp.GetRequiredService<Inbox>()));
        services.TryAddScoped<Outbox>();
        services.Replace(ServiceDescriptor.Scoped<IOutbox>(static sp => sp.GetRequiredService<Outbox>()));
        services.TryAddScoped<IMessagingDiagnostics, MessagingDiagnostics>();
        return services;
    }

    // 无效选项通过选项合同报告无效选项自己的信息。
    private sealed class DurableMessagingOptionsValidation : IValidateOptions<DurableMessagingOptions>
    {
        public ValidateOptionsResult Validate(string? name, DurableMessagingOptions options)
        {
            try
            {
                options.Validate();
                return ValidateOptionsResult.Success;
            }
            catch (ArgumentException exception)
            {
                return ValidateOptionsResult.Fail(exception.Message);
            }
        }
    }

    // 解析选项时运行验证, 因此 silo 在启动前报告无效配置 。
    private sealed class DurableMessagingOptionsValidator(IOptions<DurableMessagingOptions> options) : IConfigurationValidator
    {
        public void ValidateConfiguration() => _ = options.Value;
    }
}
