using System;
using Orleans.Hosting;
using Orleans.Streaming.NATS.Hosting;

namespace Orleans.Streaming.NATS.Hosting;

/// <summary>
/// 提供用于在 Orleans 客户端上配置 NATS JetStream 提供程序的扩展方法。
/// </summary>
public static class ClientBuilderExtensions
{
    /// <summary>
    /// 配置客户端使用 NATS JetStream 提供程序。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddNatsStreams(this IClientBuilder builder, string name,
        Action<NatsOptions> configureOptions)
    {
        builder.AddNatsStreams(name, b =>
            b.ConfigureNats(ob => ob.Configure(configureOptions)));
        return builder;
    }

    /// <summary>
    /// 配置客户端使用 NATS JetStream 提供程序。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configure">
    /// 用于配置提供程序的委托；传入 <see langword="null"/> 则省略内联配置。
    /// 必须另行配置必需选项（包括 <see cref="NatsOptions.StreamName"/>）。
    /// </param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddNatsStreams(this IClientBuilder builder, string name,
        Action<ClusterClientNatsStreamConfigurator>? configure)
    {
        var configurator = new ClusterClientNatsStreamConfigurator(name, builder);
        configure?.Invoke(configurator);
        return builder;
    }
}
