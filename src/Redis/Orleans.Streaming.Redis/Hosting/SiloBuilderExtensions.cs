namespace Orleans.Hosting;

/// <summary>
/// 用于配置 Redis 流的 <see cref="ISiloBuilder"/> 扩展方法。
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 将 Silo 配置为使用 Redis 流提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configure">用于配置提供程序的委托。</param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
    public static ISiloBuilder AddRedisStreams(this ISiloBuilder builder, string name, Action<SiloRedisStreamConfigurator> configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        var configurator = new SiloRedisStreamConfigurator(name,
            configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
        configure.Invoke(configurator);
        return builder;
    }
}
