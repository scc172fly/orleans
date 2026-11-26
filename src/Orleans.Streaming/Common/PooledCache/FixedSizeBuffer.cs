
using System;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 管理一个毗连的内存块。
    /// 要求清洗行动本身作为清洗请求 当它发出清洗信号时
    /// </summary>
    public class FixedSizeBuffer : PooledResource<FixedSizeBuffer>
    {
        private readonly byte[] buffer;
        private int count;

        /// <summary>
        /// 以字节计的缓冲大小 。
        /// </summary>
        public readonly int SizeInByte;

        /// <summary>
        /// 此缓冲符的独特标识 。
        /// </summary>
        public object Id => buffer;

        /// <summary>
        /// 管理对固定字节缓冲的访问。
        /// </summary>
        /// <param name="blockSizeInByte">区块大小,以字节为单位。</param>
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
        /// 尝试从此块中获取一个带有指定大小缓冲的段。
        /// 如果没有足够的空间,则不能
        /// </summary>
        /// <param name="size">大小。</param>
        /// <param name="value">段</param>
        /// <returns><see langword="true"/> ,如果该段被收回;否则 <see langword="false"/>。</returns>
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
