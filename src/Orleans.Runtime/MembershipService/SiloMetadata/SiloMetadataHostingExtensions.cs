using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration.Internal;
using Orleans.Hosting;
using Orleans.Placement;
using Orleans.Runtime.Placement.Filtering;

namespace Orleans.Runtime.MembershipService.SiloMetadata;

/// <summary>
/// 用于配置 Silo 元数据的扩展方法。
/// </summary>
public static class SiloMetadataHostingExtensions
{
    /// <summary>
    /// 根据构建器配置设置 Silo 元数据。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <remarks>
    /// 读取 <c>Orleans:Metadata</c> 配置节。
    /// 可以通过如下环境变量提供键值对：
    /// ORLEANS__METADATA__key1=value1
    /// </remarks>
    /// <returns>提供的 Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder UseSiloMetadata(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseSiloMetadata(builder.Configuration);
    }

    /// <summary>
    /// 根据所提供的配置设置 Silo 元数据。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configuration">包含 Silo 元数据的配置。</param>
    /// <remarks>
    /// 读取 <c>Orleans:Metadata</c> 配置节。
    /// 可以通过如下环境变量提供键值对：
    /// ORLEANS__METADATA__key1=value1
    /// </remarks>
    /// <returns>提供的 Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="configuration"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder UseSiloMetadata(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        var metadataConfigSection = configuration.GetSection("Orleans").GetSection("Metadata");

        return builder.UseSiloMetadata(metadataConfigSection);
    }

    /// <summary>
    /// 根据所提供的配置节设置 Silo 元数据。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configurationSection">包含 Silo 元数据的配置节。</param>
    /// <remarks>
    /// 将提供的配置节绑定为 <see cref="Dictionary{TKey,TValue}"/>。
    /// 可以通过如下环境变量提供键值对：
    /// ORLEANS__METADATA__key1=value1
    /// </remarks>
    /// <returns>提供的 Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="configurationSection"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder UseSiloMetadata(this ISiloBuilder builder, IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);
        var dictionary = configurationSection.Get<Dictionary<string, string>>();

        return builder.UseSiloMetadata(dictionary ?? []);
    }

    /// <summary>
    /// 配置本地 Silo 发布的元数据。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="metadata">为本地 Silo 发布的元数据。</param>
    /// <returns>提供的 Silo 构建器。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> 为 <see langword="null"/>。</exception>
    public static ISiloBuilder UseSiloMetadata(this ISiloBuilder builder, Dictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        builder.ConfigureServices(services =>
        {
            services
                .AddOptionsWithValidateOnStart<SiloMetadata>()
                .Configure(m => m.AddMetadata(metadata));

            services.AddSingleton<SiloMetadataSystemTarget>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, SiloMetadataSystemTarget>();
            services.AddSingleton<SiloMetadataCache>();
            services.AddFromExisting<ISiloMetadataCache, SiloMetadataCache>();
            services.AddFromExisting<ILifecycleParticipant<ISiloLifecycle>, SiloMetadataCache>();
            services.AddSingleton<ISiloMetadataClient, SiloMetadataClient>();

            // 放置筛选器。
            services.AddPlacementFilter<PreferredMatchSiloMetadataPlacementFilterStrategy, PreferredMatchSiloMetadataPlacementFilterDirector>(ServiceLifetime.Transient);
            services.AddPlacementFilter<RequiredMatchSiloMetadataPlacementFilterStrategy, RequiredMatchSiloMetadataPlacementFilterDirector>(ServiceLifetime.Transient);
        });
        return builder;
    }
}
