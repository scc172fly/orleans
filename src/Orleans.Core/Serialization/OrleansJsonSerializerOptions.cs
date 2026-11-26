using System;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Orleans.Serialization
{
    /// <summary>
    /// 配置 Newtonsoft.Json 框架类型中的序列化 Orleans 。
    /// </summary>
    public class OrleansJsonSerializerOptions
    {
        /// <summary>
        /// 获取或设置序列器设置。
        /// </summary>
        /// <remarks>
        /// 设置在 Orleans 默认值下初始化,并用 Orleans 序列化夹增强
        /// 配置后选项期间的转换器和转换器。
        /// </remarks>
        public JsonSerializerSettings JsonSerializerSettings { get; set; }

        /// <summary>
        /// 获取或设定值,以显示在
        /// 包括经
        /// <see cref="Newtonsoft.Json.TypeNameHandling"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 默认值为 <see langword="false"/>。当 <see langword="false"/>时,仅允许类型
        /// 以 Orleans 类型允许列表为基准,可在解序 &#8212; 期间构建,例如
        /// 标有 <c>[GenerateSerializer]</c>中,添加到
        /// <see cref="Orleans.Serialization.Configuration.TypeManifestOptions.AllowedTypes"/>或类型
        /// 被注册的 <see cref="ITypeNameFilter"/> 或 <see cref="ITypeFilter"/>所允许。这防止了
        /// 被无信任地摧毁的空气化时,不立即将具有潜在危险的类型即刻即予处理
        /// 持续状态或流态。
        /// </para>
        /// <para>
        /// 设置此选项为 <see langword="true"/> 恢复了先前允许任何可装入的行为
        /// 类型将在解序期间构建。 这是 <b>无保障</b> , 不推荐;
        /// 更倾向于将个别类型列入允许清单。
        /// </para>
        /// </remarks>
        public bool AllowAllTypes { get; set; }

        /// <summary>
        /// 使用默认设置初始化 <see cref="OrleansJsonSerializerOptions"/> 类的新实例。
        /// </summary>
        public OrleansJsonSerializerOptions()
        {
            JsonSerializerSettings = OrleansJsonSerializerSettings.GetDefaultSerializerSettings();
        }
    }

    /// <summary>
    /// 配置 <see cref="OrleansJsonSerializerOptions"/> ,配有 Orleans 序列绑定和转换器。
    /// </summary>
    public class ConfigureOrleansJsonSerializerOptions : IPostConfigureOptions<OrleansJsonSerializerOptions>
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="ConfigureOrleansJsonSerializerOptions"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">用于解决序列器依赖关系的服务供应商。</param>
        public ConfigureOrleansJsonSerializerOptions(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public void PostConfigure(string? name, OrleansJsonSerializerOptions options)
        {
            OrleansJsonSerializerSettings.Configure(_serviceProvider, options.JsonSerializerSettings, options.AllowAllTypes);
        }
    }
}
