using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration.Internal;
using Orleans.Runtime;
using Orleans.Configuration;
using Orleans.Runtime.Placement.Repartitioning;
using System.Diagnostics.CodeAnalysis;
using Orleans.Placement.Repartitioning;

namespace Orleans.Hosting;

/// <summary>
/// 用于配置激活实例重新分区的扩展方法。
/// </summary>
public static class ActivationRepartitioningExtensions
{
    /// <summary>
    /// 为此 Silo 启用激活实例重新分区。
    /// </summary>
    /// <remarks>
    /// 激活实例重新分区尝试将频繁通信的激活实例放置到同一位置，以优化 Grain 调用的局部性，
    /// 同时保持每个 Silo 上的激活实例数量大致相等。
    /// </remarks>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>提供的 Silo 构建器。</returns>
    public static ISiloBuilder AddActivationRepartitioner(this ISiloBuilder builder)
        => builder.AddActivationRepartitioner<RebalancerCompatibleRule>();

    /// <summary>
    /// 为此 Silo 启用激活实例重新分区。
    /// </summary>
    /// <remarks>
    /// 激活实例重新分区尝试将频繁通信的激活实例放置到同一位置，以优化 Grain 调用的局部性，
    /// 同时保持每个 Silo 上的激活实例数量大致相等。
    /// </remarks>
    /// <typeparam name="TRule">要使用的不均衡规则的类型。</typeparam>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>提供的 Silo 构建器。</returns>
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
