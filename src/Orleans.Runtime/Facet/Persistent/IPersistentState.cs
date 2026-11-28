using Orleans.Core;

namespace Orleans.Runtime
{
    /// <summary>
    /// 提供对 Grain 状态的访问，以及保存、清除和刷新状态的功能。
    /// </summary>
    /// <typeparam name="TState">底层状态类型。</typeparam>
    /// <seealso cref="Orleans.Core.IStorage{TState}" />
    public interface IPersistentState<TState> : IStorage<TState>
    {
    }
}
