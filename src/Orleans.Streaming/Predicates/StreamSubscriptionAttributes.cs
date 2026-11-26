using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans
{
    /// <summary>
    /// Orleans.ImplicitStreamSubscription属性用于标记 grains 为隐含流订阅。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ImplicitStreamSubscriptionAttribute : Attribute, IGrainBindingsProviderAttribute
    {
        /// <summary>
        /// 获取流名空间过滤器的前提。
        /// </summary>
        public IStreamNamespacePredicate Predicate { get; }

        /// <summary>
        /// 获取流标识符映射器的名称。
        /// </summary>
        /// <value>流体标识符映像器的名称。</value>
        /// <remarks>
        /// 此值是用于解决在依赖性注射容器中登记的 <see cref="IStreamIdMapper"/> 的名称。
        /// </remarks>
        public string? StreamIdMapper { get; init; }

        /// <summary>
        /// 用于订阅所有流命名空间 。
        /// </summary>
        public ImplicitStreamSubscriptionAttribute()
        {
            Predicate = new AllStreamNamespacesPredicate();
        }

        /// <summary>
        /// 用于订阅指定的流命名空间 。
        /// </summary>
        /// <param name="streamNamespace">要订阅的流命名空间。</param>
        /// <param name="streamIdMapper">流体身份映射器的名称。</param>
        public ImplicitStreamSubscriptionAttribute(string streamNamespace, string? streamIdMapper = null)
        {
            Predicate = new ExactMatchStreamNamespacePredicate(streamNamespace.Trim());
            StreamIdMapper = streamIdMapper;
        }

        /// <summary>
        /// 允许将任意的上游类型传递到过滤要订阅的流命名空间。 
        /// 必须有一个没有参数的构造器 。
        /// </summary>
        /// <param name="predicateType">流命名空间上游类型。</param>
        /// <param name="streamIdMapper">流体身份映射器的名称。</param>
        public ImplicitStreamSubscriptionAttribute(Type predicateType, string? streamIdMapper = null)
        {
            Predicate = (IStreamNamespacePredicate)Activator.CreateInstance(predicateType)!; // CreateInstance 返回一个或几个的上游实例或投出。
            StreamIdMapper = streamIdMapper;
        }

        /// <summary>
        /// 允许通过溪流命名空间前位实例。主要用作扩展点
        /// 通过继承属性。
        /// </summary>
        /// <param name="predicate">流命名空间上游。</param>
        /// <param name="streamIdMapper">流体身份映射器的名称。</param>
        public ImplicitStreamSubscriptionAttribute(IStreamNamespacePredicate predicate, string? streamIdMapper = null)
        {
            Predicate = predicate;
            StreamIdMapper = streamIdMapper;
        }

        /// <inheritdoc />
        public IEnumerable<Dictionary<string, string?>> GetBindings(IServiceProvider services, Type grainClass, GrainType grainType)
        {
            var binding = new Dictionary<string, string?>
            {
                [WellKnownGrainTypeProperties.BindingTypeKey] = WellKnownGrainTypeProperties.StreamBindingTypeValue,
                [WellKnownGrainTypeProperties.StreamBindingPatternKey] = this.Predicate.PredicatePattern,
                [WellKnownGrainTypeProperties.StreamIdMapperKey] = this.StreamIdMapper,
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
    public sealed class RegexImplicitStreamSubscriptionAttribute : ImplicitStreamSubscriptionAttribute
    {
        /// <summary>
        /// 允许将正则表达式传递到要订阅的过滤流命名空间 。
        /// </summary>
        /// <param name="pattern">流名称空间正则表达式过滤器。</param>
        public RegexImplicitStreamSubscriptionAttribute([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
            : base(new RegexStreamNamespacePredicate(pattern))
        {
        }
    }
}
