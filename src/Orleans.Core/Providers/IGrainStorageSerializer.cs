using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Storage
{
    /// <summary>
    /// Grain 状态序列化器的通用接口。
    /// </summary>
    public interface IGrainStorageSerializer
    {
        /// <summary>
        /// 将输入对象序列化。
        /// </summary>
        /// <param name="input">要序列化的对象。</param>
        /// <typeparam name="T">输入类型。</typeparam>
        /// <returns>序列化后的输入。</returns>
        BinaryData Serialize<T>(T? input);

        /// <summary>
        /// 对所提供的数据进行反序列化。
        /// </summary>
        /// <param name="input">要反序列化的数据。</param>
        /// <typeparam name="T">输出类型。</typeparam>
        /// <returns>反序列化后的对象。</returns>
        T? Deserialize<T>(BinaryData input);
    }

    /// <summary>
    /// 可选的基于流的 Grain 状态序列化器。
    /// </summary>
    public interface IGrainStorageStreamingSerializer : IGrainStorageSerializer
    {
        /// <summary>
        /// 将输入对象序列化到流中。
        /// </summary>
        /// <param name="input">要序列化的对象。</param>
        /// <param name="destination">目标流。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <typeparam name="T">输入类型。</typeparam>
        ValueTask SerializeAsync<T>(T? input, Stream destination, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从流中反序列化所提供的数据。
        /// </summary>
        /// <param name="input">输入流。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <typeparam name="T">输出类型。</typeparam>
        /// <returns>反序列化后的对象。</returns>
        ValueTask<T?> DeserializeAsync<T>(Stream input, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IGrainStorageSerializer"/> 的扩展方法。
    /// </summary>
    public static class GrainStorageSerializerExtensions
    {
        /// <summary>
        /// 反序列化所提供的数据。
        /// </summary>
        /// <param name="serializer">Grain 状态序列化器。</param>
        /// <param name="input">要反序列化的数据。</param>
        /// <typeparam name="T">输出类型。</typeparam>
        /// <returns>反序列化后的对象。</returns>
        public static T? Deserialize<T>(this IGrainStorageSerializer serializer, ReadOnlyMemory<byte> input)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            return serializer.Deserialize<T>(new BinaryData(input));
        }
    }

    /// <summary>
    /// 存储提供程序选项应实现的接口。
    /// </summary>
    public interface IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 获取或设置此存储提供程序使用的序列化器。
        /// </summary>
        IGrainStorageSerializer GrainStorageSerializer { get; set; }
    }

    /// <summary>
    /// 为 <see cref="IStorageProviderSerializerOptions.GrainStorageSerializer"/> 提供默认配置。
    /// </summary>
    /// <typeparam name="TOptions">选项类型。</typeparam>
    public class DefaultStorageProviderSerializerOptionsConfigurator<TOptions> : IPostConfigureOptions<TOptions> where TOptions : class, IStorageProviderSerializerOptions
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="DefaultStorageProviderSerializerOptionsConfigurator{TOptions}"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        public DefaultStorageProviderSerializerOptionsConfigurator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc/>
        public void PostConfigure(string? name, TOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.GrainStorageSerializer == default)
            {
                // 首先尝试获取以与存储提供程序
                // 相同的名称注册的 IGrainStorageSerializer。
                // 如果未找到，则回退到系统范围内的默认实现。
                options.GrainStorageSerializer = name is not null
                    ? _serviceProvider.GetKeyedService<IGrainStorageSerializer>(name) ?? _serviceProvider.GetRequiredService<IGrainStorageSerializer>()
                    : _serviceProvider.GetRequiredService<IGrainStorageSerializer>();
            }
        }
    }
}
