using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Azure 表存储目录 grain 的扩展方法。
    /// </summary>
    public static class AzureTableGrainDirectorySiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Azure 表格存储为默认 grain 目录。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置提供者的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder UseAzureTableGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<AzureTableGrainDirectoryOptions> configureOptions)
        {
            return builder.UseAzureTableGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 Azure 表格存储为默认 grain 目录。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder UseAzureTableGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<OptionsBuilder<AzureTableGrainDirectoryOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureTableGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));
        }

        /// <summary>
        /// 添加名为 Azure 的表格存储目录 grain 。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name"> grain 目录的名称。</param>
        /// <param name="configureOptions">用于配置提供者的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddAzureTableGrainDirectory(
            this ISiloBuilder builder,
            string name,
            Action<AzureTableGrainDirectoryOptions> configureOptions)
        {
            return builder.AddAzureTableGrainDirectory(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加名为 Azure 的表格存储目录 grain 。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name"> grain 目录的名称。</param>
        /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddAzureTableGrainDirectory(
            this ISiloBuilder builder,
            string name,
            Action<OptionsBuilder<AzureTableGrainDirectoryOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureTableGrainDirectory(name, configureOptions));
        }
    }
}
