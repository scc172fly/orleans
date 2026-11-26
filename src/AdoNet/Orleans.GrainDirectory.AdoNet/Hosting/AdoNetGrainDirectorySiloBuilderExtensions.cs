namespace Orleans.Hosting;

/// <summary>
/// 配置 ADO.NET grain 目录的扩展方法。
/// </summary>
public static class AdoNetGrainDirectorySiloBuilderExtensions
{
    /// <summary>
    /// 将配置 ADO.NET 设为默认的 grain 目录。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseAdoNetGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<AdoNetGrainDirectoryOptions> configureOptions) =>
        builder.UseAdoNetGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));

    /// <summary>
    /// 将配置 ADO.NET 设为默认的 grain 目录。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseAdoNetGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<OptionsBuilder<AdoNetGrainDirectoryOptions>> configureOptions) =>
        builder.ConfigureServices(services => services.AddAdoNetGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));

    /// <summary>
    /// 添加名为 ADO.NET grain 目录。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name"> grain 目录的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddAdoNetGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<AdoNetGrainDirectoryOptions> configureOptions) =>
        builder.AddAdoNetGrainDirectory(name, ob => ob.Configure(configureOptions));

    /// <summary>
    /// 添加名为 ADO.NET grain 目录。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name"> grain 目录的名称。</param>
    /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddAdoNetGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<OptionsBuilder<AdoNetGrainDirectoryOptions>> configureOptions) =>
        builder.ConfigureServices(services => services.AddAdoNetGrainDirectory(name, configureOptions));
}
