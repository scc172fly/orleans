using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> 的扩展方法。
    /// </summary>
    public static class GrainCallFilterServiceCollectionExtensions
    {
        /// <summary>
        /// 向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="filter">筛选器。</param>
        /// <returns>服务集合。</returns>
        [Obsolete("Use ISiloBuilder." + nameof(AddIncomingGrainCallFilter), error: true)]
        public static IServiceCollection AddGrainCallFilter(this IServiceCollection services, IIncomingGrainCallFilter filter)
        {
            throw new NotSupportedException($"{nameof(AddGrainCallFilter)} is no longer supported. Use ISiloBuilder.AddIncomingGrainCallFilter(...) instead.");
        }

        /// <summary>
        /// 向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
        /// </summary>
        /// <typeparam name="TImplementation">筛选器实现类型。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <returns>服务集合。</returns>
        [Obsolete("Use ISiloBuilder." + nameof(AddIncomingGrainCallFilter), error: true)]
        public static IServiceCollection AddGrainCallFilter<TImplementation>(this IServiceCollection services)
            where TImplementation : class, IIncomingGrainCallFilter
        {
            throw new NotSupportedException($"{nameof(AddGrainCallFilter)} is no longer supported. Use ISiloBuilder.AddIncomingGrainCallFilter(...) instead.");
        }

        /// <summary>
        /// 通过委托向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="filter">筛选器。</param>
        /// <returns>服务集合。</returns>
        [Obsolete("Use ISiloBuilder." + nameof(AddIncomingGrainCallFilter), error: true)]
        public static IServiceCollection AddGrainCallFilter(this IServiceCollection services, GrainCallFilterDelegate filter)
        {
            throw new NotSupportedException($"{nameof(AddGrainCallFilter)} is no longer supported. Use ISiloBuilder.AddIncomingGrainCallFilter(...) instead.");
        }

        /// <summary>
        /// 向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="filter">筛选器。</param>
        /// <returns>服务集合。</returns>
        internal static IServiceCollection AddIncomingGrainCallFilter(this IServiceCollection services, IIncomingGrainCallFilter filter)
        {
            return services.AddSingleton(filter);
        }

        /// <summary>
        /// 向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
        /// </summary>
        /// <typeparam name="TImplementation">筛选器实现类型。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <returns>服务集合。</returns>
        internal static IServiceCollection AddIncomingGrainCallFilter<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services)
            where TImplementation : class, IIncomingGrainCallFilter
        {
            return services.AddSingleton<IIncomingGrainCallFilter, TImplementation>();
        }

        /// <summary>
        /// 通过委托向筛选器管道添加 <see cref="IIncomingGrainCallFilter"/>。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="filter">筛选器。</param>
        /// <returns>服务集合。</returns>
        internal static IServiceCollection AddIncomingGrainCallFilter(this IServiceCollection services, IncomingGrainCallFilterDelegate filter)
        {
            return services.AddSingleton<IIncomingGrainCallFilter>(new IncomingGrainCallFilterWrapper(filter));
        }

        /// <summary>
        /// 向筛选器管道添加 <see cref="IOutgoingGrainCallFilter"/>。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="filter">筛选器。</param>
        /// <returns>服务集合。</returns>
        internal static IServiceCollection AddOutgoingGrainCallFilter(this IServiceCollection services, IOutgoingGrainCallFilter filter)
        {
            return services.AddSingleton(filter);
        }

        /// <summary>
        /// 向筛选器管道添加 <see cref="IOutgoingGrainCallFilter"/>。
        /// </summary>
        /// <typeparam name="TImplementation">筛选器实现类型。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <returns>服务集合。</returns>
        internal static IServiceCollection AddOutgoingGrainCallFilter<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services)
            where TImplementation : class, IOutgoingGrainCallFilter
        {
            return services.AddSingleton<IOutgoingGrainCallFilter, TImplementation>();
        }

        /// <summary>
        /// 通过委托将 <see cref="IOutgoingGrainCallFilter"/> 添加到筛选器管道。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="filter">筛选器。</param>
        /// <returns>服务集合。</returns>
        internal static IServiceCollection AddOutgoingGrainCallFilter(this IServiceCollection services, OutgoingGrainCallFilterDelegate filter)
        {
            return services.AddSingleton<IOutgoingGrainCallFilter>(new OutgoingGrainCallFilterWrapper(filter));
        }

        /// <summary>
        /// 将 <see cref="OutgoingGrainCallFilterDelegate"/> 委托适配为 <see cref="IOutgoingGrainCallFilter"/> 接口。
        /// </summary>
        private class OutgoingGrainCallFilterWrapper : IOutgoingGrainCallFilter
        {
            private readonly OutgoingGrainCallFilterDelegate interceptor;

            /// <summary>
            /// 初始化 <see cref="OutgoingGrainCallFilterWrapper"/> 类的新实例。
            /// </summary>
            /// <param name="interceptor">
            /// 拦截器。
            /// </param>
            public OutgoingGrainCallFilterWrapper(OutgoingGrainCallFilterDelegate interceptor)
            {
                this.interceptor = interceptor;
            }

            /// <inheritdoc />
            public Task Invoke(IOutgoingGrainCallContext context) => this.interceptor.Invoke(context);
        }

        /// <summary>
        /// 实现 <see cref="IIncomingGrainCallFilter"/>，将所有 <see cref="IIncomingGrainCallFilter.Invoke"/> 调用交给所提供的委托。
        /// </summary>
        private class IncomingGrainCallFilterWrapper : IIncomingGrainCallFilter
        {
            private readonly IncomingGrainCallFilterDelegate interceptor;

            /// <summary>
            /// 初始化 <see cref="IncomingGrainCallFilterWrapper"/> 类的新实例。
            /// </summary>
            /// <param name="interceptor">
            /// 拦截器。
            /// </param>
            public IncomingGrainCallFilterWrapper(IncomingGrainCallFilterDelegate interceptor)
            {
                this.interceptor = interceptor;
            }

            /// <summary>
            /// 调用此筛选器。
            /// </summary>
            /// <param name="context">Grain 调用上下文。</param>
            /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
            public Task Invoke(IIncomingGrainCallContext context) => this.interceptor.Invoke(context);
        }
    }
}
