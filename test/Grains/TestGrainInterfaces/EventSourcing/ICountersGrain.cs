namespace TestGrainInterfaces
{

    /// <summary>
    /// 维护多个计数器的 Grain，这些计数器以字符串键索引
    /// </summary>
    public interface ICountersGrain : Orleans.IGrainWithIntegerKey
    {
        /// <summary> 将指定键对应的计数器按指定增量更新 </summary>
        Task Add(string key, int amount, bool wait_for_confirmation);

        /// <summary> 将所有计数器重置为零 </summary>
        Task Reset(bool wait_for_confirmation);

        /// <summary> 获取指定键对应计数器的暂定值 </summary>
        Task<int> GetTentativeCount(string key);

        /// <summary> 获取所有计数器的暂定值 </summary>
        Task<IReadOnlyDictionary<string, int>> GetTentativeState();

        /// <summary> 获取所有计数器的已确认值 </summary>
        Task<IReadOnlyDictionary<string, int>> GetConfirmedState();

        /// <summary> 确认所有事件 </summary>
        Task ConfirmAllPreviouslyRaisedEvents();

    }
}
