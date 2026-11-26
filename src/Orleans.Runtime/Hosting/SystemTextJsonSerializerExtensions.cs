using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;
using Orleans.Storage;

#nullable enable

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 System.Text.Json- 基于 grain 的储存序列化的扩展。
    /// </summary>
    public static class SystemTextJsonSerializerExtensions
    {
        /// <summary>
        /// 配置 System.Text.Json 默认为存储序列器 grain 。
        /// </summary>
        /// <param name="siloBuilder"> silo 构建器, 以 System.Text.Json grain 储存支持配置。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder UseSystemTextJsonGrainStorageSerializer(this ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer, SystemTextJsonGrainStorageSerializer>();
            return siloBuilder;
        }

        /// <summary>
        /// 配置 System.Text.Json 默认为存储序列器 grain 。
        /// </summary>
        /// <param name="siloBuilder"> silo 构建器, 以 System.Text.Json grain 储存支持配置。</param>
        /// <param name="configureOptions">代表用于配置序列器选项。</param>
        /// <returns>下 silo 建筑商</returns>
        /// <remarks>
        /// <paramref name="configureOptions"/> 运行在 Orleans 添加自有转换器之前,因此在此添加的转换器优先。
        /// </remarks>
        public static ISiloBuilder UseSystemTextJsonGrainStorageSerializer(this ISiloBuilder siloBuilder, Action<SystemTextJsonGrainStorageSerializerOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);
            return siloBuilder.UseSystemTextJsonGrainStorageSerializer(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 配置 System.Text.Json 默认为存储序列器 grain 。
        /// </summary>
        /// <param name="siloBuilder"> silo 构建器, 以 System.Text.Json grain 储存支持配置。</param>
        /// <param name="configureOptions">代表用来配置序列器选项构建器,例如用于解决服务或捆绑配置。</param>
        /// <returns>下 silo 建筑商</returns>
        /// <remarks>
        /// 配置应用到 <paramref name="configureOptions"/> 之前运行的 Orleans 添加自有的转换器, 因此在此添加的转换器优先 。
        /// </remarks>
        public static ISiloBuilder UseSystemTextJsonGrainStorageSerializer(this ISiloBuilder siloBuilder, Action<OptionsBuilder<SystemTextJsonGrainStorageSerializerOptions>> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);
            configureOptions(siloBuilder.Services.AddOptions<SystemTextJsonGrainStorageSerializerOptions>());
            return siloBuilder.UseSystemTextJsonGrainStorageSerializer();
        }
    }
}
