using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans
{
    /// <summary>
    /// [Orleans.ImplicitStreamSubscription] 特性用于将 Grain 标记为隐式流订阅者。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ImplicitStreamSubscriptionAttribute : Attribute, IGrainBindingsProviderAttribute
    {
        /// <summary>
        /// 获取流命名空间筛选谓词。
        /// </summary>
        public IStreamNamespacePredicate Predicate { get; }

        /// <summary>
        /// 获取流标识符映射器的名称。
        /// </summary>
        /// <value>流标识符映射器的名称。</value>
        /// <remarks>
        /// 此值是用于解析依赖注入容器中注册的 <see cref="IStreamIdMapper"/> 的名称。
        /// </remarks>
        public string? StreamIdMapper { get; init; }

        /// <summary>
        /// 用于订阅所有流命名空间。
        /// </summary>
        public ImplicitStreamSubscriptionAttribute()
        {
            Predicate = new AllStreamNamespacesPredicate();
        }

        /// <summary>
        /// 用于订阅指定流命名空间。
        /// </summary>
        /// <param name="streamNamespace">要订阅的流命名空间。</param>
        /// <param name="streamIdMapper">流标识映射器的名称。</param>
        public ImplicitStreamSubscriptionAttribute(string streamNamespace, string? streamIdMapper = null)
        {
            Predicate = new ExactMatchStreamNamespacePredicate(streamNamespace.Trim());
            StreamIdMapper = streamIdMapper;
        }

        /// <summary>
        /// 允许传入任意谓词类型来筛选要订阅的流命名空间。该谓词类型
        /// 必须具有无参数构造函数。
        /// </summary>
        /// <param name="predicateType">流命名空间谓词类型。</param>
        /// <param name="streamIdMapper">流标识映射器的名称。</param>
        public ImplicitStreamSubscriptionAttribute(Type predicateType, string? streamIdMapper = null)
        {
            Predicate = (IStreamNamespacePredicate)Activator.CreateInstance(predicateType)!; // CreateInstance 返回谓词实例或抛出异常。
            StreamIdMapper = streamIdMapper;
        }

        /// <summary>
        /// 允许传入流命名空间谓词的实例，主要用于通过
        /// 继承特性提供扩展点。
        /// </summary>
        /// <param name="predicate">流命名空间谓词。</param>
        /// <param name="streamIdMapper">流标识映射器的名称。</param>
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
    /// [Orleans.RegexImplicitStreamSubscription] 特性使用正则表达式筛选要订阅的流命名空间，
    /// 从而将 Grain 标记为隐式流订阅者。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RegexImplicitStreamSubscriptionAttribute : ImplicitStreamSubscriptionAttribute
    {
        /// <summary>
        /// 允许传入正则表达式以筛选要订阅的流命名空间。
        /// </summary>
        /// <param name="pattern">流命名空间的正则表达式筛选器。</param>
        public RegexImplicitStreamSubscriptionAttribute([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
            : base(new RegexStreamNamespacePredicate(pattern))
        {
        }
    }
}
