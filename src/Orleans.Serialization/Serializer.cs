using Orleans.Serialization.Buffers;
using Orleans.Serialization.Buffers.Adaptors;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Orleans.Serialization
{
    /// <summary>
    /// 序列化和反序列化值。
    /// </summary>
    public sealed class Serializer
    {
        private readonly SerializerSessionPool _sessionPool;

        /// <summary>
        /// 初始化 <see cref="Serializer"/> 类的新实例。
        /// </summary>
        /// <param name="sessionPool">会话池。</param>
        public Serializer(SerializerSessionPool sessionPool) => _sessionPool = sessionPool;

        /// <summary>
        /// 获取序列化器会话池。
        /// </summary>
        public SerializerSessionPool SessionPool => _sessionPool;

        /// <summary>
        /// 返回专用于给定类型参数的序列化器。
        /// </summary>
        /// <typeparam name="T">返回的序列化器所处理的底层类型。</typeparam>
        public Serializer<T> GetSerializer<T>() => new(_sessionPool);

        /// <summary>
        /// 返回 <see langword="true"/> 表示给定类型 <typeparamref name="T"/> 可以序列化，否则返回 <see langword="false"/>。
        /// </summary>
        public bool CanSerialize<T>() => _sessionPool.CodecProvider.TryGetCodec(typeof(T)) is { };

        /// <summary>
        /// 返回 <see langword="true"/> 表示给定类型 <paramref name="type"/> 可以序列化，否则返回 <see langword="false"/>。
        /// </summary>
        public bool CanSerialize(Type type) => _sessionPool.CodecProvider.TryGetCodec(type) is { };

        /// <summary>
        /// 将给定的 <paramref name="value"/> 序列化到新数组中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <returns>包含序列化后值的字节数组。</returns>
        public byte[] SerializeToArray<T>([AllowNull] T value)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.CreatePooled(session);
            try
            {
                var codec = session.CodecProvider.GetCodec<T>();
                codec.WriteField(ref writer, 0, typeof(T), value);
                writer.Commit();
                return writer.Output.ToArray();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize<T>([AllowNull] T value, ref Memory<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize<T>([AllowNull] T value, ref Memory<byte> destination, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize<T>([AllowNull] T value, Stream destination, int sizeHint = 0)
        {
            using var session = _sessionPool.GetSession();
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                var codec = session.CodecProvider.GetCodec<T>();
                codec.WriteField(ref writer, 0, typeof(T), value);
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    var codec = session.CodecProvider.GetCodec<T>();
                    codec.WriteField(ref writer, 0, typeof(T), value);
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize<T>([AllowNull] T value, Stream destination, SerializerSession session, int sizeHint = 0)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                var codec = session.CodecProvider.GetCodec<T>();
                codec.WriteField(ref writer, 0, typeof(T), value);
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    var codec = session.CodecProvider.GetCodec<T>();
                    codec.WriteField(ref writer, 0, typeof(T), value);
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        public void Serialize<T, TBufferWriter>([AllowNull] T value, TBufferWriter destination) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        public void Serialize<T, TBufferWriter>([AllowNull] T value, TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte>
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        public void Serialize<T, TBufferWriter>([AllowNull] T value, ref Writer<TBufferWriter> destination) where TBufferWriter : IBufferWriter<byte>
        {
            var codec = destination.Session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref destination, 0, typeof(T), value);
            destination.Commit();
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize<T>([AllowNull] T value, ref Span<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize<T>([AllowNull] T value, ref Span<byte> destination, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize<T>([AllowNull] T value, byte[] destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize<T>([AllowNull] T value, ArraySegment<byte> destination)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize<T>([AllowNull] T value, ArraySegment<byte> destination, SerializerSession session)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan, session);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="T"><paramref name="value"/> 的预期类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize<T>([AllowNull] T value, byte[] destination, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(Stream source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(Stream source, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlySequence<byte> source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlySequence<byte> source, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(PooledBuffer.BufferSlice source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(PooledBuffer.BufferSlice source, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ArcBuffer source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ArcBuffer source, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlySpan<byte> source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlySpan<byte> source, SerializerSession session)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec<T>();
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(byte[] source) => Deserialize<T>(source.AsSpan());

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(byte[] source, SerializerSession session) => Deserialize<T>(source.AsSpan(), session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlyMemory<byte> source) => Deserialize<T>(source.Span);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlyMemory<byte> source, SerializerSession session) => Deserialize<T>(source.Span, session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ArraySegment<byte> source) => Deserialize<T>(source.AsSpan());

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ArraySegment<byte> source, SerializerSession session) => Deserialize<T>(source.AsSpan(), session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为读取器。）</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<T, TInput>(ref Reader<TInput> source)
        {
            var codec = source.Session.CodecProvider.GetCodec<T>();
            var field = source.ReadFieldHeader();
            return codec.ReadValue(ref source, field);
        }
    }

    /// <summary>
    /// 序列化和反序列化值。
    /// </summary>
    /// <typeparam name="T">此实例进行序列化和反序列化的值的类型。</typeparam>
    public sealed class Serializer<T>
    {
        private readonly IFieldCodec<T> _codec;
        private readonly SerializerSessionPool _sessionPool;
        private readonly Type _expectedType = typeof(T);

        /// <summary>
        /// 初始化 <see cref="Serializer{T}"/> 类的新实例。
        /// </summary>
        /// <param name="sessionPool">会话池。</param>
        public Serializer(SerializerSessionPool sessionPool) : this(GetCodec(sessionPool), sessionPool)
        {
        }

        private static IFieldCodec<T> GetCodec(SerializerSessionPool sessionPool)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(sessionPool);
            return OrleansGeneratedCodeHelper.UnwrapService(null!, sessionPool.CodecProvider.GetCodec<T>());
        }

        /// <summary>
        /// 初始化 <see cref="Serializer{T}"/> 类的新实例。
        /// </summary>
        /// <param name="codec">编解码器。</param>
        /// <param name="sessionPool">会话池。</param>
        public Serializer(IFieldCodec<T> codec, SerializerSessionPool sessionPool)
        {
            _codec = codec;
            _sessionPool = sessionPool;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        public void Serialize<TBufferWriter>([AllowNull] T value, ref Writer<TBufferWriter> destination) where TBufferWriter : IBufferWriter<byte>
        {
            _codec.WriteField(ref destination, 0, _expectedType, value);
            destination.Commit();
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        public void Serialize<TBufferWriter>([AllowNull] T value, TBufferWriter destination) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        public void Serialize<TBufferWriter>([AllowNull] T value, TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte>
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将给定的 <paramref name="value"/> 序列化到新数组中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <returns>包含序列化后值的字节数组。</returns>
        public byte[] SerializeToArray([AllowNull] T value)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.CreatePooled(session);
            try
            {
                _codec.WriteField(ref writer, 0, _expectedType, value);
                writer.Commit();
                return writer.Output.ToArray();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize([AllowNull] T value, ref Memory<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize([AllowNull] T value, ref Memory<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize([AllowNull] T value, ref Span<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize([AllowNull] T value, ref Span<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize([AllowNull] T value, byte[] destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize([AllowNull] T value, byte[] destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize([AllowNull] T value, Stream destination, int sizeHint = 0)
        {
            using var session = _sessionPool.GetSession();
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                _codec.WriteField(ref writer, 0, _expectedType, value);
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    _codec.WriteField(ref writer, 0, _expectedType, value);
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize([AllowNull] T value, Stream destination, SerializerSession session, int sizeHint = 0)
        {
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                _codec.WriteField(ref writer, 0, _expectedType, value);
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    _codec.WriteField(ref writer, 0, _expectedType, value);
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为读取器。）</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize<TInput>(ref Reader<TInput> source)
        {
            var field = source.ReadFieldHeader();
            return _codec.ReadValue(ref source, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(Stream source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(Stream source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySequence<byte> source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ArraySegment<byte> source) => Deserialize(source.AsSpan());

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySequence<byte> source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(PooledBuffer.BufferSlice source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(PooledBuffer.BufferSlice source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ArcBuffer source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ArcBuffer source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySpan<byte> source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySpan<byte> source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(byte[] source) => Deserialize(source.AsSpan());

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(byte[] source, SerializerSession session) => Deserialize(source.AsSpan(), session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlyMemory<byte> source) => Deserialize(source.Span);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlyMemory<byte> source, SerializerSession session) => Deserialize(source.Span, session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: MaybeNull]
        public T Deserialize(ArraySegment<byte> source, SerializerSession session) => Deserialize(source.AsSpan(), session);
    }

    /// <summary>
    /// 序列化和反序列化值类型。
    /// </summary>
    /// <typeparam name="T">此实例所操作的类型。</typeparam>
    public sealed class ValueSerializer<T> where T : struct
    {
        private readonly IValueSerializer<T> _codec;
        private readonly SerializerSessionPool _sessionPool;

        /// <summary>
        /// 初始化 <see cref="ValueSerializer{T}"/> 类的新实例。
        /// </summary>
        /// <param name="codecProvider">编解码器提供程序。</param>
        /// <param name="sessionPool">会话池。</param>
        public ValueSerializer(IValueSerializerProvider codecProvider, SerializerSessionPool sessionPool)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(codecProvider);
            ArgumentNullExceptionPolyfill.ThrowIfNull(sessionPool);
            _sessionPool = sessionPool;
            _codec = OrleansGeneratedCodeHelper.UnwrapService(null!, codecProvider.GetValueSerializer<T>());
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        public void Serialize<TBufferWriter>(scoped ref T value, ref Writer<TBufferWriter> destination) where TBufferWriter : IBufferWriter<byte>
        {
            _codec.Serialize(ref destination, ref value);
            destination.WriteEndObject();
            destination.Commit();
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        public void Serialize<TBufferWriter>(scoped ref T value, TBufferWriter destination) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        public void Serialize<TBufferWriter>(scoped ref T value, TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte>
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将给定的 <paramref name="value"/> 序列化到新数组中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <returns>包含序列化后值的字节数组。</returns>
        public byte[] SerializeToArray(scoped ref T value)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.CreatePooled(session);
            try
            {
                _codec.Serialize(ref writer, ref value);
                writer.WriteEndObject();
                writer.Commit();
                return writer.Output.ToArray();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。（译注：此重载按值接收 ArraySegment，仅调整局部 Span，不会改变调用方的 destination。）</remarks>
        public void Serialize(scoped ref T value, ArraySegment<byte> destination)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(ref value, ref destinationSpan);
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(scoped ref T value, ref Memory<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(scoped ref T value, ref Memory<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(scoped ref T value, ref Span<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(scoped ref T value, ref Span<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize(scoped ref T value, byte[] destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize(scoped ref T value, byte[] destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize(scoped ref T value, Stream destination, int sizeHint = 0)
        {
            using var session = _sessionPool.GetSession();
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                _codec.Serialize(ref writer, ref value);
                writer.WriteEndObject();
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    _codec.Serialize(ref writer, ref value);
                    writer.WriteEndObject();
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize(scoped ref T value, Stream destination, SerializerSession session, int sizeHint = 0)
        {
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                _codec.Serialize(ref writer, ref value);
                writer.WriteEndObject();
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    _codec.Serialize(ref writer, ref value);
                    writer.WriteEndObject();
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为读取器。）</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize<TInput>(ref Reader<TInput> source, scoped ref T result)
        {
            _codec.Deserialize(ref source, ref result);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(Stream source, scoped ref T result)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(Stream source, scoped ref T result, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ReadOnlySequence<byte> source, scoped ref T result)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ReadOnlySequence<byte> source, scoped ref T result, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ArraySegment<byte> source, scoped ref T result, SerializerSession session) => Deserialize(source.AsSpan(), ref result, session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ReadOnlySpan<byte> source, scoped ref T result)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ReadOnlySpan<byte> source, scoped ref T result, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(byte[] source, scoped ref T result) => Deserialize(source.AsSpan(), ref result);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(byte[] source, scoped ref T result, SerializerSession session) => Deserialize(source.AsSpan(), ref result, session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ReadOnlyMemory<byte> source, scoped ref T result) => Deserialize(source.Span, ref result);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <param name="session">序列化器会话。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ReadOnlyMemory<byte> source, scoped ref T result, SerializerSession session) => Deserialize(source.Span, ref result, session);

        /// <summary>
        /// 将 <typeparamref name="T"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="result">反序列化得到的值。</param>
        /// <returns>反序列化得到的值。（译注：此方法返回 void，结果通过 result 引用参数写入。）</returns>
        public void Deserialize(ArraySegment<byte> source, scoped ref T result) => Deserialize(source.AsSpan(), ref result);
    }

    /// <summary>
    /// 提供对编译时未知类型的值进行序列化及反序列化的方法。
    /// </summary>
    public sealed class ObjectSerializer
    {
        private readonly SerializerSessionPool _sessionPool;

        /// <summary>
        /// 初始化 <see cref="ObjectSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="sessionPool">会话池。</param>
        public ObjectSerializer(SerializerSessionPool sessionPool) => _sessionPool = sessionPool;

        /// <summary>
        /// 返回 <see langword="true"/> 表示给定类型 <paramref name="type"/> 可以序列化，否则返回 <see langword="false"/>。
        /// </summary>
        public bool CanSerialize(Type type) => _sessionPool.CodecProvider.TryGetCodec(type) is { };

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="type">值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(object? value, ref Memory<byte> destination, Type type)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(object? value, ref Memory<byte> destination, SerializerSession session, Type type)
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="type">值的预期类型。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize(object? value, Stream destination, Type type, int sizeHint = 0)
        {
            using var session = _sessionPool.GetSession();
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                ObjectCodec.WriteField(ref writer, 0, type, value);
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    ObjectCodec.WriteField(ref writer, 0, type, value);
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <param name="sizeHint">序列化数据长度的估计上限。</param>
        /// <remarks>此方法不会刷新目标流。</remarks>
        public void Serialize(object? value, Stream destination, SerializerSession session, Type type, int sizeHint = 0)
        {
            if (destination is MemoryStream memoryStream)
            {
                var writer = Writer.Create(memoryStream, session);
                ObjectCodec.WriteField(ref writer, 0, type, value);
                writer.Commit();
            }
            else
            {
                var writer = Writer.CreatePooled(destination, session, sizeHint);
                try
                {
                    ObjectCodec.WriteField(ref writer, 0, type, value);
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="type">值的预期类型。</param>
        public void Serialize<TBufferWriter>(object? value, TBufferWriter destination, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        public void Serialize<TBufferWriter>(object? value, TBufferWriter destination, SerializerSession session, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();

            // 不要释放，因为缓冲区写入器不由此方法拥有。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲区写入器的类型。</typeparam>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="type">值的预期类型。</param>
        public void Serialize<TBufferWriter>(object? value, ref Writer<TBufferWriter> destination, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            ObjectCodec.WriteField(ref destination, 0, type, value);
            destination.Commit();
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="type">值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(object? value, ref Span<byte> destination, Type type)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切片至序列化数据的长度。</remarks>
        public void Serialize(object? value, ref Span<byte> destination, SerializerSession session, Type type)
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize(object? value, byte[] destination, Type type)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize(object? value, ArraySegment<byte> destination, Type type)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan, type);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize(object? value, ArraySegment<byte> destination, SerializerSession session, Type type)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan, session, type);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 序列化到 <paramref name="destination"/> 中。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        /// <param name="destination">写入序列化数据的目标位置。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>序列化数据的长度。</returns>
        public int Serialize(object? value, byte[] destination, SerializerSession session, Type type)
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(Stream source, Type type)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为输入流。）</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(Stream source, SerializerSession session, Type type)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ReadOnlySequence<byte> source, Type type)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ReadOnlySequence<byte> source, SerializerSession session, Type type)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ReadOnlySpan<byte> source, Type type)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ReadOnlySpan<byte> source, SerializerSession session, Type type)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(byte[] source, Type type) => Deserialize(source.AsSpan(), type);

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(byte[] source, SerializerSession session, Type type) => Deserialize(source.AsSpan(), session, type);

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ReadOnlyMemory<byte> source, Type type) => Deserialize(source.Span, type);

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ReadOnlyMemory<byte> source, SerializerSession session, Type type) => Deserialize(source.Span, session, type);

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ArraySegment<byte> source, Type type) => Deserialize(source.AsSpan(), type);

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <param name="source">源缓冲区。</param>
        /// <param name="session">序列化器会话。</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize(ArraySegment<byte> source, SerializerSession session, Type type) => Deserialize(source.AsSpan(), session, type);

        /// <summary>
        /// 将 <paramref name="type"/> 类型的值从 <paramref name="source"/> 中反序列化。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="source">源缓冲区。（译注：此重载的 source 实际为读取器。）</param>
        /// <param name="type">值的预期类型。</param>
        /// <returns>反序列化得到的值。</returns>
        public object? Deserialize<TInput>(ref Reader<TInput> source, Type type)
        {
            var codec = source.Session.CodecProvider.GetCodec(type);
            var field = source.ReadFieldHeader();
            return codec.ReadValue(ref source, field);
        }
    }

    /// <summary>
    /// 提供复制对象和值的功能。
    /// </summary>
    public sealed class DeepCopier
    {
        private readonly CodecProvider _codecProvider;
        private readonly CopyContextPool _contextPool;

        /// <summary>
        /// 初始化 <see cref="DeepCopier"/> 类的新实例。
        /// </summary>
        /// <param name="codecProvider">编解码器提供程序。</param>
        /// <param name="contextPool">上下文池。</param>
        public DeepCopier(CodecProvider codecProvider, CopyContextPool contextPool)
        {
            _codecProvider = codecProvider;
            _contextPool = contextPool;
        }

        /// <summary>
        /// 返回专用于给定类型参数的复制器。
        /// </summary>
        /// <typeparam name="T">返回的复制器所处理的底层类型。</typeparam>
        public DeepCopier<T> GetCopier<T>() => new(_codecProvider.GetDeepCopier<T>(), _contextPool);

        /// <summary>
        /// 创建给定值的副本。
        /// </summary>
        /// <typeparam name="T">要复制的值的类型。</typeparam>
        /// <param name="value">要复制的值。</param>
        /// <returns>给定值的副本。</returns>
        [return: NotNullIfNotNull(nameof(value))]
        public T? Copy<T>(T? value)
        {
            using var context = _contextPool.GetContext();
            return context.DeepCopy(value);
        }
    }

    /// <summary>
    /// 提供复制对象和值的功能。
    /// </summary>
    public sealed class DeepCopier<T>
    {
        private readonly IDeepCopier<T> _copier;
        private readonly CopyContextPool _contextPool;

        /// <summary>
        /// 初始化 <see cref="DeepCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">复制器。</param>
        /// <param name="contextPool">上下文池。</param>
        public DeepCopier(IDeepCopier<T> copier, CopyContextPool contextPool)
        {
            _copier = copier;
            _contextPool = contextPool;
        }

        /// <summary>
        /// 创建给定值的副本。
        /// </summary>
        /// <param name="value">要复制的值。</param>
        /// <returns>给定值的副本。</returns>
        [return: NotNullIfNotNull(nameof(value))]
        public T? Copy(T? value)
        {
            using var context = _contextPool.GetContext();
            return _copier.DeepCopy(value, context);
        }
    }
}
