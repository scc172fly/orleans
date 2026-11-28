
namespace Orleans.Serialization.Activators
{
    /// <summary>
    /// 提供创建对象实例的功能。
    /// </summary>
    /// <typeparam name="T">此实现创建的实例类型。</typeparam>
    public interface IActivator<T>
    {
        /// <summary>
        /// 创建类型为 <typeparamref name="T"/> 的实例。
        /// </summary>
        /// <returns>类型为 <typeparamref name="T"/> 的实例。</returns>
        T Create();
    }
}
