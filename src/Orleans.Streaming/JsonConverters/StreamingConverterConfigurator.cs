
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Streaming.JsonConverters
{
    /// <summary>
    /// 使用流处理转换器配置 Newtonsoft.Json 序列化器选项。
    /// </summary>
    /// <remarks>
    /// 此类为内部类，因为它依赖内部类型 <see cref="IRuntimeClient"/>。
    /// </remarks>
    internal class StreamingConverterConfigurator : IPostConfigureOptions<OrleansJsonSerializerOptions>
    {
        private readonly IRuntimeClient _runtimeClient;

        public StreamingConverterConfigurator(IRuntimeClient runtimeClient)
        {
            _runtimeClient = runtimeClient;
        }

        public void PostConfigure(string? name, OrleansJsonSerializerOptions options)
        {
            options.JsonSerializerSettings.Converters.Add(new StreamImplConverter(_runtimeClient));
        }
    }

    /// <summary>
    /// 使用流处理转换器配置 System.Text.Json 序列化器选项。
    /// </summary>
    /// <remarks>
    /// 此类为内部类，因为它依赖内部类型 <see cref="IRuntimeClient"/>。
    /// </remarks>
    internal sealed class SystemTextJsonStreamConverterConfigurator(IRuntimeClient runtimeClient) : IPostConfigureOptions<SystemTextJsonGrainStorageSerializerOptions>
    {
        public void PostConfigure(string? name, SystemTextJsonGrainStorageSerializerOptions options)
        {
            options.JsonSerializerOptions.Converters.Add(new AsyncStreamConverter(runtimeClient));
            options.JsonSerializerOptions.Converters.Add(new EventSequenceTokenJsonConverter());
        }
    }
}
