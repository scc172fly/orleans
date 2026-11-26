using System;
using System.Text.RegularExpressions;

namespace Orleans.Streams
{
    /// <summary>
    /// <see cref="IStreamNamespacePredicate"/> 执行允许通过常规过滤流命名空间
    /// 表达式。
    /// </summary>
    public class RegexStreamNamespacePredicate : IStreamNamespacePredicate
    {
        internal const string Prefix = "regex:";
        private readonly Regex regex;

        /// <summary>
        /// 返回用于描述此实例的图案。该图案将在每个节点上用 <see cref="IStreamNamespacePredicateProvider"/> 例进行解析。
        /// </summary>
        public string PredicatePattern => $"{Prefix}{regex}";

        /// <summary>
        /// 创建有指定正则表达式的 <see cref="RegexStreamNamespacePredicate"/> 实例。
        /// </summary>
        /// <param name="regex">流名空间正则表达式。</param>
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
