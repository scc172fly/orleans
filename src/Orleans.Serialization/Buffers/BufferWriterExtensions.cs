using Orleans.Serialization.Session;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Orleans.Serialization.Buffers
{
    /// <summary>
    /// 与 <see cref="IBufferWriter{Byte}"/> 执行有关的工作延期。
    /// </summary>
    public static class BufferWriterExtensions
    {
        /// <summary>
        /// 为提供的缓冲创建 <see cref="Writer{TBufferWriter}"/> 实例。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作器的类型。</typeparam>
        /// <param name="buffer">缓冲。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>新作家。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<TBufferWriter> CreateWriter<TBufferWriter>(this TBufferWriter buffer, SerializerSession session) where TBufferWriter : IBufferWriter<byte>
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(session);
#else
            if (session is null) throw new ArgumentNullException(nameof(session));
#endif
            return Writer.Create(buffer, session);
        }
    }
}
