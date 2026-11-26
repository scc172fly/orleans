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

            // 以原子方式发布所有事件
            RaiseEvents(events);
            await ConfirmEvents();
        }

        public Task ChangeLastName(string lastName)
        {
            RaiseEvent(new PersonLastNameChanged(lastName));

            // 我们在这里无法证实这件事!
            // 因此,暂定状态和确认状态可能会在一段时间内有所不同。

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

        // 下面是单位测试; 理想情况下, 这个代码会出现在测试项目中,
        // 但本次测试必须运行在 grain上,而不是客户端, 所以我们不得不在此添加

        private static void AssertEqual<T>(T a, T b)
        {
            if (!Object.Equals(a, b))
                throw new OrleansException($"Test failed. Expected = {a}. Actual = {b}.");
        }


        public async Task RunTentativeConfirmedStateTest()
        {
            // 最初确认的版本和暂定版本相同:第0版
            AssertEqual(0, Version);
            AssertEqual(0, TentativeVersion);
            AssertEqual(null, State.LastName);
            AssertEqual(null, TentativeState.LastName);

            // 现在改姓
            await ChangeLastName("Organa");

            // 乌版尚未确定,确认的版本和暂定版本不同
            AssertEqual(0, Version);
            AssertEqual(1, TentativeVersion);
            AssertEqual(null, State.LastName);
            AssertEqual("Organa", TentativeState.LastName);

            // 等更新结果确认后再谈
            await ConfirmChanges();

            // 现在这两个版本又是一样的了
            AssertEqual(1, Version);
            AssertEqual(1, TentativeVersion);
            AssertEqual("Organa", State.LastName);
            AssertEqual("Organa", TentativeState.LastName);

            // 发布另一个更改
            await ChangeLastName("Solo");

            // ,确认的和暂定的版本不同
            AssertEqual(1, Version);
            AssertEqual(2, TentativeVersion);
            AssertEqual("Organa", State.LastName);
            AssertEqual("Solo", TentativeState.LastName);

            // 此时此刻,我们等待(应当等待)足够时间来承诺 MemoryStorage。
            // 我们绝不会在实际代码中使用这种时间假设。但这是单位测试。
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(20);
                if (Version == 2) break;
            }

            // 现在两个版本应该又一样了
            AssertEqual(2, Version);
            AssertEqual(2, TentativeVersion);
            AssertEqual("Solo", State.LastName);
            AssertEqual("Solo", TentativeState.LastName);
        }
    }
}
