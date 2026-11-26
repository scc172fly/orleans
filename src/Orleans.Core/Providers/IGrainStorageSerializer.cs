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
    /// grain 状态序列器的共同界面。
    /// </summary>
    public interface IGrainStorageSerializer
    {
        /// <summary>
        /// 将对象输入分层。
        /// </summary>
        /// <param name="input">序列化对象。</param>
        /// <typeparam name="T">输入类型。</typeparam>
        /// <returns>序列输入。</returns>
        BinaryData Serialize<T>(T? input);

        /// <summary>
        /// 使提供的数据降序。
        /// </summary>
        /// <param name="input">用于解除序列的数据。</param>
        /// <typeparam name="T">输出类型。</typeparam>
        /// <returns>消毒物体</returns>
        T? Deserialize<T>(BinaryData input);
    }

    /// <summary>
    /// grain 状态的基于流的可选序列转换器。
    /// </summary>
    public interface IGrainStorageStreamingSerializer : IGrainStorageSerializer
    {
        /// <summary>
        /// 将对象输入的序列化为流。
        /// </summary>
        /// <param name="input">序列化对象。</param>
        /// <param name="destination">目的地流。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <typeparam name="T">输入类型。</typeparam>
        ValueTask SerializeAsync<T>(T? input, Stream destination, CancellationToken cancellationToken = default);

        /// <summary>
        /// 将从流中提供的数据降序。
        /// </summary>
        /// <param name="input">输入流。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <typeparam name="T">输出类型。</typeparam>
        /// <returns>消毒物体</returns>
        ValueTask<T?> DeserializeAsync<T>(Stream input, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IGrainStorageSerializer"/>的扩展期。
    /// </summary>
    public static class GrainStorageSerializerExtensions
    {
        /// <summary>
        /// 使提供的数据降序。
        /// </summary>
        /// <param name="serializer"> grain 状态序列器。</param>
        /// <param name="input">用于解除序列的数据。</param>
        /// <typeparam name="T">输出类型。</typeparam>
        /// <returns>消毒物体</returns>
        public static T? Deserialize<T>(this IGrainStorageSerializer serializer, ReadOnlyMemory<byte> input)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            return serializer.Deserialize<T>(new BinaryData(input));
        }
    }

    /// <summary>
    /// 由存储提供商选项实施接口。
    /// </summary>
    public interface IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 获取或设置用于此存储提供者的序列器。
        /// </summary>
        IGrainStorageSerializer GrainStorageSerializer { get; set; }
    }

    /// <summary>
    /// 提供 <see cref="IStorageProviderSerializerOptions.GrainStorageSerializer"/>的默认配置。
    /// </summary>
    /// <typeparam name="TOptions">选项类型。</typeparam>
    public class DefaultStorageProviderSerializerOptionsConfigurator<TOptions> : IPostConfigureOptions<TOptions> where TOptions : class, IStorageProviderSerializerOptions
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="DefaultStorageProviderSerializerOptionsConfigurator{TOptions}"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">服务供应商。</param>
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
                // 首先,尝试获得一个 IGrainStorageSerializer 注册的
                // 与存储供应商同名
                // 如果找不到,则返回到系统范围违约
                options.GrainStorageSerializer = name is not null
                    ? _serviceProvider.GetKeyedService<IGrainStorageSerializer>(name) ?? _serviceProvider.GetRequiredService<IGrainStorageSerializer>()
                    : _serviceProvider.GetRequiredService<IGrainStorageSerializer>();
            }
        }
    }
}
