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
/// 配置 silo 元数据的扩展名。
/// </summary>
public static class SiloMetadataHostingExtensions
{
    /// <summary>
    /// 配置构建器配置中的 silo 元数据。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <remarks>
    /// 读取 <c>Orleans:Metadata</c> 配置部分。
    /// 使用环境变量,例如:
    /// ORLENS - 气象数据_ 键1=值1
    /// </remarks>
    /// <returns>提供的情况 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 是 <see langword="null"/>.</exception>
    public static ISiloBuilder UseSiloMetadata(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseSiloMetadata(builder.Configuration);
    }

    /// <summary>
    /// 配置 silo 元数据,由提供配置。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configuration">包含 silo 元数据的配置。</param>
    /// <remarks>
    /// 读取 <c>Orleans:Metadata</c> 配置部分。
    /// 使用环境变量,例如:
    /// ORLENS - 气象数据_ 键1=值1
    /// </remarks>
    /// <returns>提供的情况 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="configuration"/> 是 <see langword="null"/>。</exception>
    public static ISiloBuilder UseSiloMetadata(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        var metadataConfigSection = configuration.GetSection("Orleans").GetSection("Metadata");

        return builder.UseSiloMetadata(metadataConfigSection);
    }

    /// <summary>
    /// 配置 silo 元数据,来自提供配置的配置区域。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configurationSection">配置部分包含 silo 元数据。</param>
    /// <remarks>
    /// 将所提供的部分作为 <see cref="Dictionary{TKey,TValue}"/>捆绑。
    /// 使用环境变量,例如:
    /// ORLENS - 气象数据_ 键1=值1
    /// </remarks>
    /// <returns>提供的情况 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> 或 <paramref name="configurationSection"/> 是 <see langword="null"/>。</exception>
    public static ISiloBuilder UseSiloMetadata(this ISiloBuilder builder, IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);
        var dictionary = configurationSection.Get<Dictionary<string, string>>();

        return builder.UseSiloMetadata(dictionary ?? []);
    }

    /// <summary>
    /// 配置本地 silo发布的元数据。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="metadata">本地 silo发布元数据。</param>
    /// <returns>提供的情况 silo 建筑商</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> 是 <see langword="null"/>.</exception>
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

            // 放置过滤器
            services.AddPlacementFilter<PreferredMatchSiloMetadataPlacementFilterStrategy, PreferredMatchSiloMetadataPlacementFilterDirector>(ServiceLifetime.Transient);
            services.AddPlacementFilter<RequiredMatchSiloMetadataPlacementFilterStrategy, RequiredMatchSiloMetadataPlacementFilterDirector>(ServiceLifetime.Transient);
        });
        return builder;
    }
}
