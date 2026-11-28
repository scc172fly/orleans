using System.Diagnostics;
using VotingContract;

namespace Grains;

/// <summary>
/// 此 Grain 演示了一种对指定客户端进行限流的简单方法
/// （通过其 IP 地址进行标识，该地址用作 Grain 的主键）。
/// 它维护近期调用的计数，并在计数超过预定义阈值时进行限流。
/// 分数会随时间衰减，使客户端可以恢复调用。
/// </summary>
internal class UserAgentGrain : Grain, IUserAgentGrain
{
    private const double DecayRate = ThrottleThreshold / (double)DecayPeriod;
    private const int ThrottleThreshold = 10;
    private const int DecayPeriod = 5;

    private readonly IGrainFactory _grainFactory;
    private readonly HashSet<string> _votedPolls = new();
    private readonly HashSet<string> _myPolls = new();

    private double _throttleScore;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    public UserAgentGrain(IGrainFactory grainFactory) => _grainFactory = grainFactory;

    public async Task<(PollState Results, bool Voted)> GetPollResults(string pollId)
    {
        Throttle();

        // 获取投票 Grain 的引用。
        var pollGrain = _grainFactory.GetGrain<IPollGrain>(pollId);

        // 获取当前投票结果。
        var results = await pollGrain.GetCurrentResults();

        // 返回结果，并同时返回当前用户是否已经投票。
        return (Results: results, Voted: _votedPolls.Contains(pollId));
    }

    public async Task<string> CreatePoll(PollState initialState)
    {
        Throttle();

        // 限制任意单个用户创建的投票数量。
        if (_myPolls.Count >= 5)
        {
            throw new InvalidOperationException("You have already created 5 polls, which is enough for anybody.");
        }

        // 生成新 ID，并获取使用该 ID 的 PollGrain 引用。
        var pollId = Guid.NewGuid().ToString("N")[..6];
        var pollGrain = _grainFactory.GetGrain<IPollGrain>(pollId);

        // 创建投票。如果具有该 ID 的投票已存在，可以通过返回错误来
        // 避免 ID 冲突。
        await pollGrain.CreatePoll(initialState);

        _myPolls.Add(pollId);
        return pollId;
    }

    public async Task<PollState> AddVote(string pollId, int optionId)
    {
        Throttle();

        // 首先检查当前用户是否已经投票。
        if (_votedPolls.Contains(pollId))
        {
            throw new InvalidOperationException("You have already voted in that poll!");
        }

        // 投票。
        var pollGrain = _grainFactory.GetGrain<IPollGrain>(pollId);
        var result = await pollGrain.AddVote(optionId);

        // 记录当前用户的投票，防止重复投票。
        _votedPolls.Add(pollId);
        return result;
    }

    private void Throttle()
    {
        // 计算自上次调用以来经过的时间。
        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();

        // 根据恒定的分数衰减速率及
        // 自上次调用以来经过的时间，计算新分数。
        _throttleScore = Math.Max(0, _throttleScore - elapsedSeconds * DecayRate) + 1;

        // 如果用户已超过阈值，则拒绝其请求，并向其提供
        // 有帮助的警告信息。
        if (_throttleScore > ThrottleThreshold)
        {
            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((_throttleScore - (ThrottleThreshold - 1)) / DecayRate));
            throw new ThrottlingException($"Request rate exceeded, wait {remainingSeconds}s before retrying");
        }
    }
}
