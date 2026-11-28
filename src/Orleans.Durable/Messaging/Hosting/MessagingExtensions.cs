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
/// 用于配置消息传递的扩展方法。
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// 向 Silo 添加收件箱和发件箱消息传递支持。
    /// </summary>
    /// <remarks>
    /// Grain 在日志恢复前的构造或激活设置期间解析 <see cref="IInbox"/>，以接收消息。
    /// </remarks>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置收件箱和发件箱行为的可选操作。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddMessaging(this ISiloBuilder builder, Action<DurableMessagingOptions>? configureOptions = null)
    {
        builder.AddDurableJobs();
        return builder.ConfigureServices(services => services.AddMessaging(configureOptions));
    }

    /// <summary>
    /// 添加收件箱和发件箱消息传递服务。
    /// </summary>
    /// <remarks>
    /// Grain 在日志恢复前的构造或激活设置期间解析 <see cref="IInbox"/>，以接收消息。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">用于配置收件箱和发件箱行为的可选操作。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddMessaging(this IServiceCollection services, Action<DurableMessagingOptions>? configureOptions = null)
    {
        services.AddDurableJobs();
        services.TryAddSingleton(TimeProvider.System);

        // 消息泵作业是每个 Grain 唯一用于已提交消息传递工作的持久化唤醒机制，因此它无限期重试，
        // 而其他作业仍遵循配置的策略。
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

    // 通过选项契约报告无效选项自身的错误消息。
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

    // 解析选项时执行验证，因此 Silo 会在启动前报告无效配置。
    private sealed class DurableMessagingOptionsValidator(IOptions<DurableMessagingOptions> options) : IConfigurationValidator
    {
        public void ValidateConfiguration() => _ = options.Value;
    }
}
