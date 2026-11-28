
namespace Orleans.Runtime
{
    /// <summary>
    /// 用于为 Grain 构造 <see cref="IPersistentState{TState}"/> 实例的工厂。
    /// </summary>
    public interface IPersistentStateFactory
    {
        /// <summary>
        /// 为提供的 Grain 创建持久化状态实例。
        /// </summary>
        /// <typeparam name="TState">底层状态类型。</typeparam>
        /// <param name="context">Grain 上下文。</param>
        /// <param name="config">状态 facet 配置。</param>
        /// <returns>具有指定配置、属于提供的 Grain 的持久化状态实例。</returns>
        IPersistentState<TState> Create<TState>(IGrainContext context, IPersistentStateConfiguration config);
    }
}
