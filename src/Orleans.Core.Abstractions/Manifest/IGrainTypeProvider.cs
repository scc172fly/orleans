using System;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Metadata
{
    /// <summary>
    /// 将 <see cref="GrainType"/> 与 Grain 类关联。
    /// </summary>
    public interface IGrainTypeProvider
    {
        /// <summary>
        /// 返回与 <paramref name="type"/> 所标识的类对应的 Grain 类型。
        /// </summary>
        bool TryGetGrainType(Type type, out GrainType grainType);
    }

    /// <summary>
    /// 获取 Grain 类对应的 <see cref="GrainType"/>，其来源为该类上的 <see cref="Attribute"/>，
    /// 该特性实现了 <see cref="IGrainTypeProviderAttribute"/>。
    /// </summary>
    public class AttributeGrainTypeProvider : IGrainTypeProvider
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="AttributeGrainTypeProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务提供程序。
        /// </param>
        public AttributeGrainTypeProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public bool TryGetGrainType(Type grainClass, out GrainType grainType)
        {
            foreach (var attr in grainClass.GetCustomAttributes(inherit: false))
            {
                if (attr is IGrainTypeProviderAttribute typeProviderAttribute)
                {
                    grainType = typeProviderAttribute.GetGrainType(this._serviceProvider, grainClass);
                    return true;
                }
            }

            grainType = default;
            return false;
        }
    }

    /// <summary>
    /// 自定义 <see cref="Attribute"/> 可实现此功能，用于指定 <see cref="GrainType"/>，
    /// 即该特性所附加类型的 Grain 类型。
    /// </summary>
    public interface IGrainTypeProviderAttribute
    {
        /// <summary>
        /// 获取 <see cref="GrainType"/>（所附加的 <see cref="Type"/> 对应的 Grain 类型）。
        /// </summary>
        /// <param name="services">
        /// 服务提供程序。
        /// </param>
        /// <param name="type">
        /// Grain 类。
        /// </param>
        GrainType GetGrainType(IServiceProvider services, Type type);
    }
}

namespace Orleans
{
    /// <summary>
    /// 指定此特性所附加的 Grain 类的 Grain 类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class GrainTypeAttribute : Attribute, IGrainTypeProviderAttribute
    {
        /// <summary>
        /// Grain 类型名称。
        /// </summary>
        private readonly GrainType _grainType;

        /// <summary>
        /// 初始化 <see cref="GrainTypeAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="grainType">
        /// 非空 Grain 类型名称，不得包含引号、空白字符、控制字符或保留的类型名称分隔符（<c>[](),*&amp;</c>）。
        /// </param>
        /// <remarks>表示泛型参数个数的后缀必须是位于每个嵌套类型名称末尾的十进制整数，参数个数合计最多为 64。</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grainType"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentException"><paramref name="grainType"/> 为空、包含引号、空白字符、控制字符或保留的类型名称分隔符，或者包含无效的泛型参数个数后缀。</exception>
        public GrainTypeAttribute(string grainType)
        {
            IdentityNameValidator.Validate(grainType, nameof(grainType));
            this._grainType = GrainType.Create(grainType);
        }

        /// <inheritdoc />
        public GrainType GetGrainType(IServiceProvider services, Type type) => this._grainType;
    }
}
