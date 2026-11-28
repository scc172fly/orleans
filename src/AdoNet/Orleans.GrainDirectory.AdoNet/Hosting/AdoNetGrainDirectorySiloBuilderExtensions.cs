namespace Orleans.Hosting;

/// <summary>
/// 用于配置 ADO.NET Grain 目录的扩展方法。
/// </summary>
public static class AdoNetGrainDirectorySiloBuilderExtensions
{
    /// <summary>
    /// 将 ADO.NET 配置为默认 Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseAdoNetGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<AdoNetGrainDirectoryOptions> configureOptions) =>
        builder.UseAdoNetGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));

    /// <summary>
    /// 将 ADO.NET 配置为默认 Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseAdoNetGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<OptionsBuilder<AdoNetGrainDirectoryOptions>> configureOptions) =>
        builder.ConfigureServices(services => services.AddAdoNetGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));

    /// <summary>
    /// 添加命名的 ADO.NET Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">Grain 目录名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddAdoNetGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<AdoNetGrainDirectoryOptions> configureOptions) =>
        builder.AddAdoNetGrainDirectory(name, ob => ob.Configure(configureOptions));

    /// <summary>
    /// 添加命名的 ADO.NET Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">Grain 目录名称。</param>
    /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddAdoNetGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<OptionsBuilder<AdoNetGrainDirectoryOptions>> configureOptions) =>
        builder.ConfigureServices(services => services.AddAdoNetGrainDirectory(name, configureOptions));
}
