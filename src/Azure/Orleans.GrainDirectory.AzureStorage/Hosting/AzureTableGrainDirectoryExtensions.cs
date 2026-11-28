using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 Azure Table Storage Grain 目录的扩展方法。
    /// </summary>
    public static class AzureTableGrainDirectorySiloBuilderExtensions
    {
        /// <summary>
        /// 将 Azure Table Storage 配置为默认 Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder UseAzureTableGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<AzureTableGrainDirectoryOptions> configureOptions)
        {
            return builder.UseAzureTableGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 将 Azure Table Storage 配置为默认 Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder UseAzureTableGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<OptionsBuilder<AzureTableGrainDirectoryOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureTableGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));
        }

        /// <summary>
        /// 添加命名的 Azure Table Storage Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">Grain 目录名称。</param>
        /// <param name="configureOptions">用于配置提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddAzureTableGrainDirectory(
            this ISiloBuilder builder,
            string name,
            Action<AzureTableGrainDirectoryOptions> configureOptions)
        {
            return builder.AddAzureTableGrainDirectory(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加命名的 Azure Table Storage Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">Grain 目录名称。</param>
        /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddAzureTableGrainDirectory(
            this ISiloBuilder builder,
            string name,
            Action<OptionsBuilder<AzureTableGrainDirectoryOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddAzureTableGrainDirectory(name, configureOptions));
        }
    }
}
