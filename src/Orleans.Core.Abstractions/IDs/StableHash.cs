using System;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Orleans
{
    /// <summary>
    /// 提供稳定的散列功能,其结果在各种进程和群集节点之间是一致的。
    /// </summary>
    public static class StableHash
    {
        /// <summary>
        /// 计算输入的散列摘要。
        /// </summary>
        /// <param name="data">
        /// 输入数据
        /// </param>
        /// <returns>
        /// 输入的散列摘要 。
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe uint ComputeHash(ReadOnlySpan<byte> data)
        {
            uint hash;
            XxHash32.TryHash(data, new Span<byte>((byte*)&hash, sizeof(uint)), out _);
            return BitConverter.IsLittleEndian ? hash : BinaryPrimitives.ReverseEndianness(hash);
        }

        /// <summary>
        /// 计算输入的散列摘要。
        /// </summary>
        /// <param name="data">
        /// 输入数据
        /// </param>
        /// <returns>
        /// 输入的散列摘要 。
        /// </returns>
        public static uint ComputeHash(string data) => ComputeHash(BitConverter.IsLittleEndian ? MemoryMarshal.AsBytes(data.AsSpan()) : Encoding.Unicode.GetBytes(data));
    }
}
