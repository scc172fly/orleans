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
    /// 分层和消散价值。
    /// </summary>
    public sealed class Serializer
    {
        private readonly SerializerSessionPool _sessionPool;

        /// <summary>
        /// 初始化 <see cref="Serializer"/> 类的新实例。
        /// </summary>
        /// <param name="sessionPool">届会池。</param>
        public Serializer(SerializerSessionPool sessionPool) => _sessionPool = sessionPool;

        /// <summary>
        /// 获得连环器会话池。
        /// </summary>
        public SerializerSessionPool SessionPool => _sessionPool;

        /// <summary>
        /// 返回一个用于提供类型参数的序列化器。
        /// </summary>
        /// <typeparam name="T">返回的序列器的基础类型。</typeparam>
        public Serializer<T> GetSerializer<T>() => new(_sessionPool);

        /// <summary>
        /// 返回 <see langword="true"/> 如果是规定的类型, <typeparamref name="T"/>,可以按序排列,并且 <see langword="false"/> 否则。
        /// </summary>
        public bool CanSerialize<T>() => _sessionPool.CodecProvider.TryGetCodec(typeof(T)) is { };

        /// <summary>
        /// 返回 <see langword="true"/> 如果是规定的类型, <paramref name="type"/>,可以按序排列,并且 <see langword="false"/> 否则。
        /// </summary>
        public bool CanSerialize(Type type) => _sessionPool.CodecProvider.TryGetCodec(type) is { };

        /// <summary>
        /// 将提供的 <paramref name="value"/> 编序成新的数组。
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <returns>包含序列值的字节数组。</returns>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        public void Serialize<T, TBufferWriter>([AllowNull] T value, TBufferWriter destination) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        public void Serialize<T, TBufferWriter>([AllowNull] T value, TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte>
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var writer = Writer.Create(destination, session);
            var codec = session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref writer, 0, typeof(T), value);
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        public void Serialize<T, TBufferWriter>([AllowNull] T value, ref Writer<TBufferWriter> destination) where TBufferWriter : IBufferWriter<byte>
        {
            var codec = destination.Session.CodecProvider.GetCodec<T>();
            codec.WriteField(ref destination, 0, typeof(T), value);
            destination.Commit();
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <returns>序列数据长度。</returns>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize<T>([AllowNull] T value, ArraySegment<byte> destination)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize<T>([AllowNull] T value, ArraySegment<byte> destination, SerializerSession session)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan, session);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="T">所预期的 <paramref name="value"/>.</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>序列数据长度。</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<T>(byte[] source) => Deserialize<T>(source.AsSpan());

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<T>(byte[] source, SerializerSession session) => Deserialize<T>(source.AsSpan(), session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlyMemory<byte> source) => Deserialize<T>(source.Span);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ReadOnlyMemory<byte> source, SerializerSession session) => Deserialize<T>(source.Span, session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ArraySegment<byte> source) => Deserialize<T>(source.AsSpan());

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<T>(ArraySegment<byte> source, SerializerSession session) => Deserialize<T>(source.AsSpan(), session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<T, TInput>(ref Reader<TInput> source)
        {
            var codec = source.Session.CodecProvider.GetCodec<T>();
            var field = source.ReadFieldHeader();
            return codec.ReadValue(ref source, field);
        }
    }

    /// <summary>
    /// 分层和消散价值。
    /// </summary>
    /// <typeparam name="T">本例序列和取消的值类型。</typeparam>
    public sealed class Serializer<T>
    {
        private readonly IFieldCodec<T> _codec;
        private readonly SerializerSessionPool _sessionPool;
        private readonly Type _expectedType = typeof(T);

        /// <summary>
        /// 初始化 <see cref="Serializer{T}"/> 类的新实例。
        /// </summary>
        /// <param name="sessionPool">届会池。</param>
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
        /// <param name="codec">代码码</param>
        /// <param name="sessionPool">届会池。</param>
        public Serializer(IFieldCodec<T> codec, SerializerSessionPool sessionPool)
        {
            _codec = codec;
            _sessionPool = sessionPool;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        public void Serialize<TBufferWriter>([AllowNull] T value, ref Writer<TBufferWriter> destination) where TBufferWriter : IBufferWriter<byte>
        {
            _codec.WriteField(ref destination, 0, _expectedType, value);
            destination.Commit();
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        public void Serialize<TBufferWriter>([AllowNull] T value, TBufferWriter destination) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        public void Serialize<TBufferWriter>([AllowNull] T value, TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte>
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 编序成新的数组。
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <returns>包含序列值的字节数组。</returns>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize([AllowNull] T value, ref Memory<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize([AllowNull] T value, ref Memory<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize([AllowNull] T value, ref Span<byte> destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize([AllowNull] T value, ref Span<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize([AllowNull] T value, byte[] destination)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize([AllowNull] T value, byte[] destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.WriteField(ref writer, 0, _expectedType, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize<TInput>(ref Reader<TInput> source)
        {
            var field = source.ReadFieldHeader();
            return _codec.ReadValue(ref source, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(Stream source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(Stream source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySequence<byte> source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ArraySegment<byte> source) => Deserialize(source.AsSpan());

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySequence<byte> source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(PooledBuffer.BufferSlice source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(PooledBuffer.BufferSlice source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ArcBuffer source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ArcBuffer source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySpan<byte> source)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlySpan<byte> source, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            var field = reader.ReadFieldHeader();
            return _codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(byte[] source) => Deserialize(source.AsSpan());

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(byte[] source, SerializerSession session) => Deserialize(source.AsSpan(), session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlyMemory<byte> source) => Deserialize(source.Span);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ReadOnlyMemory<byte> source, SerializerSession session) => Deserialize(source.Span, session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        [return: MaybeNull]
        public T Deserialize(ArraySegment<byte> source, SerializerSession session) => Deserialize(source.AsSpan(), session);
    }

    /// <summary>
    /// 分层和分层处理价值类型。
    /// </summary>
    /// <typeparam name="T">本实例操作的类型。</typeparam>
    public sealed class ValueSerializer<T> where T : struct
    {
        private readonly IValueSerializer<T> _codec;
        private readonly SerializerSessionPool _sessionPool;

        /// <summary>
        /// 初始化 <see cref="ValueSerializer{T}"/> 类的新实例。
        /// </summary>
        /// <param name="codecProvider">编码器提供者。</param>
        /// <param name="sessionPool">届会池。</param>
        public ValueSerializer(IValueSerializerProvider codecProvider, SerializerSessionPool sessionPool)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(codecProvider);
            ArgumentNullExceptionPolyfill.ThrowIfNull(sessionPool);
            _sessionPool = sessionPool;
            _codec = OrleansGeneratedCodeHelper.UnwrapService(null!, codecProvider.GetValueSerializer<T>());
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        public void Serialize<TBufferWriter>(scoped ref T value, ref Writer<TBufferWriter> destination) where TBufferWriter : IBufferWriter<byte>
        {
            _codec.Serialize(ref destination, ref value);
            destination.WriteEndObject();
            destination.Commit();
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        public void Serialize<TBufferWriter>(scoped ref T value, TBufferWriter destination) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        public void Serialize<TBufferWriter>(scoped ref T value, TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte>
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 将提供的 <paramref name="value"/> 编序成新的数组。
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <returns>包含序列值的字节数组。</returns>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize(scoped ref T value, ArraySegment<byte> destination)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(ref value, ref destinationSpan);
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize(scoped ref T value, ref Memory<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize(scoped ref T value, ref Span<byte> destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <returns>序列数据长度。</returns>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize(scoped ref T value, byte[] destination, SerializerSession session)
        {
            var writer = Writer.Create(destination, session);
            _codec.Serialize(ref writer, ref value);
            writer.WriteEndObject();
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <returns>消毒价值</returns>
        public void Deserialize<TInput>(ref Reader<TInput> source, scoped ref T result)
        {
            _codec.Deserialize(ref source, ref result);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(Stream source, scoped ref T result)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(Stream source, scoped ref T result, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ReadOnlySequence<byte> source, scoped ref T result)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ReadOnlySequence<byte> source, scoped ref T result, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ArraySegment<byte> source, scoped ref T result, SerializerSession session) => Deserialize(source.AsSpan(), ref result, session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ReadOnlySpan<byte> source, scoped ref T result)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ReadOnlySpan<byte> source, scoped ref T result, SerializerSession session)
        {
            var reader = Reader.Create(source, session);
            _codec.Deserialize(ref reader, ref result);
        }

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(byte[] source, scoped ref T result) => Deserialize(source.AsSpan(), ref result);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(byte[] source, scoped ref T result, SerializerSession session) => Deserialize(source.AsSpan(), ref result, session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ReadOnlyMemory<byte> source, scoped ref T result) => Deserialize(source.Span, ref result);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <param name="session">序列器会话。</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ReadOnlyMemory<byte> source, scoped ref T result, SerializerSession session) => Deserialize(source.Span, ref result, session);

        /// <summary>
        /// 使类型值降序 <typeparamref name="T"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="result">消毒价值</param>
        /// <returns>消毒价值</returns>
        public void Deserialize(ArraySegment<byte> source, scoped ref T result) => Deserialize(source.AsSpan(), ref result);
    }

    /// <summary>
    /// 提供序列化和去矿化方法,这些数值的类型不固定。
    /// </summary>
    public sealed class ObjectSerializer
    {
        private readonly SerializerSessionPool _sessionPool;

        /// <summary>
        /// 初始化 <see cref="ObjectSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="sessionPool">届会池。</param>
        public ObjectSerializer(SerializerSessionPool sessionPool) => _sessionPool = sessionPool;

        /// <summary>
        /// 返回 <see langword="true"/> 如果是规定的类型, <paramref name="type"/>,可以按序排列,并且 <see langword="false"/> 否则。
        /// </summary>
        public bool CanSerialize(Type type) => _sessionPool.CodecProvider.TryGetCodec(type) is { };

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize(object? value, ref Memory<byte> destination, Type type)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize(object? value, ref Memory<byte> destination, SerializerSession session, Type type)
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <param name="sizeHint">序列数据长度的估计上限。</param>
        /// <remarks>目标流不会被此方法冲洗。</remarks>
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
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="type">数值的预期类型。</param>
        public void Serialize<TBufferWriter>(object? value, TBufferWriter destination, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        public void Serialize<TBufferWriter>(object? value, TBufferWriter destination, SerializerSession session, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();

            // 不处置, 因为缓冲写作器不是方法所拥有 。
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">输出缓冲写器。</typeparam>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="type">数值的预期类型。</param>
        public void Serialize<TBufferWriter>(object? value, ref Writer<TBufferWriter> destination, Type type) where TBufferWriter : IBufferWriter<byte>
        {
            ObjectCodec.WriteField(ref destination, 0, type, value);
            destination.Commit();
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize(object? value, ref Span<byte> destination, Type type)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <remarks>此方法将 <paramref name="destination"/> 切除到序列数据长度。</remarks>
        public void Serialize(object? value, ref Span<byte> destination, SerializerSession session, Type type)
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            destination = destination[..writer.Position];
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize(object? value, byte[] destination, Type type)
        {
            using var session = _sessionPool.GetSession();
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize(object? value, ArraySegment<byte> destination, Type type)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan, type);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize(object? value, ArraySegment<byte> destination, SerializerSession session, Type type)
        {
            var destinationSpan = destination.AsSpan();
            Serialize(value, ref destinationSpan, session, type);
            return destinationSpan.Length;
        }

        /// <summary>
        /// 提供分级 <paramref name="value"/> 与 <paramref name="destination"/>.
        /// </summary>
        /// <param name="value">序列化的值。</param>
        /// <param name="destination">将编写序列数据的地点。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>序列数据长度。</returns>
        public int Serialize(object? value, byte[] destination, SerializerSession session, Type type)
        {
            var writer = Writer.Create(destination, session);
            ObjectCodec.WriteField(ref writer, 0, type, value);
            writer.Commit();
            return writer.Position;
        }

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(Stream source, Type type)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(Stream source, SerializerSession session, Type type)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ReadOnlySequence<byte> source, Type type)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ReadOnlySequence<byte> source, SerializerSession session, Type type)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ReadOnlySpan<byte> source, Type type)
        {
            using var session = _sessionPool.GetSession();
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ReadOnlySpan<byte> source, SerializerSession session, Type type)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(session);
            var reader = Reader.Create(source, session);
            var codec = session.CodecProvider.GetCodec(type);
            var field = reader.ReadFieldHeader();
            return codec.ReadValue(ref reader, field);
        }

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(byte[] source, Type type) => Deserialize(source.AsSpan(), type);

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(byte[] source, SerializerSession session, Type type) => Deserialize(source.AsSpan(), session, type);

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ReadOnlyMemory<byte> source, Type type) => Deserialize(source.Span, type);

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ReadOnlyMemory<byte> source, SerializerSession session, Type type) => Deserialize(source.Span, session, type);

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ArraySegment<byte> source, Type type) => Deserialize(source.AsSpan(), type);

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <param name="source">源缓冲。</param>
        /// <param name="session">序列器会话。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize(ArraySegment<byte> source, SerializerSession session, Type type) => Deserialize(source.AsSpan(), session, type);

        /// <summary>
        /// 使类型值降序 <paramref name="type"/> 调自 <paramref name="source"/>.
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="source">源缓冲。</param>
        /// <param name="type">数值的预期类型。</param>
        /// <returns>消毒价值</returns>
        public object? Deserialize<TInput>(ref Reader<TInput> source, Type type)
        {
            var codec = source.Session.CodecProvider.GetCodec(type);
            var field = source.ReadFieldHeader();
            return codec.ReadValue(ref source, field);
        }
    }

    /// <summary>
    /// 提供复制对象和价值的功能。
    /// </summary>
    public sealed class DeepCopier
    {
        private readonly CodecProvider _codecProvider;
        private readonly CopyContextPool _contextPool;

        /// <summary>
        /// 初始化 <see cref="DeepCopier"/> 类的新实例。
        /// </summary>
        /// <param name="codecProvider">编码器提供者。</param>
        /// <param name="contextPool">上下文集合。</param>
        public DeepCopier(CodecProvider codecProvider, CopyContextPool contextPool)
        {
            _codecProvider = codecProvider;
            _contextPool = contextPool;
        }

        /// <summary>
        /// 返回一个专门为所提供的类型参数提供的复印机。
        /// </summary>
        /// <typeparam name="T">返回的复印机的基本类型 。</typeparam>
        public DeepCopier<T> GetCopier<T>() => new(_codecProvider.GetDeepCopier<T>(), _contextPool);

        /// <summary>
        /// 创建所提供值的副本。
        /// </summary>
        /// <typeparam name="T">要复制的价值类型。</typeparam>
        /// <param name="value">要复制的值。</param>
        /// <returns>提供价值的副本。</returns>
        [return: NotNullIfNotNull(nameof(value))]
        public T? Copy<T>(T? value)
        {
            using var context = _contextPool.GetContext();
            return context.DeepCopy(value);
        }
    }

    /// <summary>
    /// 提供复制对象和价值的功能。
    /// </summary>
    public sealed class DeepCopier<T>
    {
        private readonly IDeepCopier<T> _copier;
        private readonly CopyContextPool _contextPool;

        /// <summary>
        /// 初始化 <see cref="DeepCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">复印机。</param>
        /// <param name="contextPool">上下文集合。</param>
        public DeepCopier(IDeepCopier<T> copier, CopyContextPool contextPool)
        {
            _copier = copier;
            _contextPool = contextPool;
        }

        /// <summary>
        /// 创建所提供值的副本。
        /// </summary>
        /// <param name="value">要复制的值。</param>
        /// <returns>提供价值的副本。</returns>
        [return: NotNullIfNotNull(nameof(value))]
        public T? Copy(T? value)
        {
            using var context = _contextPool.GetContext();
            return _copier.DeepCopy(value, context);
        }
    }
}
