using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 为序列化测试提供固定容量的缓冲区写入器。
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
        /// <param name="buffer">要写入的缓冲区。</param>
        public TestBufferWriterStruct(byte[] buffer)
        {
            _buffer = buffer;
            _written = 0;
        }

        /// <inheritdoc/>
        public void Advance(int bytes) => _written += bytes;

        /// <summary>
        /// 获取固定输出缓冲区中尚未写入的部分。
        /// </summary>
        /// <param name="sizeHint">请求的最小大小。此固定缓冲区实现不会分配额外容量。</param>
        /// <returns>可用于写入的内存。</returns>
        [Pure]
        public readonly Memory<byte> GetMemory(int sizeHint = 0) => _buffer.AsMemory()[_written..];

        /// <summary>
        /// 获取固定输出缓冲区中尚未写入的部分。
        /// </summary>
        /// <param name="sizeHint">请求的最小大小。此固定缓冲区实现不会分配额外容量。</param>
        /// <returns>可用于写入的跨度（Span）。</returns>
        [Pure]
        public readonly Span<byte> GetSpan(int sizeHint) => _buffer.AsSpan()[_written..];

        /// <inheritdoc/>
        [Pure]
        public readonly ReadOnlySequence<byte> GetReadOnlySequence(int maxSegmentSize) => _buffer.Take(_written).Batch(maxSegmentSize).ToReadOnlySequence();
    }
}
