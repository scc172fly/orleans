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
/// 用于配置 Google Cloud Firestore Grain 目录的扩展方法。
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
    /// 将 Google Cloud Firestore 配置为默认 Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseFirestoreGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<FirestoreOptions> configureOptions)
    {
        return builder.UseFirestoreGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 将 Google Cloud Firestore 配置为默认 Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseFirestoreGrainDirectoryAsDefault(
        this ISiloBuilder builder,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        return builder.ConfigureServices(services =>
            services.AddFirestoreGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));
    }

    /// <summary>
    /// 添加具名 Google Cloud Firestore Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">Grain 目录名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddFirestoreGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<FirestoreOptions> configureOptions)
    {
        return builder.AddFirestoreGrainDirectory(name, ob => ob.Configure(configureOptions));
    }

    /// <summary>
    /// 添加具名 Google Cloud Firestore Grain 目录。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">Grain 目录名称。</param>
    /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddFirestoreGrainDirectory(
        this ISiloBuilder builder,
        string name,
        Action<OptionsBuilder<FirestoreOptions>> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddFirestoreGrainDirectory(name, configureOptions));
    }
}
