using System.Buffers;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 提供访问写到输出缓冲的字节的机会。
    /// </summary>
    public interface IOutputBuffer
    {
        /// <summary>
        /// 返回只读的顺序。
        /// </summary>
        /// <param name="maxSegmentSize">每个序列段的最大字节数。</param>
        /// <returns>包含书面字节的只读序列。</returns>
        ReadOnlySequence<byte> GetReadOnlySequence(int maxSegmentSize);
    }
}
