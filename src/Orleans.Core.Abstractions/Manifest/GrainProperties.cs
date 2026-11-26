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
    /// 关于逻辑 grain 类型 <see cref="GrainType"/>的信息。
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
        /// 基本财产收集。
        /// </param>
        public GrainProperties(ImmutableDictionary<string, string> values)
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
    /// 众所周知的 grain 属性。
    /// </summary>
    /// <seealso cref="GrainProperties"/>
    public static class WellKnownGrainTypeProperties
    {
        /// <summary>
        /// 这类 grains 的安置策略的名称。
        /// </summary>
        public const string PlacementStrategy = "placement-strategy";

        /// <summary>
        /// 这类 grains 的安置策略的名称。
        /// </summary>
        public const string PlacementFilter = "placement-filter";

        /// <summary>
        /// 此类型 grains 的目录策略 。
        /// </summary>
        public const string GrainDirectory = "directory-policy";

        /// <summary>
        /// 是否未对给此 grain 的信件进行排序 。
        /// </summary>
        public const string Unordered = "unordered";

        /// <summary>
        /// 显示 <see cref="GrainInterfaceType"/> 界面的接口, grain 类工具。
        /// </summary>
        public const string ImplementedInterfacePrefix = "interface.";

        /// <summary>
        /// 闲置的 grain 将在该期限之后停止使用。
        /// </summary>
        public const string IdleDeactivationPeriod = "idle-duration";

        /// <summary>
        /// 值的数值 <see cref="IdleDeactivationPeriod"/> 用于指定 grain 不应因闲置而停止使用。
        /// </summary>
        public const string IndefiniteIdleDeactivationPeriodValue = "indefinite";

        /// <summary>
        /// 主要执行类型的名称:用于在公约基础上对初级接口实施进行匹配。
        /// </summary>
        public const string TypeName = "type-name";

        /// <summary>
        /// 主要执行类型的全名。 用于基于前缀的比对执行 。
        /// </summary>
        public const string FullTypeName = "full-type-name";

        /// <summary>
        /// 具有约束力的声明的前缀
        /// </summary>
        public const string BindingPrefix = "binding";

        /// <summary>
        /// 确定约束型的关键。
        /// </summary>
        public const string BindingTypeKey = "type";

        /// <summary>
        /// Orleans 流的绑定类型。
        /// </summary>
        public const string StreamBindingTypeValue = "stream";

        /// <summary>
        /// 广播频道的绑定类型 。
        /// </summary>
        public const string BroadcastChannelBindingTypeValue = "broadcast-channel";

        /// <summary>
        /// 指定串流绑定模式的密钥。
        /// </summary>
        public const string StreamBindingPatternKey = "pattern";

        /// <summary>
        /// 指定频道绑定模式的密钥 。
        /// </summary>
        public const string BroadcastChannelBindingPatternKey = "channel-pattern";

        /// <summary>
        /// 指定流 ID 映像仪的密钥
        /// </summary>
        public const string StreamIdMapperKey = "streamid-mapper";

        /// <summary>
        /// 指定频道 ID 映像仪的密钥
        /// </summary>
        public const string ChannelIdMapperKey = "channelid-mapper";

        /// <summary>
        /// 是否在 grain id 中包含命名空间名称 。
        /// </summary>
        public const string StreamBindingIncludeNamespaceKey = "include-namespace";

        /// <summary>
        /// grain的密钥类型, 如果它执行了遗留界面。 有效的值是名称( String)、 名称( Int64) 和名称( 指南)
        /// </summary>
        public const string LegacyGrainKeyType = "legacy-grain-key-type";

        /// <summary>
        /// 是否重新输入一个 grain 。
        /// </summary>
        public const string Reentrant = "reentrant";

        /// <summary>
        /// 指定用于确定请求能否将其他请求打断的方法的名称。
        /// </summary>
        public const string MayInterleavePredicate = "may-interleave-predicate";

        /// <summary>
        /// 是否可以通过主动平衡来迁移 grain 。
        /// </summary>
        public const string Immovable = "immovable";
    }

    /// <summary>
    /// 提供 grain 属性。
    /// </summary>
    public interface IGrainPropertiesProvider
    {
        /// <summary>
        /// 将 grain 属性添加到 <paramref name="properties"/>。
        /// </summary>
        /// <param name="grainClass">
        /// 下 grain 类.
        /// </param>
        /// <param name="grainType">
        /// 类型 grain 代号。
        /// </param>
        /// <param name="properties">
        /// 使用此方法的属性收藏应该以空格形式显示 。
        /// </param>
        void Populate(Type grainClass, GrainType grainType, Dictionary<string, string> properties);
    }

    /// <summary>
    /// <see cref="Attribute"/> 类的接口,该类别提供关于 grain的信息。
    /// </summary>
    public interface IGrainPropertiesProviderAttribute
    {
        /// <summary>
        /// 将 grain 属性添加到 <paramref name="properties"/>。
        /// </summary>
        /// <remarks>
        /// <see cref="AttributeGrainPropertiesProvider"/> 验证 <paramref name="grainClass"/> 和
        /// <paramref name="properties"/> 是在发送属性前不中点的。
        /// 还提供了非null grain 级和财产收藏。
        /// </remarks>
        /// <param name="services">
        /// 服务供应商。
        /// </param>
        /// <param name="grainClass">
        /// 下 grain 类.
        /// </param>
        /// <param name="grainType">
        /// 类型 grain 代号。
        /// </param>
        /// <param name="properties">
        /// 使用此方法的属性收藏应该以空格形式显示 。
        /// </param>
        void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties);
    }

    /// <summary>
    /// 提供 grain 来自执行属性的界面属性 <see cref="IGrainPropertiesProviderAttribute"/>.
    /// </summary>
    public sealed class AttributeGrainPropertiesProvider : IGrainPropertiesProvider
    {
        private readonly IServiceProvider serviceProvider;

        /// <summary>
        /// 初始化 <see cref="AttributeGrainPropertiesProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务供应商。
        /// </param>
        public AttributeGrainPropertiesProvider(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="grainClass"/> 或 <paramref name="properties"/> 是 <see langword="null"/>。</exception>
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
    /// <see cref="Attribute"/> 类的接口,该类别提供关于 grain的信息。
    /// </summary>
    public interface IGrainBindingsProviderAttribute
    {
        /// <summary>
        /// 获取该属性附加的类型的绑定。
        /// </summary>
        /// <param name="services">
        /// 服务供应商。
        /// </param>
        /// <param name="grainClass">
        /// 下 grain 类.
        /// </param>
        /// <param name="grainType">
        /// 类型 grain 。
        /// </param>
        /// <returns>
        /// 指定 grain的绑定。
        /// </returns>
        IEnumerable<Dictionary<string, string?>> GetBindings(IServiceProvider services, Type grainClass, GrainType grainType);
    }

    /// <summary>
    /// 提供 grain 来自执行属性的界面属性 <see cref="IGrainPropertiesProviderAttribute"/>.
    /// </summary>
    public sealed class AttributeGrainBindingsProvider : IGrainPropertiesProvider
    {
        /// <summary>
        /// 希望能够有一个独有的名称来描述这个提供者添加的约束力。
        /// 装订名称毫无意义,仅用于将某一约束性组合在一起的属性分组。
        /// </summary>
        private const string BindingPrefix = WellKnownGrainTypeProperties.BindingPrefix + ".attr-";
        private readonly IServiceProvider serviceProvider;

        /// <summary>
        /// 初始化 <see cref="AttributeGrainBindingsProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务供应商。
        /// </param>
        public AttributeGrainBindingsProvider(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="grainClass"/> 或 <paramref name="properties"/> 是 <see langword="null"/>。</exception>
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
                        properties[BindingPrefix + bindingIndex.ToString(CultureInfo.InvariantCulture) + '.' + pair.Key] = pair.Value!; // Null 是默认映像仪的遗留监控器 。
                    }

                    ++bindingIndex;
                }
            }
        }
    }
}
