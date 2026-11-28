using TestGrainInterfaces;
using Xunit;
using Assert = Xunit.Assert;

namespace Tester.EventSourcingTests
{
    /// <summary>
    /// 测试基于日志的人员 Grain 功能，包括出生登记、结婚事件及状态转换。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("EventSourcing")]
    public class PersonGrainTests : IClassFixture<EventSourcingClusterFixture>
    {
        private readonly EventSourcingClusterFixture fixture;

        public PersonGrainTests(EventSourcingClusterFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact, TestCategory("EventSourcing"), TestCategory("Functional")]
        public async Task JournaledGrainTests_Activate()
        {
            var grainWithState = this.fixture.GrainFactory.GetGrain<IPersonGrain>(Guid.Empty);

            Assert.NotNull(await grainWithState.GetTentativePersonalAttributes());
        }

        [Fact, TestCategory("EventSourcing"), TestCategory("Functional")]
        public async Task JournaledGrainTests_Persist()
        {
            var grainWithState = this.fixture.GrainFactory.GetGrain<IPersonGrain>(Guid.Empty);

            await grainWithState.RegisterBirth(new PersonAttributes { FirstName = "Luke", LastName = "Skywalker", Gender = GenderType.Male });

            var attributes = await grainWithState.GetTentativePersonalAttributes();

            Assert.NotNull(attributes);
            Assert.Equal("Luke", attributes.FirstName);
        }

        [Fact, TestCategory("EventSourcing"), TestCategory("Functional")]
        public async Task JournaledGrainTests_AppendMoreEvents()
        {
            var leia = this.fixture.GrainFactory.GetGrain<IPersonGrain>(Guid.NewGuid());
            await leia.RegisterBirth(new PersonAttributes { FirstName = "Leia", LastName = "Organa", Gender = GenderType.Female });

            var han = this.fixture.GrainFactory.GetGrain<IPersonGrain>(Guid.NewGuid());
            await han.RegisterBirth(new PersonAttributes { FirstName = "Han", LastName = "Solo", Gender = GenderType.Male });

            await leia.Marry(han);

            var attributes = await leia.GetTentativePersonalAttributes();
            Assert.NotNull(attributes);
            Assert.Equal("Leia", attributes.FirstName);
            Assert.Equal("Solo", attributes.LastName);
        }

        [Fact, TestCategory("EventSourcing"), TestCategory("Functional")]
        public async Task JournaledGrainTests_TentativeConfirmedState()
        {
            var leia = this.fixture.GrainFactory.GetGrain<IPersonGrain>(Guid.NewGuid());

            // 整个测试必须在 Grain 内运行，否则
            // 各个步骤之间的交错执行将是不确定的。
            await leia.RunTentativeConfirmedStateTest();
        }
    }
}
