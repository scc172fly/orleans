using System;

namespace Orleans.Storage
{
    /// <summary>
    /// 提供功能,用于序列化和取消 grain 状态,授权优先和后备地实施 <see cref="IGrainStorageSerializer"/>。
    /// </summary>
    public class GrainStorageSerializer : IGrainStorageSerializer
    {
        private readonly IGrainStorageSerializer _serializer;
        private readonly IGrainStorageSerializer _fallbackDeserializer;

        /// <summary>
        /// 初始化 <see cref="GrainStorageSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="serializer">下 grain 存储序列器。</param>
        /// <param name="fallbackDeserializer">存储序列器的后退 grain 存储序列器。</param>
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
