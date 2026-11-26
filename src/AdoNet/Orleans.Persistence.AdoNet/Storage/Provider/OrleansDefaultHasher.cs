namespace Orleans.Storage
{
    /// <summary>
    /// 默认执行默认使用与 Orleans 内 grains 安置。
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
