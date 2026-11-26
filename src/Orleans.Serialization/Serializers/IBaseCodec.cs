using Orleans.Serialization.Buffers;
using System;
using System.Buffers;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 功能性,在类型等级中按类别划分成员序列和取消成员顺序。
    /// </summary>
    /// <typeparam name="T">此编码器所支持的类型。</typeparam>
    public interface IBaseCodec<in T> : IBaseCodec where T : class
    {
        /// <summary>
        /// 将提供的值分级。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="value">数值。</param>
        void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, T value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 向提供的值递减。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="value">数值。</param>
        void Deserialize<TInput>(ref Reader<TInput> reader, T value);
    }

    /// <summary>
    /// 基本序列器的标记界面。
    /// </summary>
    public interface IBaseCodec
    {
    }

    /// <summary>
    /// 支持多种类型的基础型序列器。
    /// </summary>
    public interface IGeneralizedBaseCodec : IBaseCodec<object>
    {
        /// <summary>
        /// 确定指定类型是否得到此实例的支持。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> ,如果支持了指定类型;否则, <see langword="false" />。</returns>
        bool IsSupportedType(Type type);
    }

    /// <summary>
    /// 提供功能,用于创建支持特定类型的 <see cref="IBaseCodec"/> 实例。
    /// </summary>
    public interface ISpecializableBaseCodec
    {
        /// <summary>
        /// 确定指定类型是否得到此实例的支持。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> ,如果支持了指定类型;否则, <see langword="false" />。</returns>
        bool IsSupportedType(Type type);

        /// <summary>
        /// 获得支持指定类型的 <see cref="IBaseCodec"/> 执行。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>A 音 A <see cref="IBaseCodec"/> 支持指定类型的执行。</returns>
        IBaseCodec GetSpecializedCodec(Type type);
    }
}
