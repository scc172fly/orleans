using Orleans.EventSourcing;
using TestGrainInterfaces;

namespace TestGrains
{
    /// <summary>
    /// 进行统计计数的日志化 Grain 示例。
    /// 
    /// 有关配置选项，请参阅 CountersGrainVariations.cs 中的派生类
    /// </summary>
    [GrainType("simple-counters-grain")]
    public class CountersGrain : JournaledGrain<CountersGrain.GrainState>, ICountersGrain
    {
        /// <summary>
        /// 此 Grain 的状态为字典，保存每个键对应的计数。
        /// 为了便于限定作用域，将其定义为嵌套类。
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
        /// 表示计数器更新的事件
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
        /// 表示重置所有计数器的事件
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public class ResetAllEvent
        {
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // 激活时采用延迟加载（不等待当前状态加载完成）。
            return Task.CompletedTask;
        }

        public async Task Add(string key, int amount, bool wait_for_confirmation)
        {
            RaiseEvent(new UpdatedEvent() { Key = key, Amount = amount });

            // 可选择等待事件持久化到存储
            if (wait_for_confirmation)
                await ConfirmEvents();
        }

        public async Task Reset(bool wait_for_confirmation)
        {
            RaiseEvent(new ResetAllEvent());

            // 可选择等待事件持久化到存储
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


        // 某些提供程序允许查看事件日志
        public Task<IReadOnlyList<object>> GetAllEvents()
        {
            return RetrieveConfirmedEvents(0, Version);
        }
    }
}
