using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.GrainReferences;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Serialization
{
    /// <summary>
    /// 提供支持 Orleans 框架类型的 Newtonsoft.Json 设置的工厂方法和配置方法。
    /// </summary>
    public static class OrleansJsonSerializerSettings
    {
        internal static JsonSerializerSettings GetDefaultSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                Formatting = Formatting.None,
                SerializationBinder = null,
            };
        }

        /// <summary>
        /// 创建默认序列化器设置，并配置 Orleans 序列化绑定器及转换器。
        /// </summary>
        /// <param name="services">用于解析序列化器依赖项的服务提供程序。</param>
        /// <returns>已配置的默认序列化器设置。</returns>
        public static JsonSerializerSettings GetDefaultSerializerSettings(IServiceProvider services)
        {
            var settings = GetDefaultSerializerSettings();
            var allowAllTypes = services.GetService<IOptions<OrleansJsonSerializerOptions>>()?.Value.AllowAllTypes ?? false;
            Configure(services, settings, allowAllTypes);
            return settings;
        }

        internal static void Configure(IServiceProvider services, JsonSerializerSettings jsonSerializerSettings, bool allowAllTypes = false)
        {
            if (jsonSerializerSettings.SerializationBinder == null)
            {
                var typeResolver = services.GetRequiredService<TypeResolver>();
                var typeConverter = services.GetRequiredService<TypeConverter>();
                jsonSerializerSettings.SerializationBinder = new OrleansJsonSerializationBinder(typeConverter, typeResolver, allowAllTypes);
            }

            jsonSerializerSettings.Converters.Add(new IPAddressConverter());
            jsonSerializerSettings.Converters.Add(new IPEndPointConverter());
            jsonSerializerSettings.Converters.Add(new GrainIdConverter());
            jsonSerializerSettings.Converters.Add(new ActivationIdConverter());
            jsonSerializerSettings.Converters.Add(new SiloAddressJsonConverter());
            jsonSerializerSettings.Converters.Add(new MembershipVersionJsonConverter());
            jsonSerializerSettings.Converters.Add(new UniqueKeyConverter());
            jsonSerializerSettings.Converters.Add(new GrainReferenceJsonConverter(services.GetRequiredService<GrainReferenceActivator>()));
        }

        /// <summary>
        /// 使用指定选项更新提供的序列化器设置。
        /// </summary>
        /// <param name="settings">设置。</param>
        /// <param name="useFullAssemblyNames">
        /// 当值为 <see langword="true"/> 时，在格式化类型名称时使用完整程序集限定名称；
        /// 否则保持当前程序集名称格式不变。
        /// </param>
        /// <param name="indentJson">
        /// 当值为 <see langword="true"/> 时，对格式化后的 JSON 进行缩进；否则保持当前格式不变。
        /// </param>
        /// <param name="typeNameHandling">
        /// 要应用的类型名称处理值，或使用 <see langword="null"/> 保持当前值不变。
        /// </param>
        /// <returns>提供的序列化器设置。</returns>
        public static JsonSerializerSettings UpdateSerializerSettings(JsonSerializerSettings settings, bool useFullAssemblyNames, bool indentJson, TypeNameHandling? typeNameHandling)
        {
            if (useFullAssemblyNames)
            {
                settings.TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full;
            }

            if (indentJson)
            {
                settings.Formatting = Formatting.Indented;
            }

            if (typeNameHandling.HasValue)
            {
                settings.TypeNameHandling = typeNameHandling.Value;
            }

            return settings;
        }
    }
}
