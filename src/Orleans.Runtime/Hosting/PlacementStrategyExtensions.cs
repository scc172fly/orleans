using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Placement;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 grain 位置的扩展 。
    /// </summary>
    public static class PlacementStrategyExtensions
    {
        /// <summary>
        /// 配置 <typeparamref name="TDirector"/> 作为职位安排策略 <typeparamref name="TStrategy"/>的安置主任。
        /// </summary>
        /// <typeparam name="TStrategy">安置战略。</typeparam>
        /// <typeparam name="TDirector">安置主任。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDirector>(
            this ISiloBuilder builder)
            where TStrategy : PlacementStrategy, new()
            where TDirector : class, IPlacementDirector
        {
            return builder.ConfigureServices(services => services.AddPlacementDirector<TStrategy, TDirector>());
        }

        /// <summary>
        /// 加上一个安置主任
        /// </summary>
        /// <typeparam name="TStrategy">安置战略。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <param name="createDirector">代表过去曾设立安置主任。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(
            this ISiloBuilder builder,
            Func<IServiceProvider, IPlacementDirector> createDirector)
            where TStrategy : PlacementStrategy, new()
        {
            return builder.ConfigureServices(services => services.AddPlacementDirector<TStrategy>(createDirector));
        }

        /// <summary>
        /// 配置 <typeparamref name="TDirector"/> 作为职位安排策略 <typeparamref name="TStrategy"/>的安置主任。
        /// </summary>
        /// <typeparam name="TStrategy">安置战略。</typeparam>
        /// <typeparam name="TDirector">安置主任。</typeparam>
        /// <param name="services">服务收藏。</param>
        /// <returns>服务收藏。</returns>
        public static void AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDirector>(
            this IServiceCollection services)
            where TStrategy : PlacementStrategy, new()
            where TDirector : class, IPlacementDirector => services.AddPlacementDirector<TStrategy, TDirector>(ServiceLifetime.Singleton);

        /// <summary>
        /// 配置 <typeparamref name="TDirector"/> 作为职位安排策略 <typeparamref name="TStrategy"/>的安置主任。
        /// </summary>
        /// <typeparam name="TStrategy">安置战略。</typeparam>
        /// <typeparam name="TDirector">安置主任。</typeparam>
        /// <param name="services">服务收藏。</param>
        /// <param name="strategyLifetime">安置策略的寿命</param>
        /// <returns>服务收藏。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 是 <see langword="null"/>.</exception>
        public static void AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDirector>(
            this IServiceCollection services,
            ServiceLifetime strategyLifetime)
            where TStrategy : PlacementStrategy, new()
            where TDirector : class, IPlacementDirector
        {
            ArgumentNullException.ThrowIfNull(services);
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(PlacementStrategy), typeof(TStrategy).Name, typeof(TStrategy), strategyLifetime));
            services.AddKeyedSingleton<IPlacementDirector, TDirector>(typeof(TStrategy));
        }

        /// <summary>
        /// 加上一个安置主任
        /// </summary>
        /// <typeparam name="TStrategy">安置战略。</typeparam>
        /// <param name="services">服务收藏。</param>
        /// <param name="createDirector">代表过去曾设立安置主任。</param>
        /// <returns>服务收藏。</returns>
        public static void AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(
            this IServiceCollection services,
            Func<IServiceProvider, IPlacementDirector> createDirector)
            where TStrategy : PlacementStrategy, new() => services.AddPlacementDirector<TStrategy>(createDirector, ServiceLifetime.Singleton);

        /// <summary>
        /// 加上一个安置主任
        /// </summary>
        /// <typeparam name="TStrategy">安置战略。</typeparam>
        /// <param name="services">服务收藏。</param>
        /// <param name="createDirector">代表过去曾设立安置主任。</param>
        /// <param name="strategyLifetime">安置策略的寿命</param>
        /// <returns>服务收藏。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="createDirector"/> 是 <see langword="null"/>。</exception>
        public static void AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(
            this IServiceCollection services,
            Func<IServiceProvider, IPlacementDirector> createDirector,
            ServiceLifetime strategyLifetime)
            where TStrategy : PlacementStrategy, new()
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(createDirector);
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(PlacementStrategy), typeof(TStrategy).Name, typeof(TStrategy), strategyLifetime));
            services.AddKeyedSingleton<IPlacementDirector>(typeof(TStrategy), (sp, type) => createDirector(sp));
        }
    }
}
