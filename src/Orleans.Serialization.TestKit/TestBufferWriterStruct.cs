using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 提供固定容量缓冲作家进行序列测试。
    /// </summary>
    [ExcludeFromCodeCoverage]
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This mutable writer owns an aliased buffer and cursor position rather than representing a comparable value.")]
    public struct TestBufferWriterStruct : IBufferWriter<byte>, IOutputBuffer
    {
        private readonly byte[] _buffer;
        private int _written;

        /// <summary>
        /// 初始化 <see cref="TestBufferWriterStruct"/> 结构的新实例。
        /// </summary>
        /// <param name="buffer">写给的缓冲。</param>
        public TestBufferWriterStruct(byte[] buffer)
        {
            _buffer = buffer;
            _written = 0;
        }

        /// <inheritdoc/>
        public void Advance(int bytes) => _written += bytes;

        /// <summary>
        /// 获取固定输出缓冲符的未写部分。
        /// </summary>
        /// <param name="sizeHint">要求的最低尺寸。 这种固定缓冲执行不分配额外能力 。</param>
        /// <returns>可供写入的内存。</returns>
        [Pure]
        public readonly Memory<byte> GetMemory(int sizeHint = 0) => _buffer.AsMemory()[_written..];

        /// <summary>
        /// 获取固定输出缓冲符的未写部分。
        /// </summary>
        /// <param name="sizeHint">要求的最低尺寸。 这种固定缓冲执行不分配额外能力 。</param>
        /// <returns>可用于撰写的间隔。</returns>
        [Pure]
        public readonly Span<byte> GetSpan(int sizeHint) => _buffer.AsSpan()[_written..];

        /// <inheritdoc/>
        [Pure]
        public readonly ReadOnlySequence<byte> GetReadOnlySequence(int maxSegmentSize) => _buffer.Take(_written).Batch(maxSegmentSize).ToReadOnlySequence();
    }
}
