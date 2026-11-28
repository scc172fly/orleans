using System;
using System.Buffers;
using System.IO;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// <see cref="IBufferWriter{T}"/> 的一种实现，向 <see cref="MemoryStream"/> 写入数据。
    /// </summary>
    public readonly struct MemoryStreamBufferWriter : IBufferWriter<byte>
    {
        private readonly MemoryStream _stream;
        private const int MinRequestSize = 256;
#if NETSTANDARD2_1
        // netstandard2.1 的契约中没有 Array.MaxLength。
        private const int MaxArrayLength = 0x7FFFFFC7;
#else
        private static readonly int MaxArrayLength = Array.MaxLength;
#endif

        /// <summary>
        /// 初始化 <see cref="MemoryStreamBufferWriter"/> 结构的新实例。
        /// </summary>
        /// <param name="stream">流。</param>
        public MemoryStreamBufferWriter(MemoryStream stream)
        {
            _stream = stream;
        }

        /// <inheritdoc />
        public void Advance(int count)
        {
            if (count == 0)
            {
                return;
            }

            var position = checked((int)_stream.Position);
            var availableCapacity = _stream.Capacity - position;
            if (count < 0 || count > availableCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var newPosition = position + count;
            if (newPosition <= _stream.Length)
            {
                _stream.Position = newPosition;
                return;
            }

            // 通过 MemoryStream 提交，使其更新 Length，而不清除
            // 已直接写入其公开缓冲区的字节。
            if (_stream.Position > _stream.Length)
            {
                _stream.SetLength(_stream.Position);
            }

            var (buffer, offset) = GetBuffer();
            _stream.Write(buffer.AsSpan(offset + position, count));
        }

        /// <inheritdoc />
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            var position = EnsureCapacity(sizeHint);
            var (buffer, offset) = GetBuffer();
            return buffer.AsMemory(offset + position, _stream.Capacity - position);
        }

        /// <inheritdoc />
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            var position = EnsureCapacity(sizeHint);
            var (buffer, offset) = GetBuffer();
            return buffer.AsSpan(offset + position, _stream.Capacity - position);
        }

        private int EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }

            var position = checked((int)_stream.Position);
            var requiredCapacity = checked(position + Math.Max(sizeHint, 1));
            if (_stream.Capacity < requiredCapacity)
            {
                requiredCapacity = checked(position + Math.Max(sizeHint, MinRequestSize));
                var doubledCapacity = Math.Min((long)_stream.Capacity * 2, MaxArrayLength);
                _stream.Capacity = Math.Max(requiredCapacity, (int)doubledCapacity);
            }

            return position;
        }

        private (byte[] Buffer, int Offset) GetBuffer()
        {
            var buffer = _stream.GetBuffer();
            _ = _stream.TryGetBuffer(out var segment);
            return (buffer, segment.Offset);
        }
    }
}
