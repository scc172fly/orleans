namespace Orleans.Hosting;

/// <summary>
/// <see cref="ISiloBuilder"/> 配置 Redis 流的扩展。
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 配置 silo 使用 Redis 串流提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configure">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
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
