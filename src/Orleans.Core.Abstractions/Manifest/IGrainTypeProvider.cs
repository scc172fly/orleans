using System;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Metadata
{
    /// <summary>
    /// 协理合伙人a <see cref="GrainType"/> 具有 a 的 grain 类.
    /// </summary>
    public interface IGrainTypeProvider
    {
        /// <summary>
        /// 返回 grain 与确定类别相对应的 <paramref name="type"/>.
        /// </summary>
        bool TryGetGrainType(Type type, out GrainType grainType);
    }

    /// <summary>
    /// 获得相应的 <see cref="GrainType"/> a 的a grain 从一个类中的 <see cref="Attribute"/>
    /// 执行该类 <see cref="IGrainTypeProviderAttribute"/> 。
    /// </summary>
    public class AttributeGrainTypeProvider : IGrainTypeProvider
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="AttributeGrainTypeProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务供应商。
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
    /// 可以通过习惯来发挥作用 <see cref="Attribute"/> 执行此指定 <see cref="GrainType"/> 会 议 排排排排排排排排排排排排排排排排排
    /// 类型。
    /// </summary>
    public interface IGrainTypeProviderAttribute
    {
        /// <summary>
        /// 得到 <see cref="GrainType"/> 所附附件 <see cref="Type"/>.
        /// </summary>
        /// <param name="services">
        /// 服务供应商。
        /// </param>
        /// <param name="type">
        /// 下 grain 类.
        /// </param>
        GrainType GetGrainType(IServiceProvider services, Type type);
    }
}

namespace Orleans
{
    /// <summary>
    /// 指定 grain 类型类型 grain 与该类相连接。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class GrainTypeAttribute : Attribute, IGrainTypeProviderAttribute
    {
        /// <summary>
        /// grain 类型名称。
        /// </summary>
        private readonly GrainType _grainType;

        /// <summary>
        /// 初始化 <see cref="GrainTypeAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="grainType">
        /// 非空白 grain 类型名称,不含引号、白空格、控制字符或保留型号名称分隔符(<c>[](),*&amp;</c>)。
        /// </param>
        /// <remarks>通用数后缀必须是每个嵌套类型名称结尾的十进制整数,总计最多64个</remarks>。
        /// <exception cref="ArgumentNullException"><paramref name="grainType"/> 是 <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="grainType"/> 是空的, 含有引号、 空白、 控制字符或保留类型名称分隔符, 或者无效的通用性等字尾。</exception>
        public GrainTypeAttribute(string grainType)
        {
            IdentityNameValidator.Validate(grainType, nameof(grainType));
            this._grainType = GrainType.Create(grainType);
        }

        /// <inheritdoc />
        public GrainType GetGrainType(IServiceProvider services, Type type) => this._grainType;
    }
}
