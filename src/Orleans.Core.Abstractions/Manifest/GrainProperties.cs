#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Orleans.Runtime;

namespace Orleans.Metadata
{
    /// <summary>
    /// 有关逻辑 Grain 类型 <see cref="GrainType"/> 的信息。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class GrainProperties : IEquatable<GrainProperties>
    {
        [NonSerialized]
        private int? _hashCode;

        /// <summary>
        /// 初始化 <see cref="GrainProperties"/> 类的新实例。
        /// </summary>
        /// <param name="values">
        /// 底层属性集合。
        /// </param>
        public GrainProperties(ImmutableDictionary<string, string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            EnsureOrdinalKeyComparer(values, nameof(values));
            this.Properties = values.WithComparers(StringComparer.Ordinal, StringComparer.Ordinal);
            Debug.Assert(HasOrdinalComparers(this.Properties));
        }

        /// <summary>
        /// 获取属性。
        /// </summary>
        [Id(0)]
        public ImmutableDictionary<string, string> Properties { get; }

        /// <summary>
        /// 返回此实例的详细字符串表示。
        /// </summary>
        /// <returns>
        /// 此实例的详细字符串表示。
        /// </returns>
        public string ToDetailedString()
        {
            if (Properties is null) return string.Empty;
            var result = new StringBuilder("[");
            bool first = true;
            foreach (var entry in Properties)
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
        public override bool Equals(object? obj) => obj is GrainProperties other && Equals(other);

        /// <inheritdoc/>
        public bool Equals(GrainProperties? other)
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
    /// 预定义的 Grain 属性。
    /// </summary>
    /// <seealso cref="GrainProperties"/>
    public static class WellKnownGrainTypeProperties
    {
        /// <summary>
        /// 此类型 Grain 所使用的放置策略名称。
        /// </summary>
        public const string PlacementStrategy = "placement-strategy";

        /// <summary>
        /// 此类型 Grain 所使用的放置策略名称。
        /// </summary>
        public const string PlacementFilter = "placement-filter";

        /// <summary>
        /// 此类型 Grain 所使用的目录策略。
        /// </summary>
        public const string GrainDirectory = "directory-policy";

        /// <summary>
        /// 发送给此 Grain 的消息是否不要求顺序。
        /// </summary>
        public const string Unordered = "unordered";

        /// <summary>
        /// 用于标识 Grain 类所实现接口的 <see cref="GrainInterfaceType"/> 的键前缀。
        /// </summary>
        public const string ImplementedInterfacePrefix = "interface.";

        /// <summary>
        /// 空闲 Grain 在经过此时间间隔后将被停用。
        /// </summary>
        public const string IdleDeactivationPeriod = "idle-duration";

        /// <summary>
        /// <see cref="IdleDeactivationPeriod"/> 的取值，用于指定 Grain 不应因空闲而停用。
        /// </summary>
        public const string IndefiniteIdleDeactivationPeriodValue = "indefinite";

        /// <summary>
        /// 主实现类型名称，用于按约定匹配接口的主实现。
        /// </summary>
        public const string TypeName = "type-name";

        /// <summary>
        /// 主实现类型的完整名称，用于按前缀匹配实现。
        /// </summary>
        public const string FullTypeName = "full-type-name";

        /// <summary>
        /// 绑定声明的前缀。
        /// </summary>
        public const string BindingPrefix = "binding";

        /// <summary>
        /// 定义绑定类型的键。
        /// </summary>
        public const string BindingTypeKey = "type";

        /// <summary>
        /// Orleans 流的绑定类型。
        /// </summary>
        public const string StreamBindingTypeValue = "stream";

        /// <summary>
        /// 广播通道的绑定类型。
        /// </summary>
        public const string BroadcastChannelBindingTypeValue = "broadcast-channel";

        /// <summary>
        /// 指定流绑定模式的键。
        /// </summary>
        public const string StreamBindingPatternKey = "pattern";

        /// <summary>
        /// 指定通道绑定模式的键。
        /// </summary>
        public const string BroadcastChannelBindingPatternKey = "channel-pattern";

        /// <summary>
        /// 指定流 ID 映射器的键。
        /// </summary>
        public const string StreamIdMapperKey = "streamid-mapper";

        /// <summary>
        /// 指定通道 ID 映射器的键。
        /// </summary>
        public const string ChannelIdMapperKey = "channelid-mapper";

        /// <summary>
        /// Grain ID 中是否包含命名空间名称。
        /// </summary>
        public const string StreamBindingIncludeNamespaceKey = "include-namespace";

        /// <summary>
        /// Grain 实现旧版接口时使用的键类型。有效值为 nameof(String)、nameof(Int64) 和 nameof(Guid)。
        /// </summary>
        public const string LegacyGrainKeyType = "legacy-grain-key-type";

        /// <summary>
        /// Grain 是否可重入。
        /// </summary>
        public const string Reentrant = "reentrant";

        /// <summary>
        /// 指定用于判断请求能否与其他请求交错执行的方法名称。
        /// </summary>
        public const string MayInterleavePredicate = "may-interleave-predicate";

        /// <summary>
        /// Grain 是否可以通过主动再均衡进行迁移。
        /// </summary>
        public const string Immovable = "immovable";
    }

    /// <summary>
    /// 提供 Grain 属性。
    /// </summary>
    public interface IGrainPropertiesProvider
    {
        /// <summary>
        /// 向 <paramref name="properties"/> 添加 Grain 属性。
        /// </summary>
        /// <param name="grainClass">
        /// Grain 类。
        /// </param>
        /// <param name="grainType">
        /// Grain 类型 ID。
        /// </param>
        /// <param name="properties">
        /// 调用此方法时应填充的属性集合。
        /// </param>
        void Populate(Type grainClass, GrainType grainType, Dictionary<string, string> properties);
    }

    /// <summary>
    /// 为 Grain 提供信息的 <see cref="Attribute"/> 类所实现的接口。
    /// </summary>
    public interface IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 向 <paramref name="properties"/> 添加 Grain 属性。
        /// </summary>
        /// <remarks>
        /// <see cref="AttributeGrainPropertiesProvider"/> 会在将调用分派给特性之前验证 <paramref name="grainClass"/> 和
        /// <paramref name="properties"/> 不为 null。直接调用者也必须
        /// 提供非 null 的 Grain 类和属性集合。
        /// </remarks>
        /// <param name="services">
        /// 服务提供程序。
        /// </param>
        /// <param name="grainClass">
        /// Grain 类。
        /// </param>
        /// <param name="grainType">
        /// Grain 类型 ID。
        /// </param>
        /// <param name="properties">
        /// 调用此方法时应填充的属性集合。
        /// </param>
        void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties);
    }

    /// <summary>
    /// 从实现 <see cref="IGrainPropertiesProviderAttribute"/> 的特性获取 Grain 接口属性。
    /// </summary>
    public sealed class AttributeGrainPropertiesProvider : IGrainPropertiesProvider
    {
        private readonly IServiceProvider serviceProvider;

        /// <summary>
        /// 初始化 <see cref="AttributeGrainPropertiesProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务提供程序。
        /// </param>
        public AttributeGrainPropertiesProvider(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="grainClass"/> 或 <paramref name="properties"/> 为 <see langword="null"/>。</exception>
        public void Populate(Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            ArgumentNullException.ThrowIfNull(grainClass);
            ArgumentNullException.ThrowIfNull(properties);
            foreach (var attr in grainClass.GetCustomAttributes(inherit: true))
            {
                if (attr is IGrainPropertiesProviderAttribute providerAttribute)
                {
                    providerAttribute.Populate(this.serviceProvider, grainClass, grainType, properties);
                }
            }
        }
    }

    /// <summary>
    /// 为 Grain 提供信息的 <see cref="Attribute"/> 类所实现的接口。
    /// </summary>
    public interface IGrainBindingsProviderAttribute
    {
        /// <summary>
        /// 获取此特性所附加类型的绑定。
        /// </summary>
        /// <param name="services">
        /// 服务提供程序。
        /// </param>
        /// <param name="grainClass">
        /// Grain 类。
        /// </param>
        /// <param name="grainType">
        /// Grain 类型。
        /// </param>
        /// <returns>
        /// 指定 Grain 的绑定。
        /// </returns>
        IEnumerable<Dictionary<string, string?>> GetBindings(IServiceProvider services, Type grainClass, GrainType grainType);
    }

    /// <summary>
    /// 从实现 <see cref="IGrainPropertiesProviderAttribute"/> 的特性获取 Grain 接口属性。
    /// </summary>
    public sealed class AttributeGrainBindingsProvider : IGrainPropertiesProvider
    {
        /// <summary>
        /// 用于描述此提供程序所添加绑定的名称，应尽量保证唯一。
        /// 绑定名称本身没有语义，仅用于将同一绑定的属性归为一组。
        /// </summary>
        private const string BindingPrefix = WellKnownGrainTypeProperties.BindingPrefix + ".attr-";
        private readonly IServiceProvider serviceProvider;

        /// <summary>
        /// 初始化 <see cref="AttributeGrainBindingsProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务提供程序。
        /// </param>
        public AttributeGrainBindingsProvider(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="grainClass"/> 或 <paramref name="properties"/> 为 <see langword="null"/>。</exception>
        public void Populate(Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            ArgumentNullException.ThrowIfNull(grainClass);
            ArgumentNullException.ThrowIfNull(properties);
            var bindingIndex = 1;
            foreach (var attr in grainClass.GetCustomAttributes(inherit: true))
            {
                if (!(attr is IGrainBindingsProviderAttribute providerAttribute))
                {
                    continue;
                }

                foreach (var binding in providerAttribute.GetBindings(this.serviceProvider, grainClass, grainType))
                {
                    foreach (var pair in binding)
                    {
                        properties[BindingPrefix + bindingIndex.ToString(CultureInfo.InvariantCulture) + '.' + pair.Key] = pair.Value!; // null 是旧版用于表示默认映射器的哨兵值。
                    }

                    ++bindingIndex;
                }
            }
        }
    }
}
