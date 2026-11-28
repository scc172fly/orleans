using System;
using System.Buffers;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// <see cref="IBufferWriter{T}"/> 的专用实现，用于支持 <see cref="Span{T}"/> 在 <see cref="Writer{TBufferWriter}"/> 中的使用。
    /// </summary>
    public struct SpanBufferWriter : IBufferWriter<byte>
    {
        private readonly int _maxLength;
        private int _bytesWritten;

        /// <summary>
        /// 初始化 <see cref="SpanBufferWriter"/> 结构的新实例。
        /// </summary>
        /// <param name="buffer">缓冲区。</param>
        internal SpanBufferWriter(ReadOnlySpan<byte> buffer)
        {
            _maxLength = buffer.Length;
            _bytesWritten = 0;
        }

        /// <summary>
        /// 获取已写入的字节数。
        /// </summary>
        /// <value>已写入的字节数。</value>
        public readonly int BytesWritten => _bytesWritten;

        /// <inheritdoc />
        public void Advance(int count) => _bytesWritten += count;

        /// <inheritdoc />
        public readonly Memory<byte> GetMemory(int sizeHint = 0) => throw GetException(sizeHint);

        /// <inheritdoc />
        public readonly Span<byte> GetSpan(int sizeHint = 0) => throw GetException(sizeHint);

        private readonly Exception GetException(int sizeHint)
        {
            return _bytesWritten + sizeHint > _maxLength
                ? new InvalidOperationException($"Insufficient capacity to perform the requested operation. Buffer size is {_maxLength}. Current length is {_bytesWritten} and requested size increase is {sizeHint}")
                : new NotSupportedException("Method is not supported on this instance");
        }
    }
}
