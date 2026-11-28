using System.Diagnostics.CodeAnalysis;

namespace Orleans.Hosting;

/// <summary>
/// 用于配置 Grain 调用筛选器的扩展方法。
/// </summary>
public static class ClientBuilderGrainCallFilterExtensions
{
    /// <summary>
    /// 向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
    /// </summary>
    /// <param name="builder">构建器。</param>
    /// <param name="filter">筛选器。</param>
    /// <returns>构建器。</returns>
    public static IClientBuilder AddIncomingGrainCallFilter(this IClientBuilder builder, IIncomingGrainCallFilter filter)
    {
        return builder.ConfigureServices(services => services.AddIncomingGrainCallFilter(filter));
    }

    /// <summary>
    /// 向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
    /// </summary>
    /// <typeparam name="TImplementation">筛选器实现类型。</typeparam>
    /// <param name="builder">构建器。</param>
    /// <returns>构建器。</returns>
    public static IClientBuilder AddIncomingGrainCallFilter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IClientBuilder builder)
        where TImplementation : class, IIncomingGrainCallFilter
    {
        return builder.ConfigureServices(services => services.AddIncomingGrainCallFilter<TImplementation>());
    }

    /// <summary>
    /// 通过委托向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
    /// </summary>
    /// <param name="builder">构建器。</param>
    /// <param name="filter">筛选器。</param>
    /// <returns>构建器。</returns>
    public static IClientBuilder AddIncomingGrainCallFilter(this IClientBuilder builder, IncomingGrainCallFilterDelegate filter)
    {
        return builder.ConfigureServices(services => services.AddIncomingGrainCallFilter(filter));
    }

    /// <summary>
    /// 向筛选器管道添加 <see cref="IOutgoingGrainCallFilter"/>。
    /// </summary>
    /// <param name="builder">构建器。</param>
    /// <param name="filter">筛选器。</param>
    /// <returns><see cref="IClientBuilder"/>。</returns>
    public static IClientBuilder AddOutgoingGrainCallFilter(this IClientBuilder builder, IOutgoingGrainCallFilter filter)
    {
        return builder.ConfigureServices(services => services.AddOutgoingGrainCallFilter(filter));
    }

    /// <summary>
    /// 向筛选器管道添加 <see cref="IOutgoingGrainCallFilter"/>。
    /// </summary>
    /// <typeparam name="TImplementation">筛选器实现类型。</typeparam>
    /// <param name="builder">构建器。</param>
    /// <returns><see cref="IClientBuilder"/>。</returns>
    public static IClientBuilder AddOutgoingGrainCallFilter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IClientBuilder builder)
        where TImplementation : class, IOutgoingGrainCallFilter
    {
        return builder.ConfigureServices(services => services.AddOutgoingGrainCallFilter<TImplementation>());
    }

    /// <summary>
    /// 通过委托将 <see cref="IOutgoingGrainCallFilter"/> 添加到筛选器管道。
    /// </summary>
    /// <param name="builder">构建器。</param>
    /// <param name="filter">筛选器。</param>
    /// <returns><see cref="IClientBuilder"/>。</returns>
    public static IClientBuilder AddOutgoingGrainCallFilter(this IClientBuilder builder, OutgoingGrainCallFilterDelegate filter)
    {
        return builder.ConfigureServices(services => services.AddOutgoingGrainCallFilter(filter));
    }
}
