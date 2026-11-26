using Orleans.EventSourcing;
using TestGrainInterfaces;

namespace TestGrains
{
    /// <summary>
    /// 以一个计算统计数据的日记 grain 实例。
    /// 
    /// 配置选项,请参见 CountersGrainVariations.cs中的衍生类别
    /// </summary>
    [GrainType("simple-counters-grain")]
    public class CountersGrain : JournaledGrain<CountersGrain.GrainState>, ICountersGrain
    {
        /// <summary>
        /// 此 grain 状态是一本字典, 记录每个密钥的数。
        /// 我们把它定义为一个嵌套型阶级, 只是为了方便范围。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public class GrainState
        {
            /// <summary> 当前计数 </summary>
            [Id(0)]
            public Dictionary<string, int> Counts { get; set; }

            public GrainState()
            {
                Counts = new Dictionary<string, int>();
            }

            public void Apply(UpdatedEvent e)
            {
                if (Counts.ContainsKey(e.Key))
                    Counts[e.Key] += e.Amount;
                else
                    Counts.Add(e.Key, e.Amount);
            }

            public void Apply(ResetAllEvent e)
            {
                Counts.Clear();
            }
        }

        /// <summary>
        /// 代表反更新的活动
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public class UpdatedEvent
        {
            [Id(0)]
            public string Key { get; set; } = null!;
            [Id(1)]
            public int Amount { get; set; }
        }

        /// <summary>
        /// 代表全部计数器重置的事件
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public class ResetAllEvent
        {
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // 激活时, 我们装入懒惰( 不要等待当前状态已装入) 。
            return Task.CompletedTask;
        }

        public async Task Add(string key, int amount, bool wait_for_confirmation)
        {
            RaiseEvent(new UpdatedEvent() { Key = key, Amount = amount });

            // 可选, 等到事件持续存储后再继续
            if (wait_for_confirmation)
                await ConfirmEvents();
        }

        public async Task Reset(bool wait_for_confirmation)
        {
            RaiseEvent(new ResetAllEvent());

            // 可选, 等到事件持续存储后再继续
            if (wait_for_confirmation)
                await ConfirmEvents();
        }

        public Task ConfirmAllPreviouslyRaisedEvents()
        {
            return ConfirmEvents();
        }


        public Task<int> GetTentativeCount(string key)
        {
            return Task.FromResult(TentativeState.Counts[key]);
        }

        public Task<IReadOnlyDictionary<string, int>> GetTentativeState()
        {
            return Task.FromResult((IReadOnlyDictionary<string, int>)TentativeState.Counts);
        }

        public Task<IReadOnlyDictionary<string, int>> GetConfirmedState()
        {
            return Task.FromResult((IReadOnlyDictionary<string, int>)State.Counts);
        }


        // 有些提供者允许您查看事件日志
        public Task<IReadOnlyList<object>> GetAllEvents()
        {
            return RetrieveConfirmedEvents(0, Version);
        }
    }
}
