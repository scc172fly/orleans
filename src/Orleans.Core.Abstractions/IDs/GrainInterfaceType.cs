using System;
using Orleans.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 唯一指定 grain 接口。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct GrainInterfaceType : IEquatable<GrainInterfaceType>, ISpanFormattable
    {
        /// <summary>
        /// 基本价值。
        /// </summary>
        [Id(0)]
        private readonly IdSpan _value;

        /// <summary>
        /// 创建 <see cref="GrainInterfaceType"/> 实例。
        /// </summary>
        public GrainInterfaceType(string value)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(value);
            _value = IdSpan.Create(value);
        }

        /// <summary>
        /// 创建 <see cref="GrainInterfaceType"/> 实例。
        /// </summary>
        public GrainInterfaceType(IdSpan value) => _value = value;

        /// <summary>
        /// 返回此实例的 <see cref="IdSpan"/> 值。
        /// </summary>
        public IdSpan Value => _value;

        /// <summary>
        /// 如果此值等于 <see langword="default"/> 实例,则返回 True。
        /// </summary>
        public bool IsDefault => _value.IsDefault;

        /// <summary>
        /// 创建 <see cref="GrainInterfaceType"/> 实例。
        /// </summary>
        public static GrainInterfaceType Create(string value) => new GrainInterfaceType(value);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GrainInterfaceType id && Equals(id);

        /// <inheritdoc />
        public bool Equals(GrainInterfaceType other) => _value.Equals(other._value);

        /// <inheritdoc />
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>
        /// 返回当前实例的 UTF8 解释。
        /// </summary>
        /// <returns></returns>
        public override string ToString() => _value.ToString();

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString() ?? "";

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => _value.TryFormat(destination, out charsWritten);

        /// <summary>
        /// 与所提供的平等目标相比较。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果规定的数值相等,否则 <see langword="false"/>。</returns>
        public static bool operator ==(GrainInterfaceType left, GrainInterfaceType right) => left.Equals(right);

        /// <summary>
        /// 比较所提供的不平等域。
        /// </summary>
        /// <param name="left">左侧操作。</param>
        /// <param name="right">右侧操作。</param>
        /// <returns><see langword="true"/> 如果所提供的数值不相等,否则 <see langword="false"/>.</returns>
        public static bool operator !=(GrainInterfaceType left, GrainInterfaceType right) => !left.Equals(right);
    }

    /// <summary>
    /// 获取一个接口的 <see cref="GrainInterfaceType"/> 。
    /// </summary>
    public interface IGrainInterfaceTypeProvider
    {
        /// <summary>
        /// 得到 <see cref="GrainInterfaceType"/> 对应指定 <paramref name="type"/>.
        /// </summary>
        /// <param name="type"> grain 接口类型实例。</param>
        /// <param name="grainInterfaceType">由此结果 grain 界面类型识别符号。</param>
        /// <returns>
        /// <see langword="true"/> ,如果发现与规定类型相应的 <see cref="GrainInterfaceType"/> ,否则 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// 实施必须恢复稳定的成功结果和每个CLR接口类型标识符
        /// 使用此提供者的解决者寿命。 解决者缓存解决了身份,
        /// 包括封闭通用接口类型。 执行必须支持同时通话;
        /// 同步缓存误差可多次对同类型使用此方法 。
        /// </remarks>
        bool TryGetGrainInterfaceType(Type type, out GrainInterfaceType grainInterfaceType);
    }

    /// <summary>
    /// 得到a <see cref="GrainInterfaceType"/> 属性执行中的属性 <see cref="IGrainInterfaceTypeProviderAttribute"/>.
    /// </summary>
    public class AttributeGrainInterfaceTypeProvider : IGrainInterfaceTypeProvider
    {
        /// <summary>
        /// 服务供应商。
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 创建 <see cref="AttributeGrainInterfaceTypeProvider"/> 实例。
        /// </summary>
        public AttributeGrainInterfaceTypeProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public bool TryGetGrainInterfaceType(Type type, out GrainInterfaceType grainInterfaceType)
        {
            foreach (var attr in type.GetCustomAttributes(inherit: false))
            {
                if (attr is IGrainInterfaceTypeProviderAttribute provider)
                {
                    grainInterfaceType = provider.GetGrainInterfaceType(this._serviceProvider, type);
                    return true;
                }
            }

            grainInterfaceType = default;
            return false;
        }
    }

    /// <summary>
    /// A 音 A <see cref="Attribute"/> 执行此指定 <see cref="GrainInterfaceType"/> 会 议 排排排排排排排排排排排排排排排排排
    /// 类型。
    /// </summary>
    public interface IGrainInterfaceTypeProviderAttribute
    {
        /// <summary>
        /// 获取 grain 接口标识符。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="type"> grain 接口类型。</param>
        /// <returns>
        /// 与所提供类型相应的 <see cref="GrainInterfaceType"/> 。
        /// </returns>
        /// <remarks>
        /// 解决者寿命期间,每个CLR接口类型的身份识别特征必须保持稳定
        /// 执行必须支持同时通话,包括重复通话
        /// 同一类型身份识别时,可同时使用。
        /// </remarks>
        GrainInterfaceType GetGrainInterfaceType(IServiceProvider services, Type type);
    }

    /// <summary>
    /// 当应用到 grain 接口时,指定 <see cref="GrainInterfaceType"/>。
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
    public sealed class GrainInterfaceTypeAttribute : Attribute, IGrainInterfaceTypeProviderAttribute
    {
        /// <summary>
        /// grain 接口类型。
        /// </summary>
        private readonly GrainInterfaceType _value;

        /// <summary>
        /// 创建 <see cref="GrainInterfaceTypeAttribute"/> 实例。
        /// </summary>
        /// <param name="value">不含引号、空白空间、控制字符或保留类型名称分隔符的非空白身份(<c>[](),*&amp;</c>).</param>
        /// <remarks>通用数后缀必须是每个嵌套类型名称结尾的十进制整数,总计最多64个</remarks>。
        /// <exception cref="ArgumentNullException"><paramref name="value"/> 是 <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="value"/> 是空的, 含有引号、 空白、 控制字符或保留类型名称分隔符, 或者无效的通用性等字尾。</exception>
        public GrainInterfaceTypeAttribute(string value)
        {
            IdentityNameValidator.Validate(value, nameof(value));
            _value = GrainInterfaceType.Create(value);
        }

        /// <inheritdoc />
        public GrainInterfaceType GetGrainInterfaceType(IServiceProvider services, Type type) => _value;
    }
}
