using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Metadata
{
    /// <summary>
    /// a <see cref="GrainInterfaceType"/> 与 <see cref="Type" />有关。
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
        /// 收集 grain 接口类型提供者。
        /// </param>
        /// <param name="typeConverter">
        /// 用于通用参数名称的型号转换器。
        /// </param>
        public GrainInterfaceTypeResolver(
            IEnumerable<IGrainInterfaceTypeProvider> providers,
            TypeConverter typeConverter)
        {
            _providers = providers.ToArray();
            _typeConverter = typeConverter;
        }

        /// <summary>
        /// 返回为提供接口而缓存的 <see cref="GrainInterfaceType"/> 。
        /// </summary>
        /// <remarks>
        /// 每个解决者和CLR接口类型都隐藏身份,包括封闭的通用参数。
        /// 配置的提供者定义了解决者一生的稳定身份 。
        /// </remarks>
        /// <param name="type"> grain 接口。</param>
        /// <returns>下 <see cref="GrainInterfaceType"/> 提供的接口。</returns>
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

            // 配置的提供者优先
            foreach (var provider in _providers)
            {
                if (provider.TryGetGrainInterfaceType(type, out var interfaceType))
                {
                    interfaceType = AddGenericParameters(interfaceType, type);
                    return interfaceType;
                }
            }

            // 公约被用作一种退让。
            return GetGrainInterfaceTypeByConvention(type);
        }

        /// <summary>
        /// 根据默认常规获取 grain 接口类型。
        /// </summary>
        /// <param name="type"> grain 接口类型。</param>
        /// <returns> grain 接口类型名称。</returns>
        public GrainInterfaceType GetGrainInterfaceTypeByConvention(Type type)
        {
            var result = GrainInterfaceType.Create(_typeConverter.Format(type, input => input switch
            {
                AssemblyQualifiedTypeSpec asm => asm.Type, // 外流浮浮表面组装资格
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
