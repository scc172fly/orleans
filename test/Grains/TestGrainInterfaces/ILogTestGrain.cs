namespace UnitTests.GrainInterfaces
{
    /// <summary>
    /// 用于测试逻辑一致性提供者的 grain 。
    /// 班级的内容是武断的,乱七八糟的;
    /// (不要用这个来介绍如何使用 JournaledGrain)
    /// 是从 SimpleGrain开始的, 但随着时间推移,很多东西被添加 
    /// </summary>
    public interface ILogTestGrain : IGrainWithIntegerKey
    {
        // 改为A

        Task<int> GetAGlobal();

        Task<int> GetALocal();

        // 改为

        Task<AB> GetBothGlobal();

        Task<AB> GetBothLocal();

        // 保留

        Task<int[]> GetReservationsGlobal();

        // 版本版本

        Task<int> GetConfirmedVersion();

        // 设置值或加量 A

        Task SetAGlobal(int a);

        Task<Tuple<int, bool>> SetAConditional(int a);

        Task SetALocal(int a);

        Task IncrementALocal();

        Task IncrementAGlobal();

        // B 套B

        Task SetBGlobal(int b);

        Task SetBLocal(int b);

        // 保留

        Task AddReservationLocal(int x);

        Task RemoveReservationLocal(int x);


        Task<KeyValuePair<int, object>> Read();
        Task<bool> Update(IReadOnlyList<object> updates, int expectedversion);

        Task<IReadOnlyList<object>> GetEventLog();


        // 其他业务的业务

        Task SynchronizeGlobalState();
        Task Clear();
        Task Deactivate();
    }

    /// <summary>
    /// 单位测试使用。 
    /// 田地没有真正意义 
    /// 支架的点只是 grain 方法可以同时返回 A和B。
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
