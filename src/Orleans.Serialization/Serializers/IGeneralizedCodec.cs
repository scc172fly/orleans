using System;
using Orleans.Serialization.Codecs;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 支持多种类型的编解码器。
    /// </summary>
    public interface IGeneralizedCodec : IFieldCodec
    {
        /// <summary>
        /// 确定此实例是否支持指定类型。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> 表示支持指定类型；否则为 <see langword="false" />。</returns>
        bool IsSupportedType(Type type);
    }

    /// <summary>
    /// 提供对多种类型的编解码器的访问。
    /// </summary>
    public interface ISpecializableCodec
    {
        /// <summary>
        /// 确定此实例是否支持指定类型。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> 表示支持指定类型；否则为 <see langword="false" />。</returns>
        bool IsSupportedType(Type type);

        /// <summary>
        /// 获取支持指定类型的 <see cref="IFieldCodec"/> 实现。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>支持指定类型的 <see cref="IFieldCodec"/> 实现。</returns>
        IFieldCodec GetSpecializedCodec(Type type);
    }
}
