namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 用于测试日志一致性提供程序的 Grain。
    /// 此类的内容比较随意且杂乱；
    /// （请勿将此类用作 JournaledGrain 的入门示例）
    /// 它最初基于 SimpleGrain，后来陆续增加了许多内容
    /// </summary>
    public interface ILogTestGrain : IGrainWithIntegerKey
    {
        // 读取 A

        Task<int> GetAGlobal();

        Task<int> GetALocal();

        // 同时读取两者

        Task<AB> GetBothGlobal();

        Task<AB> GetBothLocal();

        // 预订

        Task<int[]> GetReservationsGlobal();

        // 版本

        Task<int> GetConfirmedVersion();

        // 设置或递增 A

        Task SetAGlobal(int a);

        Task<Tuple<int, bool>> SetAConditional(int a);

        Task SetALocal(int a);

        Task IncrementALocal();

        Task IncrementAGlobal();

        // 设置 B

        Task SetBGlobal(int b);

        Task SetBLocal(int b);

        // 预订

        Task AddReservationLocal(int x);

        Task RemoveReservationLocal(int x);


        Task<KeyValuePair<int, object>> Read();
        Task<bool> Update(IReadOnlyList<object> updates, int expectedversion);

        Task<IReadOnlyList<object>> GetEventLog();


        // 其他操作

        Task SynchronizeGlobalState();
        Task Clear();
        Task Deactivate();
    }

    /// <summary>
    /// 供单元测试使用。
    /// 这些字段本身没有特殊含义。
    /// 此结构体只是为了使 Grain 方法能够同时返回 A 和 B。
    /// </summary>
    [GenerateSerializer]
    public struct AB
    {
        [Id(0)]
        public int A;

        [Id(1)]
        public int B;
    }
}
