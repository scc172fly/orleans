using System.Diagnostics.CodeAnalysis;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 用于通过下列特性筛选隐式订阅的流命名空间谓词：
    /// <see cref="ImplicitChannelSubscriptionAttribute"/>.
    /// </summary>
    /// <remarks>所有实现都必须可序列化。</remarks>
    public interface IChannelNamespacePredicate
    {
        /// <summary>
        /// 定义消费者 Grain 是否应订阅指定命名空间。
        /// </summary>
        /// <param name="streamNamespace">要检查的目标流命名空间。</param>
        /// <returns><c>true</c>，如果 Grain 应订阅指定命名空间；否则为 <c>false</c>。
        /// </returns>
        bool IsMatch(string streamNamespace);

        /// <summary>
        /// 获取描述此谓词的模式。此值将传递给 <see cref="IChannelNamespacePredicateProvider"/> 实例以重新创建此谓词。
        /// </summary>
        string PredicatePattern { get; }
    }

    /// <summary>
    /// 将谓词模式字符串转换为 <see cref="IChannelNamespacePredicate"/> 实例。
    /// </summary>
    /// <seealso cref="IChannelNamespacePredicate.PredicatePattern"/>
    public interface IChannelNamespacePredicateProvider
    {
        /// <summary>
        /// 获取匹配所提供模式的谓词。如果此提供程序无法匹配谓词，则返回 <see langword="false"/>。
        /// </summary>
        bool TryGetPredicate(string predicatePattern, [NotNullWhen(true)] out IChannelNamespacePredicate? predicate);
    }
}
