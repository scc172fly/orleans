using System;
using System.Text.RegularExpressions;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// <see cref="IChannelNamespacePredicate"/> 执行允许通过常规过滤流命名空间
    /// 表达式。
    /// </summary>
    public class RegexChannelNamespacePredicate : IChannelNamespacePredicate
    {
        internal const string Prefix = "regex:";
        private readonly Regex regex;

        /// <summary>
        /// 返回用于描述此实例的图案。该图案将在每个节点上用 <see cref="IChannelNamespacePredicateProvider"/> 例进行解析。
        /// </summary>
        public string PredicatePattern => $"{Prefix}{regex}";

        /// <summary>
        /// 创建有指定正则表达式的 <see cref="RegexChannelNamespacePredicate"/> 实例。
        /// </summary>
        /// <param name="regex">流名空间正则表达式。</param>
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
