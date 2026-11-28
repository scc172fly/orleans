using System.Buffers;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 提供对写入输出缓冲区的字节的访问。
    /// </summary>
    public interface IOutputBuffer
    {
        /// <summary>
        /// 将已写入的字节作为只读序列返回。
        /// </summary>
        /// <param name="maxSegmentSize">每个序列分段的最大字节数。</param>
        /// <returns>包含已写入字节的只读序列。</returns>
        ReadOnlySequence<byte> GetReadOnlySequence(int maxSegmentSize);
    }
}
