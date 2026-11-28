namespace Orleans.Storage
{
    /// <summary>
    /// 默认实现使用与 Orleans Grain 放置相同的哈希算法。
    /// </summary>
    public sealed class OrleansDefaultHasher : IHasher
    {
        /// <summary>
        /// <see cref="IHasher.Description"/>
        /// </summary>
        public string Description => $"The default Orleans hash function ({nameof(StableHash)}).";

        /// <summary>
        /// <see cref="IHasher.Hash(byte[])"/>.
        /// </summary>
        public int Hash(byte[] data) => (int)StableHash.ComputeHash(data);
    }
}
