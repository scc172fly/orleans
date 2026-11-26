using Orleans.EventSourcing;
using UnitTests.GrainInterfaces;

namespace TestGrains
{
    /// <summary>
    /// 各种日志一致性提供者的许多不同单位测试所使用的一个类别。
    /// 班级的内容是武断的,乱七八糟的;
    /// (不要用这个来介绍如何使用 JournaledGrain)
    /// 是从 SimpleGrain开始的, 但随着时间推移,很多东西被添加 
    /// </summary>
    [Serializable]
    [Orleans.GenerateSerializer]
    public class MyGrainState
    {
        [Orleans.Id(0)]
        public int A;
        [Orleans.Id(1)]
        public int B;
        [Orleans.Id(2)]
        public Dictionary<string, int> Reservations;

        public MyGrainState()
        {
            Reservations = new Dictionary<string, int>();
        }

        public override string ToString()
        {
            return string.Format("A={0} B={1} R={{{2}}}", A, B, string.Join(", ", Reservations.Select(kvp => string.Format("{0}:{1}", kvp.Key, kvp.Value))));
        }

        // 全部更新操作在此列出
        public void Apply(UpdateA x) { A = x.Val; }
        public void Apply(UpdateB x) { B = x.Val; }
        public void Apply(IncrementA x) { A++; }

        public void Apply(AddReservation x) { Reservations[x.Val.ToString()] = x.Val; }
        public void Apply(RemoveReservation x) { Reservations.Remove(x.Val.ToString()); }
    }


    [Serializable]
    [Orleans.GenerateSerializer]
    public class UpdateA { [Orleans.Id(0)] public int Val; }
    [Serializable]
    [Orleans.GenerateSerializer]
    public class UpdateB { [Orleans.Id(0)] public int Val; }
    [Serializable]
    [Orleans.GenerateSerializer]
    public class IncrementA { [Orleans.Id(0)] public int Val; }
    [Serializable]
    [Orleans.GenerateSerializer]
    public class AddReservation { [Orleans.Id(0)] public int Val; }
    [Serializable]
    [Orleans.GenerateSerializer]
    public class RemoveReservation { [Orleans.Id(0)] public int Val; }



    /// <summary>
    /// 用于测试逻辑一致性提供者的 grain 。
    /// 有两个领域A, B, 可更新或加增;
    /// 和保留词典,可以添加和删除
    /// 我们对此进行分类, 为所有存储提供者创建变量
    /// </summary>
    public abstract class LogTestGrain : JournaledGrain<MyGrainState, object>, UnitTests.GrainInterfaces.ILogTestGrain
    {

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask; // 不等待初始装入
        }

        public async Task SetAGlobal(int x)
        {
            RaiseEvent(new UpdateA() { Val = x });
            await ConfirmEvents();
        }

        public async Task<Tuple<int, bool>> SetAConditional(int x)
        {
            int version = this.Version;
            bool success = await RaiseConditionalEvent(new UpdateA() { Val = x });
            return new Tuple<int, bool>(version, success);
        }

        public Task SetALocal(int x)
        {
            RaiseEvent(new UpdateA() { Val = x });
            return Task.CompletedTask;
        }
        public async Task SetBGlobal(int x)
        {
            RaiseEvent(new UpdateB() { Val = x });
            await ConfirmEvents();
        }

        public Task SetBLocal(int x)
        {
            RaiseEvent(new UpdateB() { Val = x });
            return Task.CompletedTask;
        }

        public async Task IncrementAGlobal()
        {
            RaiseEvent(new IncrementA());
            await ConfirmEvents();
        }

        public Task IncrementALocal()
        {
            RaiseEvent(new IncrementA());
            return Task.CompletedTask;

        }

        public async Task<int> GetAGlobal()
        {
            await RefreshNow();
            return State.A;
        }

        public Task<int> GetALocal()
        {
            return Task.FromResult(TentativeState.A);
        }

        public async Task<AB> GetBothGlobal()
        {
            await RefreshNow();
            return new AB() { A = State.A, B = State.B };
        }

        public Task<AB> GetBothLocal()
        {
            return Task.FromResult(new AB() { A = TentativeState.A, B = TentativeState.B });
        }

        public Task AddReservationLocal(int val)
        {
            RaiseEvent(new AddReservation() { Val = val });
            return Task.CompletedTask;

        }
        public Task RemoveReservationLocal(int val)
        {
            RaiseEvent(new RemoveReservation() { Val = val });
            return Task.CompletedTask;

        }
        public async Task<int[]> GetReservationsGlobal()
        {
            await RefreshNow();
            return State.Reservations.Values.ToArray();
        }

        public Task SynchronizeGlobalState()
        {
            return RefreshNow();
        }

        public Task<int> GetConfirmedVersion()
        {
            return Task.FromResult(this.Version);
        }

        public async Task<KeyValuePair<int, object>> Read()
        {
            await RefreshNow();
            return new KeyValuePair<int, object>(Version, State);
        }
        public async Task<bool> Update(IReadOnlyList<object> updates, int expectedversion)
        {
            if (expectedversion > Version)
                await RefreshNow();
            if (expectedversion != Version)
                return false;
            return await RaiseConditionalEvents(updates);
        }

        public Task Deactivate()
        {
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        public Task Clear()
        {
            return ClearLogAsync();
        }

        public Task<IReadOnlyList<object>> GetEventLog()
        {
            return this.RetrieveConfirmedEvents(0, Version);
        }

    }
}
