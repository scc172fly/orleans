using System.Buffers;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// <see cref="IBufferWriter{T}"/> 的一种实现，使用池化数组作为中间缓冲区向 <see cref="Stream"/> 写入数据。
    /// </summary>
    public struct PoolingStreamBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly Stream _stream;
        private byte[] _buffer;
        private const int MinRequestSize = 256;

        /// <summary>
        /// 初始化 <see cref="PoolingStreamBufferWriter"/> 结构的新实例。
        /// </summary>
        /// <param name="stream">流。</param>
        /// <param name="sizeHint">大小提示。</param>
        internal PoolingStreamBufferWriter(Stream stream, int sizeHint)
        {
            _stream = stream;
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, MinRequestSize));
        }

        /// <inheritdoc />
        public void Advance(int count) => _stream.Write(_buffer, 0, count);

        /// <inheritdoc />
        public Memory<byte> GetMemory(int sizeHint = 0) => sizeHint <= _buffer.Length ? _buffer : Resize(sizeHint);

        /// <inheritdoc />
        public Span<byte> GetSpan(int sizeHint = 0) => sizeHint <= _buffer.Length ? _buffer : Resize(sizeHint);

        private byte[] Resize(int sizeHint)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            ArrayPool<byte>.Shared.Return(_buffer);
            return _buffer = newBuffer;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_buffer is { } buf)
            {
                _buffer = null!;
                ArrayPool<byte>.Shared.Return(buf);
            }
        }
    }
}
