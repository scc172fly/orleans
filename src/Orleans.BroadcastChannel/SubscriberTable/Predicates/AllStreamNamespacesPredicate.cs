namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 匹配所有命名空间的流命名空间谓词。
    /// </summary>
    internal class AllStreamNamespacesPredicate : IChannelNamespacePredicate
    {
        /// <inheritdoc/>
        public string PredicatePattern => "*";

        /// <inheritdoc/>
        public bool IsMatch(string streamNamespace)
        {
            return true;
        }
    }
}
