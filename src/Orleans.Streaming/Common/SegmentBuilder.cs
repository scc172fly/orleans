
using System;
using System.Runtime.InteropServices;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 将数据编码为 ArraySegment的实用类。
    /// </summary>
    public static class SegmentBuilder
    {
        /// <summary>
        /// 计算在段中附加提供的字节需要多少空间。
        /// </summary>
        public static int CalculateAppendSize(ReadOnlySpan<byte> memory) => memory.Length + sizeof(int);

        /// <summary>
        /// 计算在段中追加提供字符串需要多少空间。
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static int CalculateAppendSize(string? str) => str is null ? sizeof(int) : str.Length * sizeof(char) + sizeof(int);

        /// <summary>
        /// 在段末加上 <see cref="ReadOnlyMemory{T}"/> 字节的字节
        /// </summary>
        /// <param name="writerOffset"></param>
        /// <param name="bytes"></param>
        /// <param name="segment"></param>
        public static void Append(ArraySegment<byte> segment, ref int writerOffset, ReadOnlySpan<byte> bytes)
        {
            if (segment.Array == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }

            var length = bytes.Length;
            MemoryMarshal.Write(segment.AsSpan(writerOffset), in length);
            writerOffset += sizeof(int);

            if (bytes.Length > 0)
            {
                bytes.CopyTo(segment.AsSpan(writerOffset));
                writerOffset += bytes.Length;
            }
        }

        /// <summary>
        /// 将字符串附加到段末
        /// </summary>
        /// <param name="writerOffset"></param>
        /// <param name="str"></param>
        /// <param name="segment"></param>
        public static void Append(ArraySegment<byte> segment, ref int writerOffset, string? str)
        {
            if (segment.Array == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }
            if (str == null)
            {
                var length = -1;
                MemoryMarshal.Write(segment.AsSpan(writerOffset), in length);
                writerOffset += sizeof(int);
            }
            else
            {
                Append(segment, ref writerOffset, MemoryMarshal.AsBytes(str.AsSpan()));
            }
        }

        /// <summary>
        /// 将段内的下一个项作为字节数组读取。对于性能,此项作为原段的一个子部分返回。
        /// </summary>
        /// <returns></returns>
        public static ArraySegment<byte> ReadNextBytes(ArraySegment<byte> segment, ref int readerOffset)
        {
            if (segment.Array == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }
            int size = BitConverter.ToInt32(segment.Array, segment.Offset + readerOffset);
            readerOffset += sizeof(int);
            var seg = new ArraySegment<byte>(segment.Array, segment.Offset + readerOffset, size);
            readerOffset += size;
            return seg;
        }

        /// <summary>
        /// 将段内的下一个项目作为字符串读取。
        /// </summary>
        /// <returns></returns>
        public static string? ReadNextString(ArraySegment<byte> segment, ref int readerOffset)
        {
            if (segment.Array == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }
            int size = BitConverter.ToInt32(segment.Array, segment.Offset + readerOffset);
            readerOffset += sizeof(int);
            if (size < 0)
            {
                return null;
            }
            if (size == 0)
            {
                return string.Empty;
            }
            var chars = segment.AsSpan(readerOffset, size);
            readerOffset += size;
            return new string(MemoryMarshal.Cast<byte, char>(chars));
        }
    }
}
