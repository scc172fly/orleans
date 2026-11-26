using System.Diagnostics.CodeAnalysis;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 <see cref="IIncomingGrainCallFilter"/> 和 <see cref="IOutgoingGrainCallFilter"/> 执行的扩展方法。
    /// </summary>
    public static class GrainCallFilterSiloBuilderExtensions
    {
        /// <summary>
        /// 在过滤管道中添加 <see cref="IIncomingGrainCallFilter"/> 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="filter">过滤器。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddIncomingGrainCallFilter(this ISiloBuilder builder, IIncomingGrainCallFilter filter)
        {
            return builder.ConfigureServices(services => services.AddIncomingGrainCallFilter(filter));
        }

        /// <summary>
        /// 在过滤管道中添加 <see cref="IIncomingGrainCallFilter"/> 。
        /// </summary>
        /// <typeparam name="TImplementation">过滤器执行类型。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddIncomingGrainCallFilter<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this ISiloBuilder builder)
            where TImplementation : class, IIncomingGrainCallFilter
        {
            return builder.ConfigureServices(services => services.AddIncomingGrainCallFilter<TImplementation>());
        }

        /// <summary>
        /// 通过代表向过滤管道添加 <see cref="IIncomingGrainCallFilter"/> 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="filter">过滤器。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddIncomingGrainCallFilter(this ISiloBuilder builder, IncomingGrainCallFilterDelegate filter)
        {
            return builder.ConfigureServices(services => services.AddIncomingGrainCallFilter(filter));
        }

        /// <summary>
        /// 在过滤管道中添加 <see cref="IOutgoingGrainCallFilter"/> 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="filter">过滤器。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddOutgoingGrainCallFilter(this ISiloBuilder builder, IOutgoingGrainCallFilter filter)
        {
            return builder.ConfigureServices(services => services.AddOutgoingGrainCallFilter(filter));
        }

        /// <summary>
        /// 在过滤管道中添加 <see cref="IOutgoingGrainCallFilter"/> 。
        /// </summary>
        /// <typeparam name="TImplementation">过滤器执行类型。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddOutgoingGrainCallFilter<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this ISiloBuilder builder)
            where TImplementation : class, IOutgoingGrainCallFilter
        {
            return builder.ConfigureServices(services => services.AddOutgoingGrainCallFilter<TImplementation>());
        }

        /// <summary>
        /// 通过代表向过滤管道添加 <see cref="IOutgoingGrainCallFilter"/> 。
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="filter">过滤器。</param>
        /// <returns>建筑工。</returns>
        public static ISiloBuilder AddOutgoingGrainCallFilter(this ISiloBuilder builder, OutgoingGrainCallFilterDelegate filter)
        {
            return builder.ConfigureServices(services => services.AddOutgoingGrainCallFilter(filter));
        }
    }
}
