namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供对 <see cref="IBaseCodec{T}"/> 实现的访问。
    /// </summary>
    public interface IBaseCodecProvider
    {
        /// <summary>
        /// 获取指定类型的基类型编解码器。
        /// </summary>
        /// <typeparam name="TField">底层字段类型。</typeparam>
        /// <returns>基类型编解码器。</returns>
        IBaseCodec<TField> GetBaseCodec<TField>() where TField : class;
    }
}
