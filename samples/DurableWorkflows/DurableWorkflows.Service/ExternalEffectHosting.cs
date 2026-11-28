using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Durable.Jobs;
using Orleans.Hosting;

namespace DurableWorkflows;

public static class ExternalEffectHosting
{
    public const string WakeupJobName = "external-effect-next-action";

    public static IServiceCollection AddExternalEffectRecipes(this IServiceCollection services)
    {
        services.AddOptions<ExternalEffectOptions>();
        services.TryAddKeyedSingleton(ExternalEffectOptions.ClockName, TimeProvider.System);
        services.AddOptions<DurableJobsOptions>().PostConfigure<IServiceProvider>((options, provider) =>
        {
            var previous = options.ShouldRetry;
            var clock = provider.GetRequiredKeyedService<TimeProvider>(DurableJobTimeProviderNames.DurableJobs);
            // 基础设施重试保留唤醒操作；所有者持久化的策略限制向服务提供方发送请求的次数。
            options.ShouldRetry = (context, exception) => context.Job.Name == WakeupJobName
                ? clock.GetUtcNow() + TimeSpan.FromMinutes(1)
                : previous(context, exception);
        });
        return services;
    }
}
