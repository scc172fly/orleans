using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Placement;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 Grain 放置的扩展方法。
    /// </summary>
    public static class PlacementStrategyExtensions
    {
        /// <summary>
        /// 将 <typeparamref name="TDirector"/> 配置为放置策略 <typeparamref name="TStrategy"/> 的放置决策器。
        /// </summary>
        /// <typeparam name="TStrategy">放置策略。</typeparam>
        /// <typeparam name="TDirector">放置决策器。</typeparam>
        /// <param name="builder">构建器。</param>
        /// <returns>构建器。</returns>
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
        /// 添加放置决策器。
        /// </summary>
        /// <typeparam name="TStrategy">放置策略。</typeparam>
        /// <param name="builder">构建器。</param>
        /// <param name="createDirector">用于创建放置决策器的委托。</param>
        /// <returns>构建器。</returns>
        public static ISiloBuilder AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(
            this ISiloBuilder builder,
            Func<IServiceProvider, IPlacementDirector> createDirector)
            where TStrategy : PlacementStrategy, new()
        {
            return builder.ConfigureServices(services => services.AddPlacementDirector<TStrategy>(createDirector));
        }

        /// <summary>
        /// 将 <typeparamref name="TDirector"/> 配置为放置策略 <typeparamref name="TStrategy"/> 的放置决策器。
        /// </summary>
        /// <typeparam name="TStrategy">放置策略。</typeparam>
        /// <typeparam name="TDirector">放置决策器。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <returns>服务集合。（译注：此重载实际返回 void，不返回服务集合。）</returns>
        public static void AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDirector>(
            this IServiceCollection services)
            where TStrategy : PlacementStrategy, new()
            where TDirector : class, IPlacementDirector => services.AddPlacementDirector<TStrategy, TDirector>(ServiceLifetime.Singleton);

        /// <summary>
        /// 将 <typeparamref name="TDirector"/> 配置为放置策略 <typeparamref name="TStrategy"/> 的放置决策器。
        /// </summary>
        /// <typeparam name="TStrategy">放置策略。</typeparam>
        /// <typeparam name="TDirector">放置决策器。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <param name="strategyLifetime">放置策略的生命周期。</param>
        /// <returns>服务集合。（译注：此重载实际返回 void，不返回服务集合。）</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 为 <see langword="null"/>。</exception>
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
        /// 添加放置决策器。
        /// </summary>
        /// <typeparam name="TStrategy">放置策略。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <param name="createDirector">用于创建放置决策器的委托。</param>
        /// <returns>服务集合。（译注：此重载实际返回 void，不返回服务集合。）</returns>
        public static void AddPlacementDirector<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(
            this IServiceCollection services,
            Func<IServiceProvider, IPlacementDirector> createDirector)
            where TStrategy : PlacementStrategy, new() => services.AddPlacementDirector<TStrategy>(createDirector, ServiceLifetime.Singleton);

        /// <summary>
        /// 添加放置决策器。
        /// </summary>
        /// <typeparam name="TStrategy">放置策略。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <param name="createDirector">用于创建放置决策器的委托。</param>
        /// <param name="strategyLifetime">放置策略的生命周期。</param>
        /// <returns>服务集合。（译注：此重载实际返回 void，不返回服务集合。）</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> 或 <paramref name="createDirector"/> 为 <see langword="null"/>。</exception>
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
