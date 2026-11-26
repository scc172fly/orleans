
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Streaming.JsonConverters
{
    /// <summary>
    /// 配置 Newtonsoft.Json 序列器选项,可使用流转换器。
    /// </summary>
    /// <remarks>
    /// 此类是内部的, 因为它取决于 <see cref="IRuntimeClient"/> 是一个内部类型。
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
    /// 配置 System.Text.Json 序列器选项,可使用流转换器。
    /// </summary>
    /// <remarks>
    /// 此类是内部的, 因为它取决于 <see cref="IRuntimeClient"/> 是一个内部类型。
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
