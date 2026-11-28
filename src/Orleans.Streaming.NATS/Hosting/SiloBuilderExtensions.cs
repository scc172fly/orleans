using System;
using Orleans.Hosting;

namespace Orleans.Streaming.NATS.Hosting;

/// <summary>
/// 提供用于在 Orleans Silo 上配置 NATS JetStream 提供程序的扩展方法。
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 配置 Silo 使用 NATS JetStream 提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
    public static ISiloBuilder AddNatsStreams(this ISiloBuilder builder, string name,
        Action<NatsOptions> configureOptions)
    {
        builder.AddNatsStreams(name, b =>
            b.ConfigureNats(ob => ob.Configure(configureOptions)));
        return builder;
    }

    /// <summary>
    /// 配置 Silo 使用 NATS JetStream 提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configure">
    /// 用于配置提供程序的委托；传入 <see langword="null"/> 则省略内联配置。
    /// 必须另行配置必需选项（包括 <see cref="NatsOptions.StreamName"/>）。
    /// </param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
    public static ISiloBuilder AddNatsStreams(this ISiloBuilder builder, string name,
        Action<SiloNatsStreamConfigurator>? configure)
    {
        var configurator = new SiloNatsStreamConfigurator(name,
            configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
        configure?.Invoke(configurator);
        return builder;
    }
}
