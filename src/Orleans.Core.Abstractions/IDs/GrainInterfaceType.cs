using System;
using Orleans.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 唯一标识一个 Grain 接口。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct GrainInterfaceType : IEquatable<GrainInterfaceType>, ISpanFormattable
    {
        /// <summary>
        /// 底层值。
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
        /// 返回此实例的底层 <see cref="IdSpan"/> 值。
        /// </summary>
        public IdSpan Value => _value;

        /// <summary>
        /// 如果此值与 <see langword="default"/> 实例相等，则返回 true。
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
        /// 返回将当前实例按 UTF-8 解码后的字符串表示形式。
        /// </summary>
        /// <returns></returns>
        public override string ToString() => _value.ToString();

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString() ?? "";

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => _value.TryFormat(destination, out charsWritten);

        /// <summary>
        /// 比较提供的操作数是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator ==(GrainInterfaceType left, GrainInterfaceType right) => left.Equals(right);

        /// <summary>
        /// 比较提供的操作数是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>如果提供的值不相等，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool operator !=(GrainInterfaceType left, GrainInterfaceType right) => !left.Equals(right);
    }

    /// <summary>
    /// 获取接口对应的 <see cref="GrainInterfaceType"/>。
    /// </summary>
    public interface IGrainInterfaceTypeProvider
    {
        /// <summary>
        /// 获取 <see cref="GrainInterfaceType"/>，其对应指定的 <paramref name="type"/>。
        /// </summary>
        /// <param name="type">Grain 接口类型实例。</param>
        /// <param name="grainInterfaceType">所得的 Grain 接口类型标识符。</param>
        /// <returns>
        /// 如果找到对应于所提供类型的标识，则返回 <see langword="true"/>（标识类型为 <see cref="GrainInterfaceType"/>），否则返回 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// 在使用此提供程序的解析器的整个生存期内，实现必须为每个 CLR 接口类型
        /// 返回稳定的成功结果及标识符。解析器缓存已解析的标识，
        /// 包括封闭泛型接口类型。实现必须支持并发调用；
        /// 并发缓存未命中时，可能会针对同一类型多次调用此方法。
        /// </remarks>
        bool TryGetGrainInterfaceType(Type type, out GrainInterfaceType grainInterfaceType);
    }

    /// <summary>
    /// 获取 <see cref="GrainInterfaceType"/>，其来自实现了 <see cref="IGrainInterfaceTypeProviderAttribute"/> 的特性。
    /// </summary>
    public class AttributeGrainInterfaceTypeProvider : IGrainInterfaceTypeProvider
    {
        /// <summary>
        /// 服务提供程序。
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
    /// 实现此接口的 <see cref="Attribute"/> 指定 <see cref="GrainInterfaceType"/>，
    /// 即该特性所附加类型的接口标识。
    /// </summary>
    public interface IGrainInterfaceTypeProviderAttribute
    {
        /// <summary>
        /// 获取 Grain 接口标识符。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="type">Grain 接口类型。</param>
        /// <returns>
        /// 对应所提供类型的 <see cref="GrainInterfaceType"/>。
        /// </returns>
        /// <remarks>
        /// 在使用此特性的解析器的整个生存期内，每个 CLR 接口类型的标识符必须保持稳定。
        /// 实现必须支持并发调用，包括在并发解析标识期间
        /// 针对同一类型的重复调用。
        /// </remarks>
        GrainInterfaceType GetGrainInterfaceType(IServiceProvider services, Type type);
    }

    /// <summary>
    /// 应用于 Grain 接口时，指定 <see cref="GrainInterfaceType"/>。
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
    public sealed class GrainInterfaceTypeAttribute : Attribute, IGrainInterfaceTypeProviderAttribute
    {
        /// <summary>
        /// Grain 接口类型。
        /// </summary>
        private readonly GrainInterfaceType _value;

        /// <summary>
        /// 创建 <see cref="GrainInterfaceTypeAttribute"/> 实例。
        /// </summary>
        /// <param name="value">非空标识，不包含引号、空白字符、控制字符或保留的类型名称分隔符（<c>[](),*&amp;</c>）。</param>
        /// <remarks>表示泛型参数个数的后缀必须是位于每个嵌套类型名称末尾的十进制整数，参数个数合计最多为 64。</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentException"><paramref name="value"/> 为空，包含引号、空白字符、控制字符或保留的类型名称分隔符，或者具有无效的泛型参数个数后缀。</exception>
        public GrainInterfaceTypeAttribute(string value)
        {
            IdentityNameValidator.Validate(value, nameof(value));
            _value = GrainInterfaceType.Create(value);
        }

        /// <inheritdoc />
        public GrainInterfaceType GetGrainInterfaceType(IServiceProvider services, Type type) => _value;
    }
}
