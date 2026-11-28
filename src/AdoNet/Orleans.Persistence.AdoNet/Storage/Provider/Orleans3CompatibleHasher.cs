using System;

namespace Orleans.Storage
{
    /// <summary>
    /// 与 Orleans v3 兼容的哈希算法实现，适用于非纯字符串类型的 Grain 键 ID。
    /// </summary>
    internal class Orleans3CompatibleHasher : IHasher
    {
        /// <summary>
        /// <see cref="IHasher.Description"/>
        /// </summary>
        public string Description { get; } = $"Orleans v3 hash function ({nameof(JenkinsHash)}).";

        /// <summary>
        /// <see cref="IHasher.Hash(byte[])"/>.
        /// </summary>
        public int Hash(byte[] data) => Hash(data.AsSpan());

        /// <summary>
        /// <see cref="IHasher.Hash(byte[])"/>.
        /// </summary>
        public int Hash(ReadOnlySpan<byte> data)
        {
            // 从 Orleans v3.7.2 恢复的实现：https://github.com/dotnet/orleans/blob/b24e446abfd883f0e4ed614f5267eaa3331548dc/src/AdoNet/Orleans.Persistence.AdoNet/Storage/Provider/OrleansDefaultHasher.cs
            return unchecked((int)JenkinsHash.ComputeHash(data));
        }
    }
}
