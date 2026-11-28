namespace UnitTests.GrainInterfaces
{
    public interface IActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData> GetActivityId();
    }

    /// <summary>
    /// 用于测试 IAsyncEnumerable 活动跟踪的 Grain 接口。
    /// </summary>
    public interface IAsyncEnumerableActivityGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 以异步可枚举序列的形式获取多个 ActivityData 项。
        /// 每一项均捕获 yield 返回时的当前 Activity 上下文。
        /// </summary>
        /// <param name="count">要返回的项数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>ActivityData 项的异步可枚举序列。</returns>
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
