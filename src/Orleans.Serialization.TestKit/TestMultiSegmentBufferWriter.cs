using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 为序列化测试提供缓冲区写入器，将已提交的字节存储在多个分段中。
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class TestMultiSegmentBufferWriter : IBufferWriter<byte>, IOutputBuffer
    {
        private readonly List<byte[]> _committed = new();
        private readonly int _maxAllocationSize;
        private byte[] _current = Array.Empty<byte>();

        /// <summary>
        /// 初始化 <see cref="TestMultiSegmentBufferWriter"/> 类的新实例。
        /// </summary>
        /// <param name="maxAllocationSize">单次缓冲区分配的最大大小。</param>
        public TestMultiSegmentBufferWriter(int maxAllocationSize)
        {
            _maxAllocationSize = maxAllocationSize;
        }

        /// <inheritdoc/>
        public void Advance(int bytes)
        {
            if (bytes == 0)
            {
                return;
            }

            _committed.Add(_current.AsSpan(0, bytes).ToArray());
            _current = Array.Empty<byte>();
        }

        /// <summary>
        /// 获取可写内存，其大小受配置的最大分配大小限制。
        /// </summary>
        /// <param name="sizeHint">应用分配大小上限之前请求的最小大小。</param>
        /// <returns>可用于写入的内存。</returns>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (sizeHint == 0)
            {
                sizeHint = _current.Length + 1;
            }

            if (sizeHint < _current.Length)
            {
                throw new InvalidOperationException("Attempted to allocate a new buffer when the existing buffer has sufficient free space.");
            }

            var newBuffer = new byte[Math.Min(sizeHint, _maxAllocationSize)];
            _current.CopyTo(newBuffer.AsSpan());
            _current = newBuffer;
            return _current;
        }

        /// <summary>
        /// 获取可写跨度（Span），其大小受配置的最大分配大小限制。
        /// </summary>
        /// <param name="sizeHint">应用分配大小上限之前请求的最小大小。</param>
        /// <returns>可用于写入的跨度（Span）。</returns>
        public Span<byte> GetSpan(int sizeHint)
        {
            if (sizeHint == 0)
            {
                sizeHint = _current.Length + 1;
            }

            if (sizeHint < _current.Length)
            {
                throw new InvalidOperationException("Attempted to allocate a new buffer when the existing buffer has sufficient free space.");
            }

            var newBuffer = new byte[Math.Min(sizeHint, _maxAllocationSize)];
            _current.CopyTo(newBuffer.AsSpan());
            _current = newBuffer;
            return _current;
        }

        /// <inheritdoc/>
        [Pure]
        public ReadOnlySequence<byte> GetReadOnlySequence(int maxSegmentSize) => _committed.SelectMany(b => b).Batch(maxSegmentSize).ToReadOnlySequence();

        /// <summary>
        /// 返回所有已提交的缓冲区，最后附加当前未提交的缓冲区。
        /// </summary>
        /// <returns>包含所有已分配缓冲区的只读序列。</returns>
        public ReadOnlySequence<byte> PeekAllBuffers() => _committed.Concat(new[] { _current }).ToReadOnlySequence();
    }
}
