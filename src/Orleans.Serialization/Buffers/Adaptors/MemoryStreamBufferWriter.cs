using System;
using System.Buffers;
using System.IO;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// 执行《公约》执行 <see cref="IBufferWriter{T}"/> 写给 <see cref="MemoryStream"/>.
    /// </summary>
    public readonly struct MemoryStreamBufferWriter : IBufferWriter<byte>
    {
        private readonly MemoryStream _stream;
        private const int MinRequestSize = 256;
#if NETSTANDARD2_1
        // 净标准2.1合同中没有Array.MaxLength 。
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

            // 通过 MemoryStream 提交,以便更新长度而不清除字节
            // 直接写到其暴露的缓冲地带。
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
