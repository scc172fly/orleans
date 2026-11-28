using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Serialization
{
    /// <summary>
    /// <see cref="ISerializationBinder"/> 的实现，使用 <see cref="TypeConverter"/> 解析类型，
    /// 并强制执行配置的 Orleans 类型允许列表，防止在反序列化期间构造
    /// 任意类型。
    /// </summary>
    public class OrleansJsonSerializationBinder : DefaultSerializationBinder
    {
        private readonly TypeResolver _typeResolver;
        private readonly TypeConverter? _typeConverter;
        private readonly bool _allowAllTypes;

        /// <summary>
        /// 初始化 <see cref="OrleansJsonSerializationBinder"/> 类的新实例。
        /// </summary>
        /// <param name="typeResolver">类型解析器。</param>
        /// <remarks>
        /// 此构造函数不强制执行 Orleans 类型允许列表：任何可解析的类型都可能在反序列化期间
        /// 被构造。保留此构造函数是为了向后兼容。建议使用接受
        /// <see cref="TypeConverter"/> 的构造函数，以强制执行类型允许列表。
        /// </remarks>
        public OrleansJsonSerializationBinder(TypeResolver typeResolver)
        {
            _typeResolver = typeResolver;
            _allowAllTypes = true;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansJsonSerializationBinder"/> 类的新实例，该实例强制执行
        /// Orleans 类型允许列表。
        /// </summary>
        /// <param name="typeConverter">用于解析类型并依据允许列表验证类型的类型转换器。</param>
        /// <param name="typeResolver">在禁用允许列表时用于解析类型的类型解析器。</param>
        /// <param name="allowAllTypes">
        /// 当值为 <see langword="true"/> 时，恢复旧版行为，允许在反序列化期间构造任何可解析的类型，
        /// 从而绕过类型允许列表。这是不安全的，不建议使用。
        /// </param>
        public OrleansJsonSerializationBinder(TypeConverter typeConverter, TypeResolver typeResolver, bool allowAllTypes = false)
        {
            _typeConverter = typeConverter;
            _typeResolver = typeResolver;
            _allowAllTypes = allowAllTypes;
        }

        /// <inheritdoc />
        public override Type BindToType(string? assemblyName, string typeName)
        {
            var fullName = !string.IsNullOrWhiteSpace(assemblyName) ? typeName + ',' + assemblyName : typeName;

            if (_allowAllTypes || _typeConverter is null)
            {
                // 旧版宽松行为：不检查允许列表，直接解析任意类型。
                if (_typeResolver is not null && _typeResolver.TryResolveType(fullName, out var resolvedType))
                {
                    return resolvedType;
                }

                return base.BindToType(assemblyName, typeName);
            }

            // 强制执行 Orleans 类型允许列表。TypeConverter.TryParse 遇到明确禁止的类型时会引发异常，
            // 并且只解析已配置的筛选器及允许类型配置
            // 所许可的类型。
            try
            {
                if (_typeConverter.TryParse(fullName, out var type))
                {
                    return type;
                }
            }
            catch (InvalidOperationException exception)
            {
                // 当类型可以解析但未得到允许列表许可时，TypeConverter 会引发 InvalidOperationException。
                // 对外报告 JSON 专用错误，同时说明可以选择退出该限制。
                throw new JsonSerializationException(BuildNotAllowedMessage(fullName), exception);
            }

            throw new JsonSerializationException(BuildNotAllowedMessage(fullName));
        }

        private static string BuildNotAllowedMessage(string fullName) =>
            $"Unable to resolve type \"{fullName}\". The type could not be found or is not permitted by the configured type allow-list. " +
            $"To allow it, mark the type with [GenerateSerializer], call {nameof(Configuration.TypeManifestOptions)}.{nameof(Configuration.TypeManifestOptions.AddAllowedType)}, " +
            $"call {nameof(Configuration.TypeManifestOptions)}.{nameof(Configuration.TypeManifestOptions.AddAllowedAssembly)}, add its Orleans-formatted name to {nameof(Configuration.TypeManifestOptions)}.{nameof(Configuration.TypeManifestOptions.AllowedTypes)}, " +
            $"or register an {nameof(ITypeNameFilter)} or {nameof(ITypeFilter)} which allows it. Setting {nameof(OrleansJsonSerializerOptions)}.{nameof(OrleansJsonSerializerOptions.AllowAllTypes)} to true restores the previous behavior but is insecure when serialized input can be influenced by an untrusted party.";
    }
}
