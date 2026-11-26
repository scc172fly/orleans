
namespace Orleans.Runtime
{
    /// <summary>
    /// 用于建造 <see cref="IPersistentState{TState}"/> 实例的工厂 grain。
    /// </summary>
    public interface IPersistentStateFactory
    {
        /// <summary>
        /// 为所提供的 grain创建一个持久性状态实例。
        /// </summary>
        /// <typeparam name="TState">基本状态类型。</typeparam>
        /// <param name="context"> grain 上下文。</param>
        /// <param name="config">状态元件配置。</param>
        /// <returns>提供这种服务的国家案例持续州级 grain 与指定配置相配合。</returns>
        IPersistentState<TState> Create<TState>(IGrainContext context, IPersistentStateConfiguration config);
    }
}
