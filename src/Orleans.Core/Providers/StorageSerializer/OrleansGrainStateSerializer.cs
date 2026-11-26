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
    /// Grain 使用 Orleans <see cref="Serializer"/>.
    /// </summary>
    public class OrleansGrainStorageSerializer : IGrainStorageStreamingSerializer
    {
        private readonly Serializer serializer;

        /// <summary>
        /// 初始化 <see cref="OrleansGrainStorageSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="serializer">序列器。</param>
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

            // 可查找流(e.g.、 MemoryStream、 FileStream)可直接在不缓冲的情况下进行消毒。
            // 不可寻找的流(e.g., NetworkStream)需要缓冲,以便能够高效率地进行多路读取。
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
