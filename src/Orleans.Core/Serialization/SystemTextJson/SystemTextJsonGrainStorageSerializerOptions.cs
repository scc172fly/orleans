#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Orleans.GrainReferences;

namespace Orleans.Serialization
{
    /// <summary>
    /// 配置配置 <see cref="JsonSerializerOptions"/> 排排 System.Text.Json grain 存储序列器。
    /// </summary>
    public sealed class SystemTextJsonGrainStorageSerializerOptions
    {
        /// <summary>
        /// 获取用于读写 grain 状态的序列器选项。
        /// </summary>
        /// <remarks>
        /// 字段包含,默认省略有 <see langword="null"/> 值的属性。
        /// Orleans 转换器在选项配置后配置期间添加。
        /// </remarks>
        public JsonSerializerOptions JsonSerializerOptions { get; } = new JsonSerializerOptions()
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <summary>
    /// 将 Orleans 框架类型转换器添加到 <see cref="SystemTextJsonGrainStorageSerializerOptions"/>。
    /// </summary>
    /// <param name="grainReferenceActivator"> grain 参考转换器使用的激活器。</param>
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
