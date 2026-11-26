using Orleans.Storage;


namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 返回所有输入的恒定散列值。 这用于诱发散列碰撞
    /// 一种情况涉及一个以上的情况。
    /// </summary>
    public class ConstantHasher : IHasher
    {
        /// <summary>
        /// 每个输入都将被仓列到的散列值 。
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
