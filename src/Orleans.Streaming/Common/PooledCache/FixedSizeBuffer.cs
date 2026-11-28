
using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 管理一块连续内存。
    /// 收到清除信号时，以自身作为清除请求调用清除操作。
    /// </summary>
    public class FixedSizeBuffer : PooledResource<FixedSizeBuffer>
    {
        private readonly byte[] buffer;
        private int count;

        /// <summary>
        /// 缓冲区大小（字节）。
        /// </summary>
        public readonly int SizeInByte;

        /// <summary>
        /// 此缓冲区的唯一标识符。
        /// </summary>
        public object Id => buffer;

        /// <summary>
        /// 管理对固定大小字节缓冲区的访问。
        /// </summary>
        /// <param name="blockSizeInByte">块大小（字节）。</param>
        public FixedSizeBuffer(int blockSizeInByte)
        {
            if (blockSizeInByte < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockSizeInByte), "blockSize must be positive value.");
            }
            count = 0;
            this.SizeInByte = blockSizeInByte;
            buffer = new byte[this.SizeInByte];
        }

        /// <summary>
        /// 尝试从此块中获取包含指定大小缓冲区的内存段。
        /// 如果剩余空间不足，则失败。
        /// </summary>
        /// <param name="size">大小。</param>
        /// <param name="value">内存段。</param>
        /// <returns>如果成功获取内存段，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public bool TryGetSegment(int size, out ArraySegment<byte> value)
        {
            value = default;
            if (size > this.SizeInByte - count)
            {
                return false;
            }
            value = new ArraySegment<byte>(buffer, count, size);
            count += size;
            return true;
        }

        /// <inheritdoc />
        public override void OnResetState()
        {
            count = 0;
        }
    }
}
