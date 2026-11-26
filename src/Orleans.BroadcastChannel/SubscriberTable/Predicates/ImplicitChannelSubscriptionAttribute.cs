using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.BroadcastChannel;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// Orleans.ImplicitStreamSubscription属性用于标记 grains 为隐含流订阅。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ImplicitChannelSubscriptionAttribute : Attribute, IGrainBindingsProviderAttribute
    {
        /// <summary>
        /// 获取流名空间过滤器的前提。
        /// </summary>
        public IChannelNamespacePredicate Predicate { get; }

        /// <summary>
        /// 获取频道标识符映射器的名称。
        /// </summary>
        /// <value>频道标识符映像器的名称。</value>
        public string? ChannelIdMapper { get; }

        /// <summary>
        /// 用于订阅所有流命名空间 。
        /// </summary>
        public ImplicitChannelSubscriptionAttribute()
        {
            Predicate = new AllStreamNamespacesPredicate();
        }

        /// <summary>
        /// 用于订阅指定的流命名空间 。
        /// </summary>
        /// <param name="streamNamespace">要订阅的流命名空间。</param>
        /// <param name="channelIdMapper">流体身份映射器的名称。</param>
        public ImplicitChannelSubscriptionAttribute(string streamNamespace, string? channelIdMapper = null)
        {
            ArgumentNullException.ThrowIfNull(streamNamespace);

            Predicate = new ExactMatchChannelNamespacePredicate(streamNamespace.Trim());
            ChannelIdMapper = channelIdMapper;
        }

        /// <summary>
        /// 允许将任意的上游类型传递到过滤要订阅的流命名空间。 
        /// 必须有一个没有参数的构造器 。
        /// </summary>
        /// <param name="predicateType">流命名空间上游类型。</param>
        /// <param name="channelIdMapper">流体身份映射器的名称。</param>
        public ImplicitChannelSubscriptionAttribute(Type predicateType, string? channelIdMapper = null)
        {
            Predicate = (IChannelNamespacePredicate)Activator.CreateInstance(predicateType)!;
            ChannelIdMapper = channelIdMapper;
        }

        /// <summary>
        /// 允许通过溪流命名空间前位实例。主要用作扩展点
        /// 通过继承属性。
        /// </summary>
        /// <param name="predicate">流命名空间上游。</param>
        /// <param name="channelIdMapper">流体身份映射器的名称。</param>
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
                else // 返回回到字符串
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
    /// [Orleans.RegexImplicitStreamSubscription]属性用于标记 grains 的隐含流
    /// 通过过滤流命名空间订阅使用正则表达式的用户。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RegexImplicitChannelSubscriptionAttribute : ImplicitChannelSubscriptionAttribute
    {
        /// <summary>
        /// 允许将正则表达式传递到要订阅的过滤流命名空间 。
        /// </summary>
        /// <param name="pattern">流名称空间正则表达式过滤器。</param>
        public RegexImplicitChannelSubscriptionAttribute([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
            : base(new RegexChannelNamespacePredicate(pattern))
        {
        }
    }
}
