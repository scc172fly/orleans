using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.GrainReferences;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Serialization
{
    /// <summary>
    /// 提供工厂和配置方法 Newtonsoft.Json 支持的设置 Orleans 框架类型。
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
        /// 创建默认的序列器设置,并配置 Orleans 序列绑定和转换器。
        /// </summary>
        /// <param name="services">用于解决序列器依赖关系的服务供应商。</param>
        /// <returns>配置的默认序列器设置。</returns>
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
        /// 更新所提供的序列器设置, 并使用指定的选项 。
        /// </summary>
        /// <param name="settings">设置。</param>
        /// <param name="useFullAssemblyNames">
        /// 当 <see langword="true"/>时,在格式化类型名称时使用完整的组装合格名称;
        /// 另选其他格式,不修改当前组装名称的格式。
        /// </param>
        /// <param name="indentJson">
        /// 当 <see langword="true"/>时,缩进格式 JSON;否则,保持当前格式不变。
        /// </param>
        /// <param name="typeNameHandling">
        /// 要应用的名称处理值类型, 或要保持当前值不变的 <see langword="null"/> 。
        /// </param>
        /// <returns>提供的序列器设置。</returns>
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
