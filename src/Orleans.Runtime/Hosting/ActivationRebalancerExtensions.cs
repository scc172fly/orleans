using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Runtime;
using System.Diagnostics.CodeAnalysis;
using Orleans.Configuration.Internal;
using Orleans.Placement.Rebalancing;
using Orleans.Runtime.Placement.Rebalancing;

namespace Orleans.Hosting;


/// <summary>
/// 用于配置激活实例再均衡的扩展方法。
/// </summary>
public static class ActivationRebalancerExtensions
{
    /// <summary>
    /// 为整个集群启用激活实例再均衡。
    /// </summary>
    /// <remarks>
    /// 激活实例再均衡尝试在集群内分布激活实例，
    /// 从而优化集群中各 Silo 的激活实例数和内存使用量。
    /// <para>关于激活实例再均衡的更多信息请参阅<see href="https://www.ledjonbehluli.com/posts/orleans_adaptive_rebalancing/">此处</see></para>
    /// </remarks>
    public static ISiloBuilder AddActivationRebalancer(this ISiloBuilder builder) =>
        builder.AddActivationRebalancer<FailedSessionBackoffProvider>();

    /// <inheritdoc cref="AddActivationRebalancer(ISiloBuilder)"/>.
    /// <typeparam name="TProvider">用于在尝试失败后确定下一会话时间的自定义退避提供程序。</typeparam>
    public static ISiloBuilder AddActivationRebalancer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
        this ISiloBuilder builder)
        where TProvider : class, IFailedSessionBackoffProvider =>
        builder.ConfigureServices(service => service.AddActivationRebalancer<TProvider>());

    private static IServiceCollection AddActivationRebalancer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
        this IServiceCollection services)
        where TProvider : class, IFailedSessionBackoffProvider
    {
        services.AddSingleton<ActivationRebalancerMonitor>();
        services.AddFromExisting<IActivationRebalancer, ActivationRebalancerMonitor>();
        services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, ActivationRebalancerMonitor>();
        services.AddTransient<IConfigurationValidator, ActivationRebalancerOptionsValidator>();

        services.AddSingleton<TProvider>();
        services.AddFromExisting<IFailedSessionBackoffProvider, TProvider>();
        if (typeof(TProvider).IsAssignableTo(typeof(ILifecycleParticipant<ISiloLifecycle>)))
        {
            services.AddFromExisting(typeof(ILifecycleParticipant<ISiloLifecycle>), typeof(TProvider));
        }

        return services;
    }
}
