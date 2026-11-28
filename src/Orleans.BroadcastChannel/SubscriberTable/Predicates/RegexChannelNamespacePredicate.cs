using System;
using System.Text.RegularExpressions;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// <see cref="IChannelNamespacePredicate"/> 的实现，允许通过正则
    /// 表达式筛选流命名空间。
    /// </summary>
    public class RegexChannelNamespacePredicate : IChannelNamespacePredicate
    {
        internal const string Prefix = "regex:";
        private readonly Regex regex;

        /// <summary>
        /// 返回用于描述此实例的模式。该模式会由每个节点上的 <see cref="IChannelNamespacePredicateProvider"/> 实例解析。
        /// </summary>
        public string PredicatePattern => $"{Prefix}{regex}";

        /// <summary>
        /// 使用指定的正则表达式创建 <see cref="RegexChannelNamespacePredicate"/> 实例。
        /// </summary>
        /// <param name="regex">流命名空间的正则表达式。</param>
        public RegexChannelNamespacePredicate(string regex)
        {
            if (regex is null) throw new ArgumentNullException(nameof(regex));

            this.regex = new Regex(regex, RegexOptions.Compiled);
        }

        /// <inheritdoc />
        public bool IsMatch(string streamNameSpace)
        {
            return regex.IsMatch(streamNameSpace);
        }
    }
}
