
namespace Orleans.Serialization.Activators
{
    /// <summary>
    /// 创建对象实例的功能性。
    /// </summary>
    /// <typeparam name="T">执行此执行创建的示例类型。</typeparam>
    public interface IActivator<T>
    {
        /// <summary>
        /// 创建类型 <typeparamref name="T"/>的实例。
        /// </summary>
        /// <returns>类型 <typeparamref name="T"/>的示例。</returns>
        T Create();
    }
}
