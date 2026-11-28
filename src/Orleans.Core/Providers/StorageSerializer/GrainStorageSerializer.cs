using System;

namespace Orleans.Storage
{
    /// <summary>
    /// 提供 Grain 状态的序列化和反序列化功能，委托给首选及回退的 <see cref="IGrainStorageSerializer"/> 实现。
    /// </summary>
    public class GrainStorageSerializer : IGrainStorageSerializer
    {
        private readonly IGrainStorageSerializer _serializer;
        private readonly IGrainStorageSerializer _fallbackDeserializer;

        /// <summary>
        /// 初始化 <see cref="GrainStorageSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="serializer">Grain 存储序列化器。</param>
        /// <param name="fallbackDeserializer">回退使用的 Grain 存储序列化器。</param>
        public GrainStorageSerializer(IGrainStorageSerializer serializer, IGrainStorageSerializer fallbackDeserializer)
        {
            _serializer = serializer;
            _fallbackDeserializer = fallbackDeserializer;
        }

        /// <inheritdoc/>
        public BinaryData Serialize<T>(T? input) => _serializer.Serialize(input);

        /// <inheritdoc/>
        public T? Deserialize<T>(BinaryData input)
        {
            try
            {
                return _serializer.Deserialize<T>(input);
            }
            catch (Exception ex1)
            {
                try
                {
                    return _fallbackDeserializer.Deserialize<T>(input);
                }
                catch (Exception ex2)
                {
                    throw new AggregateException("Failed to deserialize input", ex1, ex2);
                }
            }
        }

    }
}
