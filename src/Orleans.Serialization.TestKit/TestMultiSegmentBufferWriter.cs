using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 提供一位缓冲作家,将承诺的字节储存在多个段次中进行序列测试。
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
        /// <param name="maxAllocationSize">个人缓冲分配的最大规模。</param>
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
        /// 获取可写内存, 以配置的最大分配大小限制 。
        /// </summary>
        /// <param name="sizeHint">适用分配上限前要求的最低尺寸。</param>
        /// <returns>可供写入的内存。</returns>
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
        /// 获得可写宽, 以配置的最大分配大小为上限。
        /// </summary>
        /// <param name="sizeHint">适用分配上限前要求的最低尺寸。</param>
        /// <returns>可用于撰写的间隔。</returns>
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
        /// 返回所有承诺的缓冲带,然后返回当前未承付的缓冲带。
        /// </summary>
        /// <returns>包含所有指定缓冲的只读序列。</returns>
        public ReadOnlySequence<byte> PeekAllBuffers() => _committed.Concat(new[] { _current }).ToReadOnlySequence();
    }
}
