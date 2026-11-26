using System;
using Orleans.Serialization.Codecs;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 支持多种类型代码化的编码器。
    /// </summary>
    public interface IGeneralizedCodec : IFieldCodec
    {
        /// <summary>
        /// 确定指定类型是否得到此实例的支持。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> ,如果支持了指定类型;否则, <see langword="false" />。</returns>
        bool IsSupportedType(Type type);
    }

    /// <summary>
    /// 提供多种类型代码的存取。
    /// </summary>
    public interface ISpecializableCodec
    {
        /// <summary>
        /// 确定指定类型是否得到此实例的支持。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> ,如果支持了指定类型;否则, <see langword="false" />。</returns>
        bool IsSupportedType(Type type);

        /// <summary>
        /// 获得支持指定类型的 <see cref="IFieldCodec"/> 执行。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns>A 音 A <see cref="IFieldCodec"/> 支持指定类型的执行。</returns>
        IFieldCodec GetSpecializedCodec(Type type);
    }
}
