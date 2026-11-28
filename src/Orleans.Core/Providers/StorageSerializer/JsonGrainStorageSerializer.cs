using Orleans.Serialization;

namespace Orleans.Storage
{
    /// <summary>
    /// 使用 Newtonsoft.Json 的 Grain 存储序列化器。
    /// </summary>
    public class JsonGrainStorageSerializer : IGrainStorageStreamingSerializer
    {
        private readonly OrleansJsonSerializer _orleansJsonSerializer;

        /// <summary>
        /// 初始化 <see cref="JsonGrainStorageSerializer"/> 类的新实例。
        /// </summary>
        public JsonGrainStorageSerializer(OrleansJsonSerializer orleansJsonSerializer)
        {
            _orleansJsonSerializer = orleansJsonSerializer;
        }

        /// <inheritdoc/>
        public BinaryData Serialize<T>(T? value)
        {
            var data = _orleansJsonSerializer.Serialize(value, typeof(T));
            return new BinaryData(data);
        }

        /// <inheritdoc/>
        public T? Deserialize<T>(BinaryData input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return (T?)_orleansJsonSerializer.Deserialize(typeof(T), input.ToString());
        }

        /// <inheritdoc/>
        public ValueTask SerializeAsync<T>(T? value, Stream destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(destination);
            _orleansJsonSerializer.Serialize(value, typeof(T), destination);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask<T?> DeserializeAsync<T>(Stream input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(input);
            return ValueTask.FromResult((T?)_orleansJsonSerializer.Deserialize(typeof(T), input));
        }
    }
}
