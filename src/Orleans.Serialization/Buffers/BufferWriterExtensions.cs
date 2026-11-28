using Orleans.Serialization.Session;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Orleans.Serialization.Buffers
{
    /// <summary>
    /// 用于操作 <see cref="IBufferWriter{Byte}"/> 实现的扩展方法。
    /// </summary>
    public static class BufferWriterExtensions
    {
        /// <summary>
        /// 为给定缓冲区创建 <see cref="Writer{TBufferWriter}"/> 实例。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器的类型。</typeparam>
        /// <param name="buffer">缓冲区。</param>
        /// <param name="session">会话。</param>
        /// <returns>新的写入器。</returns>
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
