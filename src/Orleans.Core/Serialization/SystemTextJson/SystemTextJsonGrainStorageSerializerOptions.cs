#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Orleans.GrainReferences;

namespace Orleans.Serialization
{
    /// <summary>
    /// 为 System.Text.Json Grain 存储序列化器配置 <see cref="JsonSerializerOptions"/>。
    /// </summary>
    public sealed class SystemTextJsonGrainStorageSerializerOptions
    {
        /// <summary>
        /// 获取用于读取和写入 Grain 状态的序列化器选项。
        /// </summary>
        /// <remarks>
        /// 默认包含字段，并省略值为 <see langword="null"/> 的属性。
        /// Orleans 转换器在选项后置配置期间添加。
        /// </remarks>
        public JsonSerializerOptions JsonSerializerOptions { get; } = new JsonSerializerOptions()
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <summary>
    /// 将 Orleans 框架类型的转换器添加到 <see cref="SystemTextJsonGrainStorageSerializerOptions"/>。
    /// </summary>
    /// <param name="grainReferenceActivator">Grain 引用转换器所使用的激活器。</param>
    public sealed class SystemTextJsonSerializerOptionsConfigure(GrainReferenceActivator grainReferenceActivator) : IPostConfigureOptions<SystemTextJsonGrainStorageSerializerOptions>
    {
        /// <inheritdoc />
        public void PostConfigure(string? name, SystemTextJsonGrainStorageSerializerOptions options)
        {
            options.JsonSerializerOptions.Converters.Add(new IPAddressJsonConverter());
            options.JsonSerializerOptions.Converters.Add(new IPEndPointJsonConverter());
            options.JsonSerializerOptions.Converters.Add(new GrainReferenceConverter(grainReferenceActivator));
        }
    }
}
