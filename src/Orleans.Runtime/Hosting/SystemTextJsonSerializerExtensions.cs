using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;
using Orleans.Storage;

#nullable enable

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置基于 System.Text.Json 的 Grain 存储序列化的扩展方法。
    /// </summary>
    public static class SystemTextJsonSerializerExtensions
    {
        /// <summary>
        /// 将 System.Text.Json 配置为默认 Grain 存储序列化器。
        /// </summary>
        /// <param name="siloBuilder">要配置为支持 System.Text.Json Grain 存储的 Silo 构建器。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder UseSystemTextJsonGrainStorageSerializer(this ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer, SystemTextJsonGrainStorageSerializer>();
            return siloBuilder;
        }

        /// <summary>
        /// 将 System.Text.Json 配置为默认 Grain 存储序列化器。
        /// </summary>
        /// <param name="siloBuilder">要配置为支持 System.Text.Json Grain 存储的 Silo 构建器。</param>
        /// <param name="configureOptions">用于配置序列化器选项的委托。</param>
        /// <returns>Silo 构建器。</returns>
        /// <remarks>
        /// <paramref name="configureOptions"/> 在 Orleans 添加自身转换器之前运行，因此此处添加的转换器优先。
        /// </remarks>
        public static ISiloBuilder UseSystemTextJsonGrainStorageSerializer(this ISiloBuilder siloBuilder, Action<SystemTextJsonGrainStorageSerializerOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);
            return siloBuilder.UseSystemTextJsonGrainStorageSerializer(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 将 System.Text.Json 配置为默认 Grain 存储序列化器。
        /// </summary>
        /// <param name="siloBuilder">要配置为支持 System.Text.Json Grain 存储的 Silo 构建器。</param>
        /// <param name="configureOptions">用于配置序列化器选项构建器的委托，例如解析服务或绑定配置。</param>
        /// <returns>Silo 构建器。</returns>
        /// <remarks>
        /// 通过 <paramref name="configureOptions"/> 应用的配置在 Orleans 添加自身转换器之前运行，因此此处添加的转换器优先。
        /// </remarks>
        public static ISiloBuilder UseSystemTextJsonGrainStorageSerializer(this ISiloBuilder siloBuilder, Action<OptionsBuilder<SystemTextJsonGrainStorageSerializerOptions>> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);
            configureOptions(siloBuilder.Services.AddOptions<SystemTextJsonGrainStorageSerializerOptions>());
            return siloBuilder.UseSystemTextJsonGrainStorageSerializer();
        }
    }
}
