using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Serialization
{
    /// <summary>
    /// 执行《公约》的执行情况 <see cref="ISerializationBinder"/> 使用 <see cref="TypeConverter"/>
    /// 并强制实施配置的 Orleans 型允许列表类型,防止任意类型构建
    /// 正在消毒
    /// </summary>
    public class OrleansJsonSerializationBinder : DefaultSerializationBinder
    {
        private readonly TypeResolver _typeResolver;
        private readonly TypeConverter? _typeConverter;
        private readonly bool _allowAllTypes;

        /// <summary>
        /// 初始化 <see cref="OrleansJsonSerializationBinder"/> 类的新实例。
        /// </summary>
        /// <param name="typeResolver">类型解答器。</param>
        /// <remarks>
        /// 此构建器不强制执行 Orleans 类型允许列表: 任何可溶性类型都可以构造
        /// 用于后向兼容性。
        /// a <see cref="TypeConverter"/> ,以便执行类型的允许清单。
        /// </remarks>
        public OrleansJsonSerializationBinder(TypeResolver typeResolver)
        {
            _typeResolver = typeResolver;
            _allowAllTypes = true;
        }

        /// <summary>
        /// 初始化执行 <see cref="OrleansJsonSerializationBinder"/> 类的新实例
        /// Orleans 类型允许列表。
        /// </summary>
        /// <param name="typeConverter">用于根据允许列表解析和验证类型的类型转换器。</param>
        /// <param name="typeResolver">当允许列表禁用时用于解析类型的类型解答器。</param>
        /// <param name="allowAllTypes">
        /// 当 <see langword="true"/>时,恢复允许构建任何可溶类型遗留行为
        /// 正在解密, 绕过允许列表的类型。 这不安全, 不推荐 。
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
                // 遗产,容许行为: 不参考许可名单就解决任何类型问题。
                if (_typeResolver is not null && _typeResolver.TryResolveType(fullName, out var resolvedType))
                {
                    return resolvedType;
                }

                return base.BindToType(assemblyName, typeName);
            }

            // 强制执行 Orleans 类型允许列表。 TypeConverter.TryParse 投出明确类型
            // 被不允许且只解决配置过滤器和允许类型所允许的类型
            // 配置。
            try
            {
                if (_typeConverter.TryParse(fullName, out var type))
                {
                    return type;
                }
            }
            catch (InvalidOperationException exception)
            {
                // TypeConverter 丢弃 InvalidOperationException ,当该类型可以溶解但不允许
                // 允许列表。表面有 JSON- 特定错误,其中也提到选择退出。
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
