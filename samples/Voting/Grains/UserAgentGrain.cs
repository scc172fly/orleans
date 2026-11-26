using System.Diagnostics;
using VotingContract;

namespace Grains;

/// <summary>
/// 此 grain 演示了一种简单的节减特定客户端的方法
/// (按其IP地址确定,该地址被用作 grain的主要关键)
/// 该公司继续统计最近电话和油门,如果超过规定的门槛值。
/// 得分逐渐衰减 直到客户恢复通话
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

        // 获取民意调查的引用 grain
        var pollGrain = _grainFactory.GetGrain<IPollGrain>(pollId);

        // 获取当前民意调查结果
        var results = await pollGrain.GetCurrentResults();

        // 以及我们是否投票了
        return (Results: results, Voted: _votedPolls.Contains(pollId));
    }

    public async Task<string> CreatePoll(PollState initialState)
    {
        Throttle();

        // 限制任何一个用户能够投票的投票次数
        if (_myPolls.Count >= 5)
        {
            throw new InvalidOperationException("You have already created 5 polls, which is enough for anybody.");
        }

        // 生成一个新代号, 用该代号获取 PollGrain 的引用
        var pollId = Guid.NewGuid().ToString("N")[..6];
        var pollGrain = _grainFactory.GetGrain<IPollGrain>(pollId);

        // 创建民意调查。 我们可以避免在这里发生碰撞, 如果投票结果错误, 则会返回错误
        // 已经存在了。
        await pollGrain.CreatePoll(initialState);

        _myPolls.Add(pollId);
        return pollId;
    }

    public async Task<PollState> AddVote(string pollId, int optionId)
    {
        Throttle();

        // 首先,检查一下我们是否已经投票了
        if (_votedPolls.Contains(pollId))
        {
            throw new InvalidOperationException("You have already voted in that poll!");
        }

        // 表决票数票数投票的票数票数票数票数票数表决(V的票数)
        var pollGrain = _grainFactory.GetGrain<IPollGrain>(pollId);
        var result = await pollGrain.AddVote(optionId);

        // 记录我们的投票,防止双重投票
        _votedPolls.Add(pollId);
        return result;
    }

    private void Throttle()
    {
        // 查查上次电话打多久没接了
        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();

        // 计算基于固定的分数衰减率和
        // 上次召唤后已过了一段时间。
        _throttleScore = Math.Max(0, _throttleScore - elapsedSeconds * DecayRate) + 1;

        // 如果用户超过门槛,则拒绝其请求并给予
        // 有用的警告。
        if (_throttleScore > ThrottleThreshold)
        {
            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((_throttleScore - (ThrottleThreshold - 1)) / DecayRate));
            throw new ThrottlingException($"Request rate exceeded, wait {remainingSeconds}s before retrying");
        }
    }
}
