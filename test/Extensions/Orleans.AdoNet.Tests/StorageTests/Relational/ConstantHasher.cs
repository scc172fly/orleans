using Orleans.Storage;


namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 对所有输入返回相同的哈希值。用于在涉及多个用例的场景中
    /// 人为引发哈希冲突。
    /// </summary>
    public class ConstantHasher : IHasher
    {
        /// <summary>
        /// 所有输入都会被哈希为此值。
        /// </summary>
        public const int ConstantHash = 1;

        /// <summary>
        /// <see cref="IHasher.Description"/>.
        /// </summary>
        public string Description { get; } = $"Returns {ConstantHash} to all input, thus inducing hash collisions.";

        /// <summary>
        /// <see cref="IHasher.Hash(byte[])"/>.
        /// </summary>
        public int Hash(byte[] data) { return ConstantHash; }
    }
}
