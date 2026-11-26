namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供 <see cref="IBaseCodec{T}"/> 执行的机会。
    /// </summary>
    public interface IBaseCodecProvider
    {
        /// <summary>
        /// 获取指定类型的基本代码。
        /// </summary>
        /// <typeparam name="TField">基本字段类型。</typeparam>
        /// <returns>基本编码器。</returns>
        IBaseCodec<TField> GetBaseCodec<TField>() where TField : class;
    }
}
