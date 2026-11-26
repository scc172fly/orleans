namespace Orleans.Streams
{
    /// <summary>
    /// 串行命名空间前置, 它与所有命名空间匹配 。
    /// </summary>
    internal class AllStreamNamespacesPredicate : IStreamNamespacePredicate
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
