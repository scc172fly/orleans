namespace UnitTests.GrainInterfaces
{
    public interface IActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData> GetActivityId();
    }

    /// <summary>
    /// Grain 测试界面 IAsyncEnumerable 活动追踪。
    /// </summary>
    public interface IAsyncEnumerableActivityGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 将多个 ActivityData 项目作为可量化的同步项获取。
        /// 每个项目在产出时捕捉当前活动的背景。
        /// </summary>
        /// <param name="count">生产项目数量。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>项的相加数 ActivityData 项。</returns>
        IAsyncEnumerable<ActivityData> GetActivityDataStream(int count, CancellationToken cancellationToken = default);
    }

    [GenerateSerializer]
    public class ActivityData
    {
        [Id(0)]
        public string? Id { get; set; }

        [Id(1)]
        public string? TraceState { get; set; }

        [Id(2)]
        public List<KeyValuePair<string, string>>? Baggage { get; set; }
    }
}
