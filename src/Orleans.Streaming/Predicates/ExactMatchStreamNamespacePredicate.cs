using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 精确匹配单个指定流命名空间的谓词。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    internal sealed class ExactMatchStreamNamespacePredicate : IStreamNamespacePredicate
    {
        internal const string Prefix = "namespace:";

        [Id(0)]
        private readonly string targetStreamNamespace;

        /// <summary>
        /// 初始化 <see cref="ExactMatchStreamNamespacePredicate"/> 类的新实例。
        /// </summary>
        /// <param name="targetStreamNamespace">目标流命名空间。</param>
        public ExactMatchStreamNamespacePredicate(string targetStreamNamespace)
        {
            this.targetStreamNamespace = targetStreamNamespace;
        }

        /// <inheritdoc/>
        public string PredicatePattern => $"{Prefix}{this.targetStreamNamespace}";

        /// <inheritdoc/>
        public bool IsMatch(string streamNamespace)
        {
            return string.Equals(targetStreamNamespace, streamNamespace?.Trim(), StringComparison.Ordinal);
        }
    }
}
