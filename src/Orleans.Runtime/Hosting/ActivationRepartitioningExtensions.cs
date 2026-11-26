using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration.Internal;
using Orleans.Runtime;
using Orleans.Configuration;
using Orleans.Runtime.Placement.Repartitioning;
using System.Diagnostics.CodeAnalysis;
using Orleans.Placement.Repartitioning;

namespace Orleans.Hosting;

/// <summary>
/// 配置启动重新分配的扩展 。
/// </summary>
public static class ActivationRepartitioningExtensions
{
    /// <summary>
    /// 启用此 silo的激活重新分割。
    /// </summary>
    /// <remarks>
    /// 重新激活尝试优化 grain 呼叫地点,办法是对频繁交流的启动装置进行拼接,
    /// 将每个 silo 的激活数量大致保持相等。
    /// </remarks>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>提供的情况 silo 建筑商</returns>
    public static ISiloBuilder AddActivationRepartitioner(this ISiloBuilder builder)
        => builder.AddActivationRepartitioner<RebalancerCompatibleRule>();

    /// <summary>
    /// 启用此 silo的激活重新分割。
    /// </summary>
    /// <remarks>
    /// 重新激活尝试优化 grain 呼叫地点,办法是对频繁交流的启动装置进行拼接,
    /// 将每个 silo 的激活数量大致保持相等。
    /// </remarks>
    /// <typeparam name="TRule">要使用的不平衡规则类型 。</typeparam>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>提供的情况 silo 建筑商</returns>
    public static ISiloBuilder AddActivationRepartitioner<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRule>(
        this ISiloBuilder builder)
        where TRule : class, IImbalanceToleranceRule
        => builder
            .ConfigureServices(services => services.AddActivationRepartitioner<TRule>());

    private static IServiceCollection AddActivationRepartitioner<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRule>(
        this IServiceCollection services)
        where TRule : class, IImbalanceToleranceRule
    {
        services.AddSingleton<ActivationRepartitioner>();
        services.AddSingleton<IRepartitionerMessageFilter, RepartitionerMessageFilter>();
        services.AddFromExisting<IMessageStatisticsSink, ActivationRepartitioner>();
        services.AddSingleton<ActivationRepartitioner.DeactivatedGrainQueue>();
        services.AddFromExisting<IActivationWorkingSetObserver, ActivationRepartitioner.DeactivatedGrainQueue>();
        services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ActivationRepartitioner>();
        services.AddTransient<IConfigurationValidator, ActivationRepartitionerOptionsValidator>();

        services.AddSingleton<TRule>();
        services.AddFromExisting<IImbalanceToleranceRule, TRule>();
        if (typeof(TRule).IsAssignableTo(typeof(ILifecycleParticipant<ISiloLifecycle>)))
        {
            services.AddFromExisting(typeof(ILifecycleParticipant<ISiloLifecycle>), typeof(TRule));
        }

        return services;
    }
}
