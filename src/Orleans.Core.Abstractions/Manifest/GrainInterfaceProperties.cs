#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Orleans.Runtime;

namespace Orleans.Metadata
{
    /// <summary>
    /// 关于通信界面的信息。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class GrainInterfaceProperties : IEquatable<GrainInterfaceProperties>
    {
        [NonSerialized]
        private int? _hashCode;

        /// <summary>
        /// 初始化 <see cref="GrainInterfaceProperties"/> 类的新实例。
        /// </summary>
        /// <param name="values">
        /// 界面属性值。
        /// </param>
        public GrainInterfaceProperties(ImmutableDictionary<string, string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            EnsureOrdinalKeyComparer(values, nameof(values));
            this.Properties = values.WithComparers(StringComparer.Ordinal, StringComparer.Ordinal);
            Debug.Assert(HasOrdinalComparers(this.Properties));
        }

        /// <summary>
        /// 得到属性。
        /// </summary>
        [Id(0)]
        public ImmutableDictionary<string, string> Properties { get; }

        /// <summary>
        /// 返回此实例的详细字符串表示。
        /// </summary>
        /// <returns>
        /// 此实例的详细、 字符串表示 。
        /// </returns>
        public string ToDetailedString()
        {
            if (this.Properties is null) return string.Empty;
            var result = new StringBuilder("[");
            bool first = true;
            foreach (var entry in this.Properties)
            {
                if (!first)
                {
                    result.Append(", ");
                }

                result.Append($"\"{entry.Key}\": \"{entry.Value}\"");
                first = false;
            }
            result.Append("]");

            return result.ToString();
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is GrainInterfaceProperties other && Equals(other);

        /// <inheritdoc/>
        public bool Equals(GrainInterfaceProperties? other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            return PropertiesEqual(Properties, other.Properties);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => _hashCode ??= ComputeHashCode(Properties);

        private static bool PropertiesEqual(ImmutableDictionary<string, string> left, ImmutableDictionary<string, string> right)
        {
            Debug.Assert(HasOrdinalKeyComparer(left));
            Debug.Assert(HasOrdinalKeyComparer(right));
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var entry in left)
            {
                if (!right.TryGetValue(entry.Key, out var value)
                    || !string.Equals(entry.Value, value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasOrdinalComparers(ImmutableDictionary<string, string> properties)
            => ReferenceEquals(properties.KeyComparer, StringComparer.Ordinal)
                && ReferenceEquals(properties.ValueComparer, StringComparer.Ordinal);

        private static bool HasOrdinalKeyComparer(ImmutableDictionary<string, string> properties)
            => ReferenceEquals(properties.KeyComparer, StringComparer.Ordinal);

        private static void EnsureOrdinalKeyComparer(ImmutableDictionary<string, string> properties, string paramName)
        {
            if (!HasOrdinalKeyComparer(properties))
            {
                throw new ArgumentException("The dictionary must use StringComparer.Ordinal as its key comparer.", paramName);
            }
        }

        private static int ComputeHashCode(ImmutableDictionary<string, string> properties)
        {
            var hash = 0;
            foreach (var entry in properties)
            {
                hash ^= HashCode.Combine(
                    StringComparer.Ordinal.GetHashCode(entry.Key),
                    entry.Value is null ? 0 : StringComparer.Ordinal.GetHashCode(entry.Value));
            }

            return hash;
        }
    }

    /// <summary>
    /// 众所周知的 grain 接口属性密钥。
    /// </summary>
    /// <seealso cref="GrainInterfaceProperties"/>
    public static class WellKnownGrainInterfaceProperties
    {
        /// <summary>
        /// 此界面的版本以小数整数编码。
        /// </summary>
        public const string Version = "version";

        /// <summary>
        /// 编码 <see cref="GrainType"/> 与接口的初始实施相对应。
        /// 此选项用于从接口中解析 grain 类型。
        /// </summary>
        public const string DefaultGrainType = "primary-grain-type";

        /// <summary>
        /// 此接口类型的名称。 用于基于公约的初级执行匹配 。
        /// </summary>
        public const string TypeName = "type-name";
    }

    /// <summary>
    /// 提供 grain 属性。
    /// </summary>
    public interface IGrainInterfacePropertiesProvider
    {
        /// <summary>
        /// 将 grain 接口属性添加到 <paramref name="properties"/>。
        /// </summary>
        /// <param name="interfaceType">
        /// 界面类型。
        /// </param>
        /// <param name="grainInterfaceType">
        /// 界面类型 ID 。
        /// </param>
        /// <param name="properties">
        /// 此方法所要求的属性收藏应该以空格形式显示 。
        /// </param>
        void Populate(Type interfaceType, GrainInterfaceType grainInterfaceType, Dictionary<string, string> properties);
    }

    /// <summary>
    /// <see cref="Attribute"/> 类的接口,该类别提供关于 grain 界面的信息。
    /// </summary>
    public interface IGrainInterfacePropertiesProviderAttribute
    {
        /// <summary>
        /// 将 grain 接口属性添加到 <paramref name="properties"/>。
        /// </summary>
        /// <param name="services">
        /// 服务供应商。
        /// </param>
        /// <param name="interfaceType">
        /// 界面类型。
        /// </param>
        /// <param name="properties">
        /// 此方法所要求的属性收藏应该以空格形式显示 。
        /// </param>
        void Populate(IServiceProvider services, Type interfaceType, Dictionary<string, string> properties);
    }

    /// <summary>
    /// 提供 grain 来自执行属性的界面属性 <see cref="IGrainInterfacePropertiesProviderAttribute"/>.
    /// </summary>
    internal sealed class AttributeGrainInterfacePropertiesProvider : IGrainInterfacePropertiesProvider
    {
        private readonly IServiceProvider serviceProvider;

        /// <summary>
        /// 初始化 <see cref="AttributeGrainInterfacePropertiesProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务供应商。
        /// </param>
        public AttributeGrainInterfacePropertiesProvider(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public void Populate(Type interfaceType, GrainInterfaceType grainInterfaceType, Dictionary<string, string> properties)
        {
            foreach (var attr in interfaceType.GetCustomAttributes(inherit: true))
            {
                if (attr is IGrainInterfacePropertiesProviderAttribute providerAttribute)
                {
                    providerAttribute.Populate(this.serviceProvider, interfaceType, properties);
                }
            }
        }
    }

    /// <summary>
    /// 指定默认值 grain 构造 grain 此界面的引用引用, 无需指定 grain 类型。
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
    public sealed class DefaultGrainTypeAttribute : Attribute, IGrainInterfacePropertiesProviderAttribute
    {
        private readonly string grainType;

        /// <summary>
        /// 初始化 <see cref="DefaultGrainTypeAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="grainType">
        /// 类型 grain 。
        /// </param>
        public DefaultGrainTypeAttribute(string grainType)
        {
            this.grainType = grainType;
        }

        /// <inheritdoc />
        void IGrainInterfacePropertiesProviderAttribute.Populate(IServiceProvider services, Type type, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainInterfaceProperties.DefaultGrainType] = this.grainType;
        }
    }
}
