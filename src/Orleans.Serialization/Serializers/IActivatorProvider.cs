using Orleans.Serialization.Activators;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供激活器。
    /// </summary>
    public interface IActivatorProvider
    {
        /// <summary>
        /// 获取指定类型的激活器。
        /// </summary>
        /// <typeparam name="T">类型。</typeparam>
        /// <returns>激活器。</returns>
        IActivator<T> GetActivator<T>();
    }
}
