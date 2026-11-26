using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Runtime;
using System.Diagnostics.CodeAnalysis;
using Orleans.Configuration.Internal;
using Orleans.Placement.Rebalancing;
using Orleans.Runtime.Placement.Rebalancing;

namespace Orleans.Hosting;


/// <summary>
/// 配置启动再平衡的扩展。
/// </summary>
public static class ActivationRebalancerExtensions
{
    /// <summary>
    /// 使整个组群的再平衡激活
    /// </summary>
    /// <remarks>
    /// 重新平衡在这种集群内围绕该组分配启动活动的尝试
    /// 使集的 silos 能够优化激活计数和内存使用。
    /// <para>您可以在此读取关于激活再平衡的更多内容 <see href="https://www.ledjonbehluli.com/posts/orleans_adaptive_rebalancing/"></see></para>
    /// </remarks>
    public static ISiloBuilder AddActivationRebalancer(this ISiloBuilder builder) =>
        builder.AddActivationRebalancer<FailedSessionBackoffProvider>();

    /// <inheritdoc cref="AddActivationRebalancer(ISiloBuilder)"/>.
    /// <typeparam name="TProvider">自定义后卸载提供者,以便在尝试失败后确定下一届届会。</typeparam>
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
