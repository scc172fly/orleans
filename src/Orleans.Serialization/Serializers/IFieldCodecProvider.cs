using Orleans.Serialization.Codecs;
using System;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供外地编码。
    /// </summary>
    public interface IFieldCodecProvider
    {
        /// <summary>
        /// 获取指定类型代码的编码。
        /// </summary>
        /// <typeparam name="TField">字段类型。</typeparam>
        /// <returns>代码码</returns>
        IFieldCodec<TField> GetCodec<TField>();

        /// <summary>
        /// 获取特定类型的编码器,或者找不到合适的编码器的 <see langword="null"/> 。
        /// </summary>
        /// <typeparam name="TField">字段类型。</typeparam>
        /// <returns>代码码</returns>
        IFieldCodec<TField>? TryGetCodec<TField>();

        /// <summary>
        /// 获得特定类型代码的编码器 。
        /// </summary>
        /// <param name="fieldType">
        /// 字段类型。
        /// </param>
        /// <returns>代码码</returns>
        IFieldCodec GetCodec(Type fieldType);

        /// <summary>
        /// 获取特定类型的编码器,或者找不到合适的编码器的 <see langword="null"/> 。
        /// </summary>
        /// <param name="fieldType">
        /// 字段类型。
        /// </param>
        /// <returns>代码码</returns>
        IFieldCodec? TryGetCodec(Type fieldType);
    }
}
