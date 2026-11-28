using System;
namespace Orleans.Hosting;

/// <summary>
/// 用于配置 Redis 流的 <see cref="IClientBuilder"/> 扩展方法。
/// </summary>
public static class ClientBuilderExtensions
{
    /// <summary>
    /// 将客户端配置为使用 Redis 流提供程序。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configure">用于配置提供程序的委托。</param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddRedisStreams(this IClientBuilder builder, string name, Action<ClusterClientRedisStreamConfigurator> configure)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        var configurator = new ClusterClientRedisStreamConfigurator(name, builder);
        configure.Invoke(configurator);
        return builder;
    }
}
