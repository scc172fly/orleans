namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供对值类型序列化器的访问。
    /// </summary>
    public interface IValueSerializerProvider
    {
        /// <summary>
        /// 获取指定类型的值序列化器。
        /// </summary>
        /// <typeparam name="TField">值类型。</typeparam>
        /// <returns>指定类型的值序列化器。</returns>
        IValueSerializer<TField> GetValueSerializer<TField>() where TField : struct;
    }
}
