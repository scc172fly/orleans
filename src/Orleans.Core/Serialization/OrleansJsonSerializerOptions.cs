using System;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Orleans.Serialization
{
    /// <summary>
    /// 为 Orleans 框架类型配置 Newtonsoft.Json 序列化。
    /// </summary>
    public class OrleansJsonSerializerOptions
    {
        /// <summary>
        /// 获取或设置序列化器设置。
        /// </summary>
        /// <remarks>
        /// 设置使用 Orleans 的默认值进行初始化，并在选项后置配置期间加入 Orleans 序列化绑定器
        /// 和转换器。
        /// </remarks>
        public JsonSerializerSettings JsonSerializerSettings { get; set; }

        /// <summary>
        /// 获取或设置指示是否可以在反序列化期间构造任何可解析类型的值，
        /// 包括序列化有效载荷中通过以下方式指定名称的类型：
        /// <see cref="Newtonsoft.Json.TypeNameHandling"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 默认值为 <see langword="false"/>。当值为 <see langword="false"/> 时，仅允许在反序列化期间构造
        /// Orleans 类型允许列表许可的类型 &#8212; 例如，
        /// 使用 <c>[GenerateSerializer]</c> 标记的类型、添加到
        /// <see cref="Orleans.Serialization.Configuration.TypeManifestOptions.AllowedTypes"/> 的类型，或者
        /// 由已注册的 <see cref="ITypeNameFilter"/> 或 <see cref="ITypeFilter"/> 许可的类型。这样可以防止
        /// 在反序列化不可信的持久化状态或通过流传入的状态时，实例化任意的、可能具有危险性的
        /// 类型。
        /// </para>
        /// <para>
        /// 将此值设置为 <see langword="true"/> 会恢复之前的行为，允许反序列化期间构造任何可加载的
        /// 类型。这种行为 <b>不安全</b>，不建议使用；
        /// 应优先将具体类型逐一加入允许列表。
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
    /// 使用 Orleans 序列化绑定器及转换器配置 <see cref="OrleansJsonSerializerOptions"/>。
    /// </summary>
    public class ConfigureOrleansJsonSerializerOptions : IPostConfigureOptions<OrleansJsonSerializerOptions>
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="ConfigureOrleansJsonSerializerOptions"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">用于解析序列化器依赖项的服务提供程序。</param>
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
