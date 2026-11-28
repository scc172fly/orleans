using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Metadata
{
    /// <summary>
    /// 将 <see cref="GrainType"/> 与 Grain 类关联。
    /// </summary>
    public class GrainTypeResolver
    {
        private const string GrainSuffix = "grain";
        private readonly IGrainTypeProvider[] _providers;
        private readonly TypeConverter _typeConverter;

        /// <summary>
        /// 初始化 <see cref="GrainTypeResolver"/> 类的新实例。
        /// </summary>
        /// <param name="resolvers">
        /// Grain 类型名称提供程序。
        /// </param>
        /// <param name="argumentFormatter">
        /// 用于格式化泛型参数的类型转换器。
        /// </param>
        public GrainTypeResolver(
            IEnumerable<IGrainTypeProvider> resolvers,
            TypeConverter argumentFormatter)
        {
            _providers = resolvers.ToArray();
            _typeConverter = argumentFormatter;
        }

        /// <summary>
        /// 返回所提供类的 Grain 类型。
        /// </summary>
        /// <param name="type">Grain 类。</param>
        /// <returns>所提供类的 Grain 类型。</returns>
        public GrainType GetGrainType(Type type)
        {
            if (!type.IsClass || type.IsAbstract)
            {
                throw new ArgumentException($"Argument {nameof(type)} must be a non-abstract class. Provided value, \"{type}\", is not a class.", nameof(type));
            }

            // 已配置的提供程序优先。
            foreach (var provider in _providers)
            {
                if (provider.TryGetGrainType(type, out var grainType))
                {
                    grainType = AddGenericParameters(grainType, type);

                    return grainType;
                }
            }

            // 将约定作为回退方式。
            return GetGrainTypeByConvention(type);
        }

        private GrainType GetGrainTypeByConvention(Type type)
        {
#pragma warning disable CA1308 // 小写的 Grain 类型标识符保留既定的路由契约。
            var name = type.Name.ToLowerInvariant();
#pragma warning restore CA1308

            // 移除泛型参数数量后缀。
            var index = name.IndexOf('`', StringComparison.Ordinal);
            if (index > 0)
            {
                name = name[..index];
            }

            // 移除“Grain”后缀。
            index = name.LastIndexOf(GrainSuffix, StringComparison.Ordinal);
            if (index > 0 && name.Length - index == GrainSuffix.Length)
            {
                name = name[..index];
            }

            // 附加泛型参数数量，例如 typeof(MyListGrain<T>) 最终会变为 mylist`1。
            if (type.IsGenericType)
            {
                name = name + '`' + type.GetGenericArguments().Length;
            }

            var grainType = GrainType.Create(name);
            grainType = AddGenericParameters(grainType, type);
            return grainType;
        }

        private GrainType AddGenericParameters(GrainType grainType, Type type)
        {
            if (GenericGrainType.TryParse(grainType, out var genericGrainType)
                && type.IsConstructedGenericType
                && !type.ContainsGenericParameters
                && !genericGrainType.IsConstructed)
            {
                var typeArguments = type.GetGenericArguments();
                grainType = genericGrainType.Construct(_typeConverter, typeArguments).GrainType;
            }

            return grainType;
        }
    }
}
