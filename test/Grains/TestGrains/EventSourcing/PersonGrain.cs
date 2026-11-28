using Orleans.EventSourcing;
using TestGrainInterfaces;
using Orleans.Runtime;

namespace TestGrains
{
    public class PersonGrain : JournaledGrain<PersonState, IPersonEvent>, IPersonGrain
    {

        public Task RegisterBirth(PersonAttributes props)
        {
            if (this.State.FirstName == null)
            {
                RaiseEvent(new PersonRegistered(props.FirstName, props.LastName, props.Gender));

                return ConfirmEvents();
            }

            return Task.CompletedTask;
        }

        public async Task Marry(IPersonGrain spouse)
        {
            if (State.IsMarried)
                throw new NotSupportedException(string.Format("{0} is already married.", State.LastName));

            var spouseData = await spouse.GetTentativePersonalAttributes();

            var events = new List<IPersonEvent>();

            events.Add(new PersonMarried(spouse.GetPrimaryKey(), spouseData.FirstName, spouseData.LastName));

            if (State.LastName != spouseData.LastName)
            {
                events.Add(new PersonLastNameChanged(spouseData.LastName));
            }

            // 以原子方式发出所有事件
            RaiseEvents(events);
            await ConfirmEvents();
        }

        public Task ChangeLastName(string lastName)
        {
            RaiseEvent(new PersonLastNameChanged(lastName));

            // 这里不等待该事件得到确认！
            // 因此暂定状态与已确认状态可能在一段时间内不同

            return Task.CompletedTask;
        }

        public Task ConfirmChanges()
        {
            return ConfirmEvents();
        }

        public Task<PersonAttributes> GetTentativePersonalAttributes()
        {
            return Task.FromResult(new PersonAttributes
            {
                FirstName = TentativeState.FirstName,
                LastName = TentativeState.LastName,
                Gender = TentativeState.Gender
            });
        }

        public Task<PersonAttributes> GetConfirmedPersonalAttributes()
        {
            return Task.FromResult(new PersonAttributes
            {
                FirstName = State.FirstName,
                LastName = State.LastName,
                Gender = State.Gender
            });
        }

        public Task<int> GetConfirmedVersion()
        {
            return Task.FromResult(Version);
        }

        public Task<int> GetTentativeVersion()
        {
            return Task.FromResult(TentativeVersion);
        }

        private int TentativeVersion
        {
            get
            {
                return Version + UnconfirmedEvents.Count();
            }
        }

        // 以下为单元测试；理想情况下，这段代码应位于 Tester 项目中，
        // 但此测试必须在 Grain 而非客户端上运行，因此不得不将其加入此处

        private static void AssertEqual<T>(T a, T b)
        {
            if (!Object.Equals(a, b))
                throw new OrleansException($"Test failed. Expected = {a}. Actual = {b}.");
        }


        public async Task RunTentativeConfirmedStateTest()
        {
            // 最初，已确认版本与暂定版本相同：版本 0
            AssertEqual(0, Version);
            AssertEqual(0, TentativeVersion);
            AssertEqual(null, State.LastName);
            AssertEqual(null, TentativeState.LastName);

            // 现在更改姓氏
            await ChangeLastName("Organa");

            // 更新尚待处理期间，已确认版本与暂定版本不同
            AssertEqual(0, Version);
            AssertEqual(1, TentativeVersion);
            AssertEqual(null, State.LastName);
            AssertEqual("Organa", TentativeState.LastName);

            // 等待更新得到确认。
            await ConfirmChanges();

            // 现在两个版本再次相同
            AssertEqual(1, Version);
            AssertEqual(1, TentativeVersion);
            AssertEqual("Organa", State.LastName);
            AssertEqual("Organa", TentativeState.LastName);

            // 发出另一个变更
            await ChangeLastName("Solo");

            // 已确认版本与暂定版本再次出现差异
            AssertEqual(1, Version);
            AssertEqual(2, TentativeVersion);
            AssertEqual("Organa", State.LastName);
            AssertEqual("Solo", TentativeState.LastName);

            // 这次等待（预计）足够的时间，让更新提交到 MemoryStorage。
            // 在实际代码中绝不会采用这种时间假设，但这里是单元测试。
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(20);
                if (Version == 2) break;
            }

            // 现在两个版本应该再次相同
            AssertEqual(2, Version);
            AssertEqual(2, TentativeVersion);
            AssertEqual("Solo", State.LastName);
            AssertEqual("Solo", TentativeState.LastName);
        }
    }
}
