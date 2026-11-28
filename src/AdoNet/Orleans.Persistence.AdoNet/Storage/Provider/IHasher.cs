namespace Orleans.Storage
{
    /// <summary>
    /// Orleans 存储操作当前使用的所有哈希操作的接口。
    /// </summary>
    /// <remarks>实现此接口可提供具有特定属性的数据库键哈希算法。
    /// 例如，应能抵抗不受控制的 ID 提供程序产生的哈希碰撞。</remarks>
    public interface IHasher
    {
        /// <summary>
        /// 哈希功能说明。
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 哈希值。
        /// </summary>
        /// <param name="data">要计算哈希的数据。</param>
        /// <returns>给定字节的哈希值。</returns>
        int Hash(byte[] data);
    }
}
