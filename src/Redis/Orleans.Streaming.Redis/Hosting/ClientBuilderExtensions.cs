using System;
namespace Orleans.Hosting;

/// <summary>
/// 扩展至 <see cref="IClientBuilder"/> 配置 Redis streams.
/// </summary>
public static class ClientBuilderExtensions
{
    /// <summary>
    /// 配置客户端使用 Redis 流提供商的客户端。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configure">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddRedisStreams(this IClientBuilder builder, string name, Action<ClusterClientRedisStreamConfigurator> configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        var configurator = new ClusterClientRedisStreamConfigurator(name, builder);
        configure.Invoke(configurator);
        return builder;
    }
}
