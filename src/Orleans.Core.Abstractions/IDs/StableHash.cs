using System;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Orleans
{
    /// <summary>
    /// 提供在各进程及集群节点之间结果一致的稳定哈希函数。
    /// </summary>
    public static class StableHash
    {
        /// <summary>
        /// 计算输入的哈希摘要。
        /// </summary>
        /// <param name="data">
        /// 输入数据。
        /// </param>
        /// <returns>
        /// 输入的哈希摘要。
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint ComputeHash(ReadOnlySpan<byte> data)
        {
            uint hash;
            XxHash32.TryHash(data, new Span<byte>((byte*)&hash, sizeof(uint)), out _);
            return BitConverter.IsLittleEndian ? hash : BinaryPrimitives.ReverseEndianness(hash);
        }

        /// <summary>
        /// 计算输入的哈希摘要。
        /// </summary>
        /// <param name="data">
        /// 输入数据。
        /// </param>
        /// <returns>
        /// 输入的哈希摘要。
        /// </returns>
        public static uint ComputeHash(string data) => ComputeHash(BitConverter.IsLittleEndian ? MemoryMarshal.AsBytes(data.AsSpan()) : Encoding.Unicode.GetBytes(data));
    }
}
