using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Metadata
{
    /// <summary>
    /// 协理合伙人a <see cref="GrainType"/> 具有 a 的 grain 类.
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
        /// grain 类型名称提供者。
        /// </param>
        /// <param name="argumentFormatter">
        /// 用于格式化通用参数的型号转换器。
        /// </param>
        public GrainTypeResolver(
            IEnumerable<IGrainTypeProvider> resolvers,
            TypeConverter argumentFormatter)
        {
            _providers = resolvers.ToArray();
            _typeConverter = argumentFormatter;
        }

        /// <summary>
        /// 返回所提供类的 grain 类型。
        /// </summary>
        /// <param name="type"> grain 类。</param>
        /// <returns>下 grain 类型,用于提供类。</returns>
        public GrainType GetGrainType(Type type)
        {
            if (!type.IsClass || type.IsAbstract)
            {
                throw new ArgumentException($"Argument {nameof(type)} must be a non-abstract class. Provided value, \"{type}\", is not a class.", nameof(type));
            }

            // 配置的提供者优先
            foreach (var provider in _providers)
            {
                if (provider.TryGetGrainType(type, out var grainType))
                {
                    grainType = AddGenericParameters(grainType, type);

                    return grainType;
                }
            }

            // 公约被用作后备
            return GetGrainTypeByConvention(type);
        }

        private GrainType GetGrainTypeByConvention(Type type)
        {
#pragma warning disable CA1308 // Lowercase grain type identifiers preserve the established routing contract.
            var name = type.Name.ToLowerInvariant();
#pragma warning restore CA1308

            // 外质通用均匀
            var index = name.IndexOf('`', StringComparison.Ordinal);
            if (index > 0)
            {
                name = name[..index];
            }

            // Trim "Grain" 后缀
            index = name.LastIndexOf(GrainSuffix, StringComparison.Ordinal);
            if (index > 0 && name.Length - index == GrainSuffix.Length)
            {
                name = name[..index];
            }

            // 附加通用正数, 等型(MyListGrain<T>) 将最终成为我的列表 ' 1
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
