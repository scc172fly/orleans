using System;
using Orleans.Hosting;

namespace Orleans.Streaming.NATS.Hosting;

/// <summary>
/// 提供配置NATS 的扩展 JetStream 提供方在 Orleans silo.
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 配置 silo 使用 NATS 来使用NATS JetStream 提供商。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static ISiloBuilder AddNatsStreams(this ISiloBuilder builder, string name,
        Action<NatsOptions> configureOptions)
    {
        builder.AddNatsStreams(name, b =>
            b.ConfigureNats(ob => ob.Configure(configureOptions)));
        return builder;
    }

    /// <summary>
    /// 配置 silo 使用 NATS 来使用NATS JetStream 提供商。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configure">
    /// 用于配置提供者的代表,或用于删除内嵌配置的 <see langword="null"/> 代表。
    /// 包括 <see cref="NatsOptions.StreamName"/>在内的所需选项必须单独配置。
    /// </param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static ISiloBuilder AddNatsStreams(this ISiloBuilder builder, string name,
        Action<SiloNatsStreamConfigurator>? configure)
    {
        var configurator = new SiloNatsStreamConfigurator(name,
            configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
        configure?.Invoke(configurator);
        return builder;
    }
}
