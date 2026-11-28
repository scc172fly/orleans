using System.Diagnostics.CodeAnalysis;

namespace Orleans.Streams
{
    /// <summary>
    /// 用于通过下述特性筛选隐式订阅的流命名空间谓词：
    /// <see cref="ImplicitStreamSubscriptionAttribute"/>.
    /// </summary>
    /// <remarks>所有实现都必须可序列化。</remarks>
    public interface IStreamNamespacePredicate
    {
        /// <summary>
        /// 确定消费者 Grain 是否应订阅指定命名空间。
        /// </summary>
        /// <param name="streamNamespace">要检查的目标流命名空间。</param>
        /// <returns>如果 Grain 应订阅指定命名空间，则为 <c>true</c>；否则为 <c>false</c>。
        /// </returns>
        bool IsMatch(string streamNamespace);

        /// <summary>
        /// 获取描述此谓词的模式。此值会传递给 <see cref="IStreamNamespacePredicateProvider"/> 实例以重新创建此谓词。
        /// </summary>
        string PredicatePattern { get; }
    }

    /// <summary>
    /// 将谓词模式字符串转换为 <see cref="IStreamNamespacePredicate"/> 实例。
    /// </summary>
    /// <seealso cref="IStreamNamespacePredicate.PredicatePattern"/>
    public interface IStreamNamespacePredicateProvider
    {
        /// <summary>
        /// 获取匹配给定模式的谓词。如果此提供程序无法匹配谓词，则返回 <see langword="false"/>。
        /// </summary>
        bool TryGetPredicate(string predicatePattern, [MaybeNullWhen(false)] out IStreamNamespacePredicate predicate);
    }
}
