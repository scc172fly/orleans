using System;
using System.Buffers;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// <see cref="IBufferWriter{T}"/> 的一种实现，用于装箱另一个缓冲区写入器。
    /// </summary>
    /// <typeparam name="TBufferWriter">底层缓冲区写入器的类型。</typeparam>
    public class BufferWriterBox<TBufferWriter> : IBufferWriter<byte> where TBufferWriter : struct, IBufferWriter<byte>
    {
        private TBufferWriter _bufferWriter;

        /// <summary>
        /// 初始化 <see cref="BufferWriterBox{TBufferWriter}"/> 类的新实例。
        /// </summary>
        /// <param name="bufferWriter">要装箱的缓冲区写入器。</param>
        public BufferWriterBox(TBufferWriter bufferWriter)
        {
            _bufferWriter = bufferWriter;
        }

        /// <summary>
        /// 获取对底层缓冲区写入器的引用。
        /// </summary>
        public ref TBufferWriter Value => ref _bufferWriter;

        /// <inheritdoc/>
        public void Advance(int count) => _bufferWriter.Advance(count);

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0) => _bufferWriter.GetMemory(sizeHint);

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0) => _bufferWriter.GetSpan(sizeHint);
    }
}
