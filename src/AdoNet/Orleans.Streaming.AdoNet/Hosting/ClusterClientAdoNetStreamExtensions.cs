namespace Orleans.Hosting;

/// <summary>
/// 允许组合群客户端中单个 ADO.NET 流的配置。
/// </summary>
public static class ClusterClientAdoNetStreamExtensions
{
    /// <summary>
    /// 配置客户端使用 ADO.NET 流提供商的配置。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddAdoNetStreams(this IClientBuilder builder, string name, Action<AdoNetStreamOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddAdoNetStreams(name, b => b.ConfigureAdoNet(ob => ob.Configure(configureOptions)));
    }

    /// <summary>
    /// 配置客户端使用 ADO.NET 流提供商的配置。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configure">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddAdoNetStreams(this IClientBuilder builder, string name, Action<ClusterClientAdoNetStreamConfigurator> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        var configurator = new ClusterClientAdoNetStreamConfigurator(name, builder);

        configure.Invoke(configurator);

        return builder;
    }
}
