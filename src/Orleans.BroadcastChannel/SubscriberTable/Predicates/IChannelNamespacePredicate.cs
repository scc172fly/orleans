using System.Diagnostics.CodeAnalysis;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 用于过滤使用 
    /// <see cref="ImplicitChannelSubscriptionAttribute"/>.
    /// </summary>
    /// <remarks>所有执行都必须可连号进行。</remarks>
    public interface IChannelNamespacePredicate
    {
        /// <summary>
        /// 定义消费者是否应当订阅指定命名空间 grain 。
        /// </summary>
        /// <param name="streamNamespace">要检查的目标流命名空间。</param>
        /// <returns><c>true</c>,如果 grain 订阅指定的命名空间; <c>false</c>否则的话
        /// </returns>
        bool IsMatch(string streamNamespace);

        /// <summary>
        /// 获得描述此上游的图案。 此值被传递到 <see cref="IChannelNamespacePredicateProvider"/> 实例中, 以重建该上游 。
        /// </summary>
        string PredicatePattern { get; }
    }

    /// <summary>
    /// 将上游模式字符串转换为 <see cref="IChannelNamespacePredicate"/> 实例。
    /// </summary>
    /// <seealso cref="IChannelNamespacePredicate.PredicatePattern"/>
    public interface IChannelNamespacePredicateProvider
    {
        /// <summary>
        /// 获取与所提供的模式匹配的上游。如果该提供者无法匹配上游,则返回 <see langword="false"/> 。
        /// </summary>
        bool TryGetPredicate(string predicatePattern, [NotNullWhen(true)] out IChannelNamespacePredicate? predicate);
    }
}
