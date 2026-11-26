using Orleans.Core;

namespace Orleans.Runtime
{
    /// <summary>
    /// 提供访问 grain 状态的功能,以保存、清扫和刷新状态。
    /// </summary>
    /// <typeparam name="TState">基本状态类型。</typeparam>
    /// <seealso cref="Orleans.Core.IStorage{TState}" />
    public interface IPersistentState<TState> : IStorage<TState>
    {
    }
}
