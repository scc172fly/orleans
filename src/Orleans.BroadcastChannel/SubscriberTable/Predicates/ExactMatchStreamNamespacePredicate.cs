using System;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 仅匹配一个指定命名空间的流命名空间谓词。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    internal sealed class ExactMatchChannelNamespacePredicate : IChannelNamespacePredicate
    {
        internal const string Prefix = "namespace:";

        [Id(0)]
        private readonly string targetStreamNamespace;

        /// <summary>
        /// 初始化 <see cref="ExactMatchChannelNamespacePredicate"/> 类的新实例。
        /// </summary>
        /// <param name="targetStreamNamespace">目标流命名空间。</param>
        public ExactMatchChannelNamespacePredicate(string targetStreamNamespace)
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
