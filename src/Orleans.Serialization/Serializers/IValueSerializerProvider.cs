namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供使用价值类型序列器的接入。
    /// </summary>
    public interface IValueSerializerProvider
    {
        /// <summary>
        /// 获取指定类型中的序列器值。
        /// </summary>
        /// <typeparam name="TField">值类型。</typeparam>
        /// <returns>指定类型中的值序列器。</returns>
        IValueSerializer<TField> GetValueSerializer<TField>() where TField : struct;
    }
}
