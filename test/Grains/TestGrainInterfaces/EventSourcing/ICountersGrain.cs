namespace TestGrainInterfaces
{

    /// <summary>
    /// 维护若干计数器的 grain ,用字符串键索引
    /// </summary>
    public interface ICountersGrain : Orleans.IGrainWithIntegerKey
    {
        /// <summary> 更新给定数额 </summary>给定金额中给定键的柜台
        Task Add(string key, int amount, bool wait_for_confirmation);

        /// <summary> 重置所有对数为零 </summary>
        Task Reset(bool wait_for_confirmation);

        /// <summary> 获取给定密钥的计数器临时值 </summary>
        Task<int> GetTentativeCount(string key);

        /// <summary> 检索所有计数器的暂定值 </summary>
        Task<IReadOnlyDictionary<string, int>> GetTentativeState();

        /// <summary> 检索所有计数器的确认值 </summary>
        Task<IReadOnlyDictionary<string, int>> GetConfirmedState();

        /// <summary> 确认所有事件 </summary>
        Task ConfirmAllPreviouslyRaisedEvents();

    }
}
