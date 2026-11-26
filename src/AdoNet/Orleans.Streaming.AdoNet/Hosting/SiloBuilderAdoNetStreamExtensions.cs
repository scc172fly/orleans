namespace Orleans.Hosting;

/// <summary>
/// 允许个人配置 ADO.NET 在一个串中 silo.
/// </summary>
public static class SiloBuilderAdoNetStreamExtensions
{
    /// <summary>
    /// 配置 silo 使用 ADO.NET 串流提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static ISiloBuilder AddAdoNetStreams(this ISiloBuilder builder, string name, Action<AdoNetStreamOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        return builder.AddAdoNetStreams(name, b => b.ConfigureAdoNet(ob => ob.Configure(configureOptions)));
    }

    /// <summary>
    /// 配置 silo 使用 ADO.NET 串流提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configure">用于配置提供者的代表。</param>
    /// <returns>相同 <paramref name="builder"/> 实例。</returns>
    public static ISiloBuilder AddAdoNetStreams(this ISiloBuilder builder, string name, Action<SiloAdoNetStreamConfigurator> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        var configurator = new SiloAdoNetStreamConfigurator(name, configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));

        configure.Invoke(configurator);

        return builder;
    }
}
