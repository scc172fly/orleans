using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.EventSourcing.CustomStorage
{
    /// <summary>
    /// 希望使用 CustomStorage 日志一致性提供程序的 Grain 所公开的存储接口。
    /// </summary>
    /// <typeparam name="TState">Grain 状态的类型。</typeparam>
    /// <typeparam name="TDelta">表示状态更新的增量对象的类型。</typeparam>
    public interface ICustomStorageInterface<TState, TDelta>
    {
        /// <summary>
        /// 从存储读取当前状态及版本
        /// （注意，提供程序可能修改状态对象，因此不得共享该对象）。
        /// </summary>
        /// <returns>版本号和状态对象。</returns>
        Task<KeyValuePair<int, TState>> ReadStateFromStorage();

        /// <summary>
        /// 如果存储中的版本与预期版本匹配，则将给定的增量数组应用到存储并返回 true。
        /// 否则不执行任何操作并返回 false。如果成功，存储版本必须增加相当于增量个数的值。
        /// </summary>
        /// <returns>如果已应用增量，则为 true；否则为 false。</returns>
        Task<bool> ApplyUpdatesToStorage(IReadOnlyList<TDelta> updates, int expectedVersion);

        /// <summary>
        /// 清除存储中的已保存状态。
        /// </summary>
        /// <returns>表示异步清除操作的任务。</returns>
        Task ClearStoredState() => throw new NotSupportedException();
    }
}
