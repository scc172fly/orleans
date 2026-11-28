using TestGrainInterfaces;
using Xunit;
using Assert = Xunit.Assert;
using Orleans.Runtime;

namespace Tester.EventSourcingTests
{
    /// <summary>
    /// 测试事件溯源计数器 Grain 的功能，包括并发递增和状态管理。
    /// </summary>
    [TestProvider("None")]
    [TestArea("EventSourcing")]
    public partial class CountersGrainTests : IClassFixture<EventSourcingClusterFixture>
    {
        private readonly EventSourcingClusterFixture fixture;

        public CountersGrainTests(EventSourcingClusterFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact, TestSuite("Functional"), TestCategory("EventSourcing"), TestCategory("Functional")]
        public async Task Record()
        {
            var grain = this.fixture.GrainFactory.GetGrain<ICountersGrain>(GrainId.Create("simple-counters-grain", "0"));

            var currentstate = await grain.GetTentativeState();
            Assert.NotNull(currentstate);
            Assert.Empty(currentstate);

            await grain.Add("Alice", 1, false);
            await grain.Add("Alice", 1, false);
            await grain.Add("Alice", 1, false);

            // 即使尚未确认，暂定计数也应反映全部三次更新
            Assert.Equal(3, await grain.GetTentativeCount("Alice"));

            // 将所有计数器重置为零，并等待确认
            await grain.Reset(true);

            Assert.Empty((await grain.GetTentativeState()));
        }

        [Fact, TestSuite("Functional"), TestCategory("EventSourcing"), TestCategory("Functional")]
        public async Task ConcurrentIncrements()
        {
            var grain = this.fixture.GrainFactory.GetGrain<ICountersGrain>(GrainId.Create("simple-counters-grain", "0"));
            await ConcurrentIncrementsRunner(grain, 50, false);
        }

        private static readonly string[] keys = { "a", "b", "c", "d", "e", "f", "g", "h" };
        private static string RandomKey() { return keys[Random.Shared.Next(keys.Length)]; }


        private static async Task ConcurrentIncrementsRunner(ICountersGrain grain, int count, bool wait_for_confirmation_on_each)
        {
            // 在随机键上并发执行 count 次递增
            var tasks = new List<Task>();
            for (int i = 0; i < count; i++)
                tasks.Add(grain.Add(RandomKey(), 1, wait_for_confirmation_on_each));
            await Task.WhenAll(tasks);

            // 检查暂定状态是否反映全部递增
            Assert.Equal(count, (await grain.GetTentativeState()).Aggregate(0, (c, kvp) => c + kvp.Value));

            // 如果此前没有等待每个事件的确认，现在等待确认
            if (!wait_for_confirmation_on_each)
                await grain.ConfirmAllPreviouslyRaisedEvents();

            // 检查已确认状态是否反映全部递增
            Assert.Equal(count, (await grain.GetConfirmedState()).Aggregate(0, (c, kvp) => c + kvp.Value));

            // 重置所有计数器
            await grain.Reset(true);
        }
    }
}
