using Orleans.Serialization.Buffers;
using System;
using System.Buffers;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供类型层次结构中成员的序列化和反序列化功能。
    /// </summary>
    /// <typeparam name="T">此编解码器支持的类型。</typeparam>
    public interface IBaseCodec<in T> : IBaseCodec where T : class
    {
        /// <summary>
        /// 序列化给定的值。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="value">值。</param>
        void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, T value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 将数据反序列化到给定的值中。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="value">要将数据反序列化到其中的值。</param>
        void Deserialize<TInput>(ref Reader<TInput> reader, T value);
    }

    /// <summary>
    /// 基类型序列化器的标记接口。
    /// </summary>
    public interface IBaseCodec
    {
    }

    /// <summary>
    /// 支持多种类型的基类型序列化器。
    /// </summary>
    public interface IGeneralizedBaseCodec : IBaseCodec<object>
    {
        /// <summary>
        /// 确定此实例是否支持指定类型。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> 表示支持指定类型；否则为 <see langword="false" />。</returns>
        bool IsSupportedType(Type type);
    }

    /// <summary>
    /// 提供创建支持指定类型的 <see cref="IBaseCodec"/> 实例的功能。
    /// </summary>
    public interface ISpecializableBaseCodec
    {
        /// <summary>
        /// 确定此实例是否支持指定类型。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> 表示支持指定类型；否则为 <see langword="false" />。</returns>
        bool IsSupportedType(Type type);

        /// <summary>
        /// 获取支持指定类型的 <see cref="IBaseCodec"/> 实现。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>支持指定类型的 <see cref="IBaseCodec"/> 实现。</returns>
        IBaseCodec GetSpecializedCodec(Type type);
    }
}
