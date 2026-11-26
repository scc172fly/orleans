using System;
using Orleans.Hosting;
using Orleans.Streaming.NATS.Hosting;

namespace Orleans.Streaming.NATS.Hosting;

/// <summary>
/// 提供配置NATS 的扩展 JetStream 提供方在 Orleans 客户端。
/// </summary>
public static class ClientBuilderExtensions
{
    /// <summary>
    /// 配置客户端使用 NATS JetStream 提供者的配置。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddNatsStreams(this IClientBuilder builder, string name,
        Action<NatsOptions> configureOptions)
    {
        builder.AddNatsStreams(name, b =>
            b.ConfigureNats(ob => ob.Configure(configureOptions)));
        return builder;
    }

    /// <summary>
    /// 配置客户端使用 NATS JetStream 提供者的配置。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configure">
    /// 用于配置提供者的代表,或用于删除内嵌配置的 <see langword="null"/> 代表。
    /// 包括 <see cref="NatsOptions.StreamName"/>在内的所需选项必须单独配置。
    /// </param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddNatsStreams(this IClientBuilder builder, string name,
        Action<ClusterClientNatsStreamConfigurator>? configure)
    {
        var configurator = new ClusterClientNatsStreamConfigurator(name, builder);
        configure?.Invoke(configurator);
        return builder;
    }
}
