using Grains;
using Orleans.Runtime;
using VotingContract;

namespace VotingData;

public class PollGrain : Grain, IPollGrain
{
    private readonly IPersistentState<PollState> _votes;

    public PollGrain(
        [PersistentState(stateName: "pollState", storageName: "votes")]
        IPersistentState<PollState> state) => _votes = state;

    public Task<PollState> GetCurrentResults() => Task.FromResult(_votes.State);

    public async Task CreatePoll(PollState initialState)
    {
        // 立国并坚持下去
        _votes.State = initialState;
        await _votes.WriteStateAsync();
    }

    public async Task<PollState> AddVote(int optionId)
    {
        // 进行输入验证
        var options = _votes.State.Options;
        if (optionId < 0 || optionId >= options.Count)
        {
            throw new KeyNotFoundException($"Invalid option {optionId}");
        }

        // 添加投票并维持更新状态。
        var (option, votes) = options[optionId];
        options[optionId] = (option, votes + 1);
        await _votes.WriteStateAsync();

        // 通知观察者
        _pollWatchers.Notify(watcher => watcher.OnPollUpdated(_votes.State));
        return _votes.State;
    }

    private readonly ObserverManager<IPollWatcher> _pollWatchers = new(TimeSpan.FromMinutes(1));

    public Task StartWatching(IPollWatcher watcher)
    {
        _pollWatchers.Subscribe(watcher);
        return Task.CompletedTask;
    }

    public Task StopWatching(IPollWatcher watcher)
    {
        _pollWatchers.Unsubscribe(watcher);
        return Task.CompletedTask;
    }
}