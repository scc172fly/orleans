using System;
using System.Buffers;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// 将另建一个缓冲作者的 <see cref="IBufferWriter{T}"/> 执行。
    /// </summary>
    /// <typeparam name="TBufferWriter">基础缓冲写作器的类型。</typeparam>
    public class BufferWriterBox<TBufferWriter> : IBufferWriter<byte> where TBufferWriter : struct, IBufferWriter<byte>
    {
        private TBufferWriter _bufferWriter;

        /// <summary>
        /// 初始化 <see cref="BufferWriterBox{TBufferWriter}"/> 类的新实例。
        /// </summary>
        /// <param name="bufferWriter">缓冲写作器到框中。</param>
        public BufferWriterBox(TBufferWriter bufferWriter)
        {
            _bufferWriter = bufferWriter;
        }

        /// <summary>
        /// 获取一个参考文件, 引用了基础缓冲作者 。
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
