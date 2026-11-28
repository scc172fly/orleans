using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using Orleans.Serialization;
using Orleans.Serialization.Buffers.Adaptors;

namespace Orleans.Storage
{
    /// <summary>
    /// 使用 Orleans <see cref="Serializer"/> 的 Grain 存储序列化器。
    /// </summary>
    public class OrleansGrainStorageSerializer : IGrainStorageStreamingSerializer
    {
        private readonly Serializer serializer;

        /// <summary>
        /// 初始化 <see cref="OrleansGrainStorageSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="serializer">序列化器。</param>
        public OrleansGrainStorageSerializer(Serializer serializer)
        {
            this.serializer = serializer;
        }

        /// <inheritdoc/>
        public BinaryData Serialize<T>(T? value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            this.serializer.Serialize(value, buffer);
            return new BinaryData(buffer.WrittenMemory);
        }

        /// <inheritdoc/>
        public T? Deserialize<T>(BinaryData input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return this.serializer.Deserialize<T>(input.ToMemory());
        }

        /// <inheritdoc/>
        public ValueTask SerializeAsync<T>(T? value, Stream destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            this.serializer.Serialize(value, destination);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public async ValueTask<T?> DeserializeAsync<T>(Stream input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            // 可定位流（例如 MemoryStream、FileStream）无需缓冲即可直接反序列化。
            // 不可定位流（例如 NetworkStream）需要缓冲，以便高效地多次读取。
            if (input.CanSeek)
            {
                return this.serializer.Deserialize<T>(input);
            }

            var bufferStream = PooledBufferStream.Rent();
            try
            {
                await input.CopyToAsync(bufferStream, cancellationToken).ConfigureAwait(false);
                var sequence = bufferStream.RentReadOnlySequence();
                try
                {
                    return this.serializer.Deserialize<T>(sequence);
                }
                finally
                {
                    bufferStream.ReturnReadOnlySequence(sequence);
                }
            }
            finally
            {
                PooledBufferStream.Return(bufferStream);
            }
        }
    }
}
