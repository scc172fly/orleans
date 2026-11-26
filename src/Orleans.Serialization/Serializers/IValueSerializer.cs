using Orleans.Serialization.Buffers;
using System.Buffers;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 确定价值类型序列的功能性。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    public interface IValueSerializer<T> : IValueSerializer where T : struct
    {
        /// <summary>
        /// 将提供的值分级。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="value">数值。</param>
        void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, scoped ref T value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 使指定类型降序。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="value">数值。</param>
        void Deserialize<TInput>(ref Reader<TInput> reader, scoped ref T value);
    }

    /// <summary>
    /// 值类型序列器的标记界面。
    /// </summary>
    public interface IValueSerializer
    {
    }
}
