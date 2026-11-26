using TestGrainInterfaces;
using Xunit;
using Assert = Xunit.Assert;
using Orleans.Runtime;

namespace Tester.EventSourcingTests
{
    /// <summary>
    /// 事件源计数器测试 grain 功能包括同时加薪和国家管理。
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

            // 所有三个更新应在暂定清点中显示(即使尚未确认)
            Assert.Equal(3, await grain.GetTentativeCount("Alice"));

            // 将全部对数重置为零,等待确认
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
            // 随机键同时
            var tasks = new List<Task>();
            for (int i = 0; i < count; i++)
                tasks.Add(grain.Add(RandomKey(), 1, wait_for_confirmation_on_each));
            await Task.WhenAll(tasks);

            // 检查是否临时状态显示所有加量
            Assert.Equal(count, (await grain.GetTentativeState()).Aggregate(0, (c, kvp) => c + kvp.Value));

            // 如果我们不等待每一事件的确认,那末,你们等待吧!
            if (!wait_for_confirmation_on_each)
                await grain.ConfirmAllPreviouslyRaisedEvents();

            // 检查是否确认状态显示所有加量
            Assert.Equal(count, (await grain.GetConfirmedState()).Aggregate(0, (c, kvp) => c + kvp.Value));

            // 重置所有计数器
            await grain.Reset(true);
        }
    }
}
