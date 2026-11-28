using System;
using System.Buffers;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// <see cref="IBufferWriter{T}"/> 的实现，用于 <see cref="Memory{T}"/>。
    /// </summary>
    public struct MemoryBufferWriter : IBufferWriter<byte>
    {
        private readonly Memory<byte> _buffer;
        private int _bytesWritten;

        /// <summary>
        /// 初始化 <see cref="MemoryBufferWriter"/> 结构的新实例。
        /// </summary>
        /// <param name="buffer">缓冲区。</param>
        internal MemoryBufferWriter(Memory<byte> buffer)
        {
            _buffer = buffer;
            _bytesWritten = 0;
        }

        /// <summary>
        /// 获取已写入的字节数。
        /// </summary>
        /// <value>已写入的字节数。</value>
        public readonly int BytesWritten => _bytesWritten;

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (_bytesWritten > _buffer.Length)
            {
                ThrowInvalidCount();

                static void ThrowInvalidCount() => throw new InvalidOperationException("Cannot advance past the end of the buffer");
            }

            _bytesWritten += count;
        }

        /// <inheritdoc />
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_bytesWritten + sizeHint >= _buffer.Length)
            {
                ThrowInsufficientCapacity(sizeHint);
            }

            return _buffer[_bytesWritten..];
        }

        /// <inheritdoc />
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (_bytesWritten + sizeHint >= _buffer.Length)
            {
                ThrowInsufficientCapacity(sizeHint);
            }

            return _buffer.Span[_bytesWritten..];
        }

        private void ThrowInsufficientCapacity(int sizeHint) => throw new InvalidOperationException($"Insufficient capacity to perform the requested operation. Buffer size is {_buffer.Length}. Current length is {_bytesWritten} and requested size increase is {sizeHint}");
    }
}
