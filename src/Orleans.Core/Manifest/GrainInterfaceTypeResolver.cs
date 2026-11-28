using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Metadata
{
    /// <summary>
    /// 将 <see cref="GrainInterfaceType"/> 与 <see cref="Type" /> 关联。
    /// </summary>
    public class GrainInterfaceTypeResolver
    {
        private readonly IGrainInterfaceTypeProvider[] _providers;
        private readonly TypeConverter _typeConverter;
        private readonly ConcurrentDictionary<Type, GrainInterfaceType> _interfaceTypes = new();

        /// <summary>
        /// 初始化 <see cref="GrainInterfaceTypeResolver"/> 类的新实例。
        /// </summary>
        /// <param name="providers">
        /// Grain 接口类型提供程序集合。
        /// </param>
        /// <param name="typeConverter">
        /// 用于泛型参数名称的类型转换器。
        /// </param>
        public GrainInterfaceTypeResolver(
            IEnumerable<IGrainInterfaceTypeProvider> providers,
            TypeConverter typeConverter)
        {
            _providers = providers.ToArray();
            _typeConverter = typeConverter;
        }

        /// <summary>
        /// 返回所提供接口的缓存 <see cref="GrainInterfaceType"/>。
        /// </summary>
        /// <remarks>
        /// 按解析器和 CLR 接口类型缓存标识，包括封闭泛型的类型实参。
        /// 已配置的提供程序在解析器的生命周期内定义稳定的标识。
        /// </remarks>
        /// <param name="type">Grain 接口。</param>
        /// <returns>所提供接口的 <see cref="GrainInterfaceType"/>。</returns>
        public GrainInterfaceType GetGrainInterfaceType(Type type)
        {
            return _interfaceTypes.GetOrAdd(type, static (interfaceType, resolver) => resolver.ResolveGrainInterfaceType(interfaceType), this);
        }

        private GrainInterfaceType ResolveGrainInterfaceType(Type type)
        {
            if (!type.IsInterface)
            {
                throw new ArgumentException($"Argument {nameof(type)} must be an interface. Provided value, \"{type}\", is not an interface.", nameof(type));
            }

            // 已配置的提供程序优先。
            foreach (var provider in _providers)
            {
                if (provider.TryGetGrainInterfaceType(type, out var interfaceType))
                {
                    interfaceType = AddGenericParameters(interfaceType, type);
                    return interfaceType;
                }
            }

            // 将约定作为回退方式。
            return GetGrainInterfaceTypeByConvention(type);
        }

        /// <summary>
        /// 根据默认约定获取 Grain 接口类型。
        /// </summary>
        /// <param name="type">Grain 接口类型。</param>
        /// <returns>Grain 接口类型名称。</returns>
        public GrainInterfaceType GetGrainInterfaceTypeByConvention(Type type)
        {
            var result = GrainInterfaceType.Create(_typeConverter.Format(type, input => input switch
            {
                AssemblyQualifiedTypeSpec asm => asm.Type, // 移除外层程序集限定信息。
                _ => input
            }));

            result = AddGenericParameters(result, type);
            return result;
        }

        private GrainInterfaceType AddGenericParameters(GrainInterfaceType result, Type type)
        {
            if (GenericGrainInterfaceType.TryParse(result, out var genericGrainType)
                && type.IsConstructedGenericType
                && !type.ContainsGenericParameters
                && !genericGrainType.IsConstructed)
            {
                result = genericGrainType.Construct(_typeConverter, type.GetGenericArguments()).Value;
            }

            return result;
        }
    }
}
