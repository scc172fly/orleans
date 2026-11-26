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
            // 基础设施的重新配置保持了警醒;所有者储存的保单约束供应商发送。
            options.ShouldRetry = (context, exception) => context.Job.Name == WakeupJobName
                ? clock.GetUtcNow() + TimeSpan.FromMinutes(1)
                : previous(context, exception);
        });
        return services;
    }
}
