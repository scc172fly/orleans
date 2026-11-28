namespace Orleans.Hosting;

/// <summary>
/// 允许在集群客户端中配置各个 ADO.NET 流。
/// </summary>
public static class ClusterClientAdoNetStreamExtensions
{
    /// <summary>
    /// 将客户端配置为使用 ADO.NET 流提供程序。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
    public static IClientBuilder AddAdoNetStreams(this IClientBuilder builder, string name, Action<AdoNetStreamOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddAdoNetStreams(name, b => b.ConfigureAdoNet(ob => ob.Configure(configureOptions)));
    }

    /// <summary>
    /// 将客户端配置为使用 ADO.NET 流提供程序。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configure">用于配置提供程序的委托。</param>
    /// <returns>同一个 <paramref name="builder"/> 实例。</returns>
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
