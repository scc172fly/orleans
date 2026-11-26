using Orleans.Serialization.Activators;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供活动者。
    /// </summary>
    public interface IActivatorProvider
    {
        /// <summary>
        /// 获取指定类型活动的动因。
        /// </summary>
        /// <typeparam name="T">类型。</typeparam>
        /// <returns>激活器。</returns>
        IActivator<T> GetActivator<T>();
    }
}
