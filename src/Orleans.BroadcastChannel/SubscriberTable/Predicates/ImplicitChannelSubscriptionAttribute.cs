using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.BroadcastChannel;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// [Orleans.ImplicitStreamSubscription] 特性用于将 Grain 标记为隐式流订阅者。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ImplicitChannelSubscriptionAttribute : Attribute, IGrainBindingsProviderAttribute
    {
        /// <summary>
        /// 获取流命名空间筛选谓词。
        /// </summary>
        public IChannelNamespacePredicate Predicate { get; }

        /// <summary>
        /// 获取通道标识符映射器的名称。
        /// </summary>
        /// <value>通道标识符映射器的名称。</value>
        public string? ChannelIdMapper { get; }

        /// <summary>
        /// 用于订阅所有流命名空间。
        /// </summary>
        public ImplicitChannelSubscriptionAttribute()
        {
            Predicate = new AllStreamNamespacesPredicate();
        }

        /// <summary>
        /// 用于订阅指定的流命名空间。
        /// </summary>
        /// <param name="streamNamespace">要订阅的流命名空间。</param>
        /// <param name="channelIdMapper">流标识映射器的名称。</param>
        public ImplicitChannelSubscriptionAttribute(string streamNamespace, string? channelIdMapper = null)
        {
            ArgumentNullException.ThrowIfNull(streamNamespace);

            Predicate = new ExactMatchChannelNamespacePredicate(streamNamespace.Trim());
            ChannelIdMapper = channelIdMapper;
        }

        /// <summary>
        /// 允许传入任意谓词类型，以筛选要订阅的流命名空间。谓词类型
        /// 必须具有无参构造函数。
        /// </summary>
        /// <param name="predicateType">流命名空间谓词类型。</param>
        /// <param name="channelIdMapper">流标识映射器的名称。</param>
        public ImplicitChannelSubscriptionAttribute(Type predicateType, string? channelIdMapper = null)
        {
            Predicate = (IChannelNamespacePredicate)Activator.CreateInstance(predicateType)!;
            ChannelIdMapper = channelIdMapper;
        }

        /// <summary>
        /// 允许传入流命名空间谓词的实例。主要用于通过特性继承
        /// 提供扩展点。
        /// </summary>
        /// <param name="predicate">流命名空间谓词。</param>
        /// <param name="channelIdMapper">流标识映射器的名称。</param>
        public ImplicitChannelSubscriptionAttribute(IChannelNamespacePredicate predicate, string? channelIdMapper = null)
        {
            Predicate = predicate;
            ChannelIdMapper = channelIdMapper;
        }

        /// <inheritdoc />
        public IEnumerable<Dictionary<string, string?>> GetBindings(IServiceProvider services, Type grainClass, GrainType grainType)
        {
            var binding = new Dictionary<string, string?>
            {
                [WellKnownGrainTypeProperties.BindingTypeKey] = WellKnownGrainTypeProperties.BroadcastChannelBindingTypeValue,
                [WellKnownGrainTypeProperties.BroadcastChannelBindingPatternKey] = this.Predicate.PredicatePattern,
                [WellKnownGrainTypeProperties.ChannelIdMapperKey] = this.ChannelIdMapper,
            };

            if (LegacyGrainId.IsLegacyGrainType(grainClass))
            {
                string keyType;

                if (typeof(IGrainWithGuidKey).IsAssignableFrom(grainClass) || typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(grainClass))
                    keyType = nameof(Guid);
                else if (typeof(IGrainWithIntegerKey).IsAssignableFrom(grainClass) || typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(grainClass))
                    keyType = nameof(Int64);
                else // 回退到字符串。
                    keyType = nameof(String);

                binding[WellKnownGrainTypeProperties.LegacyGrainKeyType] = keyType;
            }

            if (LegacyGrainId.IsLegacyKeyExtGrainType(grainClass))
            {
                binding[WellKnownGrainTypeProperties.StreamBindingIncludeNamespaceKey] = "true";
            }

            yield return binding;
        }
    }

    /// <summary>
    /// [Orleans.RegexImplicitStreamSubscription] 特性通过使用正则表达式筛选要订阅的流命名空间，
    /// 将 Grain 标记为隐式流订阅者。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RegexImplicitChannelSubscriptionAttribute : ImplicitChannelSubscriptionAttribute
    {
        /// <summary>
        /// 允许传入正则表达式，以筛选要订阅的流命名空间。
        /// </summary>
        /// <param name="pattern">流命名空间的正则表达式筛选器。</param>
        public RegexImplicitChannelSubscriptionAttribute([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
            : base(new RegexChannelNamespacePredicate(pattern))
        {
        }
    }
}
