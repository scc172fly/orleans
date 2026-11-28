using Orleans.Serialization.Codecs;
using System;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供对字段编解码器的访问。
    /// </summary>
    public interface IFieldCodecProvider
    {
        /// <summary>
        /// 获取指定类型的编解码器。
        /// </summary>
        /// <typeparam name="TField">字段类型。</typeparam>
        /// <returns>编解码器。</returns>
        IFieldCodec<TField> GetCodec<TField>();

        /// <summary>
        /// 获取指定类型的编解码器；如果未找到合适的编解码器，则返回 <see langword="null"/>。
        /// </summary>
        /// <typeparam name="TField">字段类型。</typeparam>
        /// <returns>编解码器。</returns>
        IFieldCodec<TField>? TryGetCodec<TField>();

        /// <summary>
        /// 获取指定类型的编解码器。
        /// </summary>
        /// <param name="fieldType">
        /// 字段类型。
        /// </param>
        /// <returns>编解码器。</returns>
        IFieldCodec GetCodec(Type fieldType);

        /// <summary>
        /// 获取指定类型的编解码器；如果未找到合适的编解码器，则返回 <see langword="null"/>。
        /// </summary>
        /// <param name="fieldType">
        /// 字段类型。
        /// </param>
        /// <returns>编解码器。</returns>
        IFieldCodec? TryGetCodec(Type fieldType);
    }
}
