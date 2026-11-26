using System;
using System.Buffers;
using System.IO;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// 执行《公约》执行 <see cref="IBufferWriter{T}"/> 写给 <see cref="Stream"/>,将一个阵列作为中间缓冲。
    /// </summary>
    public struct ArrayStreamBufferWriter : IBufferWriter<byte>
    {
        /// <summary>
        /// 中间缓冲的默认初始大小, 以字节计 。
        /// </summary>
        public const int DefaultInitialBufferSize = 256;
        private readonly Stream _stream;
        private byte[] _buffer;
        private int _index;

        /// <summary>
        /// 初始化 <see cref="ArrayStreamBufferWriter"/> 结构的新实例。
        /// </summary>
        /// <param name="stream">流。</param>
        /// <param name="sizeHint">大小提示。</param>
        public ArrayStreamBufferWriter(Stream stream, int sizeHint = 0)
        {
            if (sizeHint == 0)
            {
                sizeHint = DefaultInitialBufferSize;
            }

            _stream = stream;
            _buffer = new byte[sizeHint];
            _index = 0;
        }

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (count < 0)
            {
                ThrowNegativeAdvanceCount();
                return;
            }

            if (_index > _buffer.Length - count)
            {
                ThrowAdvancePastCapacity();
                return;
            }

            _index += count;
            _stream.Write(_buffer, 0, _index);
            _index = 0;
        }

        /// <inheritdoc />
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_index);
        }

        /// <inheritdoc />
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_index);
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (sizeHint < 0)
            {
                ThrowNegativeSizeHint();
                return;
            }

            if (sizeHint == 0)
            {
                sizeHint = 1;
            }

            if (sizeHint > _buffer.Length - _index)
            {
                int growBy = Math.Max(sizeHint, _buffer.Length);

                if (_buffer.Length == 0)
                {
                    growBy = Math.Max(growBy, DefaultInitialBufferSize);
                }

                int newSize = checked(_buffer.Length + growBy);

                Array.Resize(ref _buffer, newSize);
            }
        }

        private static void ThrowNegativeSizeHint() => throw new ArgumentException("Negative values are not supported", "sizeHint");

        private static void ThrowNegativeAdvanceCount() => throw new ArgumentException("Negative values are not supported", "count");

        private static void ThrowAdvancePastCapacity() => throw new InvalidOperationException("Cannod advance past the end of the current capacity");
    }
}
