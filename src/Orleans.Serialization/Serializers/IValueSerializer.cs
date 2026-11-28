using Orleans.Serialization.Buffers;
using System.Buffers;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供值类型的序列化功能。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    public interface IValueSerializer<T> : IValueSerializer where T : struct
    {
        /// <summary>
        /// 序列化给定的值。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="value">值。</param>
        void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, scoped ref T value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 反序列化指定类型的值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="value">用于接收反序列化结果的值。</param>
        void Deserialize<TInput>(ref Reader<TInput> reader, scoped ref T value);
    }

    /// <summary>
    /// 值类型序列化器的标记接口。
    /// </summary>
    public interface IValueSerializer
    {
    }
}
