using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.EventSourcing.CustomStorage
{
    /// <summary>
    /// 由 grains 想要使用 CustomStorage 源代码
    /// </summary>
    /// <typeparam name="TState">状态类型的类型 grain.</typeparam>
    /// <typeparam name="TDelta">表示更新到状态的三角形对象类型。</typeparam>
    public interface ICustomStorageInterface<TState, TDelta>
    {
        /// <summary>
        /// 读取存储时的当前状态和版本
        /// (注意国家物体可能由提供者变异,因此不得分享)。
        /// </summary>
        /// <returns>版本编号和状态对象。</returns>
        Task<KeyValuePair<int, TState>> ReadStateFromStorage();

        /// <summary>
        /// 如果存储中的版本与预期版本相符, 则将给定的三角形数组应用到存储中并返回真实值 。 
        /// 否则,什么都不做,就回错。 如果成功的话,存储版本必须增加三角洲的数量。
        /// </summary>
        /// <returns>如果使用三角洲,则真实,否则是假的。</returns>
        Task<bool> ApplyUpdatesToStorage(IReadOnlyList<TDelta> updates, int expectedVersion);

        /// <summary>
        /// 清除储存状态。
        /// </summary>
        /// <returns>代表无同步清晰操作的任务。</returns>
        Task ClearStoredState() => throw new NotSupportedException();
    }
}
