using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Configuration;
using Orleans.GrainDirectory;
using Orleans.GrainDirectory.Firestore;
using Orleans.Runtime.Hosting;

namespace Orleans.Hosting;

/// <summary>
/// Google云火炉配置扩展方法 grain 目录。
/// </summary>
public static class FirestoreGrainDirectoryExtensions
{
    internal static IServiceCollection AddFirestoreGrainDirectory(
        this IServiceCollection services,
        string name,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        configureOptions.Invoke(services.AddOptions<FirestoreOptions>(name));
        services
            .AddTransient<IConfigurationValidator>(sp =>
                new FirestoreOptionsValidator<FirestoreOptions>(
                    sp.GetRequiredService<IOptionsMonitor<FirestoreOptions>>().Get(name)))
            .ConfigureNamedOptionForLogging<FirestoreOptions>(name)
            .AddGrainDirectory(name,
                (sp, name) =>
                    ActivatorUtilities.CreateInstance<FirestoreGrainDirectory>(sp,
                        sp.GetOptionsByName<FirestoreOptions>(name)));

        return services;
    }

    /// <summary>
    /// 配置 Google Cloud Firshstore 作为默认 grain 目录 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseFirestoreGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<FirestoreOptions> configureOptions)
    {
        return builder.UseFirestoreGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 配置 Google Cloud Firshstore 作为默认 grain 目录 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseFirestoreGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        return builder.ConfigureServices(services =>
            services.AddFirestoreGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));
    }

    /// <summary>
    /// 添加名为 Google Cloud Firshstore grain 目录的谷歌云火炉。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name"> grain 目录的名称。</param>
    /// <param name="configureOptions">用于配置提供者的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddFirestoreGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<FirestoreOptions> configureOptions)
    {
        return builder.AddFirestoreGrainDirectory(name, ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 添加名为 Google Cloud Firshstore grain 目录的谷歌云火炉。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name"> grain 目录的名称。</param>
    /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddFirestoreGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddFirestoreGrainDirectory(name, configureOptions));
    }
}
