using System.Collections.Immutable;
using Chirper.Grains.Models;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Chirper.Grains;

[Reentrant]
public sealed class ChirperAccount : Grain, IChirperAccount
{
    /// <summary>
    /// 最近收到的信件缓存的大小 。
    /// </summary>
    private const int ReceivedMessagesCacheSize = 100;

    /// <summary>
    /// 发布信件缓存的大小 。
    /// </summary>
    private const int PublishedMessagesCacheSize = 100;

    /// <summary>
    /// 每一声的长度最大 。
    /// </summary>
    private const int MaxChirpLength = 280;

    /// <summary>
    /// 持有临时的观众名单。
    /// 此列表不属于状态的一部分, 且无法在 grain 解除作用后生存 。
    /// </summary>
    private readonly HashSet<IChirperViewer> _viewers = new();
    private readonly ILogger<ChirperAccount> _logger;
    private readonly IPersistentState<ChirperAccountState> _state;

    /// <summary>
    /// 允许在背景中进行状态写入 。
    /// </summary>
    private Task? _outstandingWriteStateOperation;

    public ChirperAccount(
       [PersistentState(stateName: "account", storageName: "AccountState")] IPersistentState<ChirperAccountState> state,
       ILogger<ChirperAccount> logger)
    {
        _state = state;
        _logger = logger;
    }

    private static string GrainType => nameof(ChirperAccount);
    private string GrainKey => this.GetPrimaryKeyString();

    public override Task OnActivateAsync(CancellationToken _)
    {
        _logger.LogInformation("{GrainType} {GrainKey} activated.", GrainType, GrainKey);

        return Task.CompletedTask;
    }

    public async ValueTask PublishMessageAsync(string message)
    {
        var chirp = CreateNewChirpMessage(message);

        _logger.LogInformation("{GrainType} {GrainKey} publishing new chirp message '{Chirp}'.",
            GrainType, GrainKey, chirp);

        _state.State.MyPublishedMessages.Enqueue(chirp);

        while (_state.State.MyPublishedMessages.Count > PublishedMessagesCacheSize)
        {
            _state.State.MyPublishedMessages.Dequeue();
        }

        await WriteStateAsync();

        // 新信件的提醒查看器
        _logger.LogInformation("{GrainType} {GrainKey} sending new chirp message to {ViewerCount} viewers.",
            GrainType, GrainKey, _viewers.Count);

        _viewers.ForEach(_ => _.NewChirp(chirp));

        // 新消息的后端通知
        _logger.LogInformation("{GrainType} {GrainKey} sending new chirp message to {FollowerCount} followers.",
            GrainType, GrainKey, _state.State.Followers.Count);

        await Task.WhenAll(_state.State.Followers.Values.Select(_ => _.NewChirpAsync(chirp)).ToArray());
    }

    public ValueTask<ImmutableList<ChirperMessage>> GetReceivedMessagesAsync(int number, int start)
    {
        if (start < 0) start = 0;
        if (start + number > _state.State.RecentReceivedMessages.Count)
        {
            number = _state.State.RecentReceivedMessages.Count - start;
        }

        return ValueTask.FromResult(
            _state.State.RecentReceivedMessages
                .Skip(start)
                .Take(number)
                .ToImmutableList());
    }

    public async ValueTask FollowUserIdAsync(string username)
    {
        _logger.LogInformation(
            "{GrainType} {UserName} > FollowUserName({TargetUserName}).",
            GrainType,
            GrainKey,
            username);

        var userToFollow = GrainFactory.GetGrain<IChirperPublisher>(username);

        await userToFollow.AddFollowerAsync(GrainKey, this.AsReference<IChirperSubscriber>());

        _state.State.Subscriptions[username] = userToFollow;

        await WriteStateAsync();

        // 通知任何查看者, 该用户的订阅已被添加
        _viewers.ForEach(cv => cv.SubscriptionAdded(username));
    }

    public async ValueTask UnfollowUserIdAsync(string username)
    {
        _logger.LogInformation(
            "{GrainType} {GrainKey} > UnfollowUserName({TargetUserName}).",
            GrainType,
            GrainKey,
            username);

        // 要求出版商删除此 grain 作为跟随者
        await GrainFactory.GetGrain<IChirperPublisher>(username)
            .RemoveFollowerAsync(GrainKey);

        // 将此出版商从订阅列表中删除
        _state.State.Subscriptions.Remove(username);

        // 现在保存
        await WriteStateAsync();

        // 通知事件用户
        _viewers.ForEach(cv => cv.SubscriptionRemoved(username));
    }

    public ValueTask<ImmutableList<string>> GetFollowingListAsync() =>
        ValueTask.FromResult(_state.State.Subscriptions.Keys.ToImmutableList());

    public ValueTask<ImmutableList<string>> GetFollowersListAsync() =>
        ValueTask.FromResult(_state.State.Followers.Keys.ToImmutableList());

    public ValueTask SubscribeAsync(IChirperViewer viewer)
    {
        _viewers.Add(viewer);
        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(IChirperViewer viewer)
    {
        _viewers.Remove(viewer);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ImmutableList<ChirperMessage>> GetPublishedMessagesAsync(int number, int start)
    {
        if (start < 0) start = 0;
        if (start + number > _state.State.MyPublishedMessages.Count)
        {
            number = _state.State.MyPublishedMessages.Count - start;
        }
        return ValueTask.FromResult(
            _state.State.MyPublishedMessages
                .Skip(start)
                .Take(number)
                .ToImmutableList());
    }

    public async ValueTask AddFollowerAsync(string username, IChirperSubscriber follower)
    {
        _state.State.Followers[username] = follower;
        await WriteStateAsync();
        _viewers.ForEach(cv => cv.NewFollower(username));
    }

    public ValueTask RemoveFollowerAsync(string username)
    {
        _state.State.Followers.Remove(username);
        return WriteStateAsync();
    }

    public async Task NewChirpAsync(ChirperMessage chirp)
    {
        _logger.LogInformation(
            "{GrainType} {GrainKey} received chirp message = {Chirp}",
            GrainType,
            GrainKey,
            chirp);

        _state.State.RecentReceivedMessages.Enqueue(chirp);

        // 仅在不使用固定队列时才相关
        while (_state.State.RecentReceivedMessages.Count > ReceivedMessagesCacheSize) // 保存不超过最多消息数
        {
            _state.State.RecentReceivedMessages.Dequeue();
        }

        await WriteStateAsync();

        // 通知任何查看者已经收到新的 chirp
        _logger.LogInformation(
            "{GrainType} {GrainKey} sending received chirp message to {ViewerCount} viewers",
            GrainType,
            GrainKey,
            _viewers.Count);

        _viewers.ForEach(_ => _.NewChirp(chirp));
    }

    private ChirperMessage CreateNewChirpMessage(string message) =>
        new(message, DateTimeOffset.UtcNow, GrainKey);

    // 当Reentrant grain 正在做 WriteStateAsync时,由于同时写作,有可能出现侵权行为。
    // 解决问题的方法就是进行序列化和批量写作,确保在任何时候只收到一份单封未完成的书。
    private async ValueTask WriteStateAsync()
    {
        if (_outstandingWriteStateOperation is Task currentWriteStateOperation)
        {
            try
            {
                // 等待着未完成的写作, 但忽略它, 因为其中不包括我们的更改
                await currentWriteStateOperation;
            }
            catch
            {
                // 忽略这个飞行中写入操作的所有错误, 因为最初的呼叫者会观察它 。
            }
            finally
            {
                if (_outstandingWriteStateOperation == currentWriteStateOperation)
                {
                    // 唯一能取消未完成行动 条件是它和我们期待的一样 否则
                    // 另一项请求可能已经提出。
                    _outstandingWriteStateOperation = null;
                }
            }
        }

        if (_outstandingWriteStateOperation is null)
        {
            // 如果在初始写作完成后,没有其他请求启动新的写作操作,请立即进行。
            currentWriteStateOperation = _state.WriteStateAsync();
            _outstandingWriteStateOperation = currentWriteStateOperation;
        }
        else
        {
            // 如果有许多要求被排在了持续状态的呼声中,就没有理由写新书
            // 每个操作的操作, 因为任何写作( 之前我们期待的首部) 都将累积
            // 包括我们打电话者要求的更改。 等待新的未写 。
            currentWriteStateOperation = _outstandingWriteStateOperation;
        }

        try
        {
            await currentWriteStateOperation;
        }
        finally
        {
            if (_outstandingWriteStateOperation == currentWriteStateOperation)
            {
                // 唯一能取消未完成行动 条件是它和我们期待的一样 否则
                // 另一项请求可能已经提出。
                _outstandingWriteStateOperation = null;
            }
        }
    }
}
