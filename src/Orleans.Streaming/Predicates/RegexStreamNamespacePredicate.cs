using System;
using System.Text.RegularExpressions;

namespace Orleans.Streams
{
    /// <summary>
    /// <see cref="IStreamNamespacePredicate"/> 实现，允许通过正则
    /// 表达式筛选流命名空间。
    /// </summary>
    public class RegexStreamNamespacePredicate : IStreamNamespacePredicate
    {
        internal const string Prefix = "regex:";
        private readonly Regex regex;

        /// <summary>
        /// 返回用于描述此实例的模式。每个节点上的 <see cref="IStreamNamespacePredicateProvider"/> 实例都将解析此模式。
        /// </summary>
        public string PredicatePattern => $"{Prefix}{regex}";

        /// <summary>
        /// 使用指定正则表达式创建 <see cref="RegexStreamNamespacePredicate"/> 实例。
        /// </summary>
        /// <param name="regex">流命名空间的正则表达式。</param>
        public RegexStreamNamespacePredicate(string regex)
        {
            ArgumentNullException.ThrowIfNull(regex);
            this.regex = new Regex(regex, RegexOptions.Compiled);
        }

        /// <inheritdoc />
        public bool IsMatch(string streamNameSpace)
        {
            return regex.IsMatch(streamNameSpace);
        }
    }
}
