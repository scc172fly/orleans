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
    /// 最近接收消息缓存的容量。
    /// </summary>
    private const int ReceivedMessagesCacheSize = 100;

    /// <summary>
    /// 已发布消息缓存的容量。
    /// </summary>
    private const int PublishedMessagesCacheSize = 100;

    /// <summary>
    /// 每条 Chirp 消息的最大长度。
    /// </summary>
    private const int MaxChirpLength = 280;

    /// <summary>
    /// 保存临时的查看者列表。
    /// 此列表不是持久化状态的一部分，Grain 停用后不会保留。
    /// </summary>
    private readonly HashSet<IChirperViewer> _viewers = new();
    private readonly ILogger<ChirperAccount> _logger;
    private readonly IPersistentState<ChirperAccountState> _state;

    /// <summary>
    /// 允许在后台写入状态。
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

        // 向查看者通知新消息。
        _logger.LogInformation("{GrainType} {GrainKey} sending new chirp message to {ViewerCount} viewers.",
            GrainType, GrainKey, _viewers.Count);

        _viewers.ForEach(_ => _.NewChirp(chirp));

        // 向关注者通知新消息。
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

        // 通知所有查看者此用户新增了一项订阅。
        _viewers.ForEach(cv => cv.SubscriptionAdded(username));
    }

    public async ValueTask UnfollowUserIdAsync(string username)
    {
        _logger.LogInformation(
            "{GrainType} {GrainKey} > UnfollowUserName({TargetUserName}).",
            GrainType,
            GrainKey,
            username);

        // 请求发布者将此 Grain 从其关注者中移除。
        await GrainFactory.GetGrain<IChirperPublisher>(username)
            .RemoveFollowerAsync(GrainKey);

        // 从订阅列表中移除此发布者。
        _state.State.Subscriptions.Remove(username);

        // 立即保存。
        await WriteStateAsync();

        // 通知事件订阅者。
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

        // 仅在不使用固定长度队列时有意义。
        while (_state.State.RecentReceivedMessages.Count > ReceivedMessagesCacheSize) // 使消息数量不超过最大值。
        {
            _state.State.RecentReceivedMessages.Dequeue();
        }

        await WriteStateAsync();

        // 通知所有查看者已收到新的 Chirp 消息。
        _logger.LogInformation(
            "{GrainType} {GrainKey} sending received chirp message to {ViewerCount} viewers",
            GrainType,
            GrainKey,
            _viewers.Count);

        _viewers.ForEach(_ => _.NewChirp(chirp));
    }

    private ChirperMessage CreateNewChirpMessage(string message) =>
        new(message, DateTimeOffset.UtcNow, GrainKey);

    // 可重入 Grain 执行 WriteStateAsync 时，并发写入可能导致 ETag 冲突。
    // 解决办法是将写入串行化并批量合并，确保任意时刻只有一个尚未完成的写入操作。
    private async ValueTask WriteStateAsync()
    {
        if (_outstandingWriteStateOperation is Task currentWriteStateOperation)
        {
            try
            {
                // 等待尚未完成的写入，但忽略其结果，因为它不包含本次请求的变更。
                await currentWriteStateOperation;
            }
            catch
            {
                // 忽略此正在进行的写入操作产生的所有错误，因为该操作的原始调用者会观察到这些错误。
            }
            finally
            {
                if (_outstandingWriteStateOperation == currentWriteStateOperation)
                {
                    // 仅当当前尚未完成的操作与我们刚等待的操作相同时，才将其置为 null，否则
                    // 另一个请求可能已经完成了这一操作。
                    _outstandingWriteStateOperation = null;
                }
            }
        }

        if (_outstandingWriteStateOperation is null)
        {
            // 如果初始写入完成后，没有其他请求发起新的写入操作，则现在发起写入。
            currentWriteStateOperation = _state.WriteStateAsync();
            _outstandingWriteStateOperation = currentWriteStateOperation;
        }
        else
        {
            // 如果已有多个持久化状态的请求排队，则无需为每个请求都安排新的写入
            // 操作，因为初始写入（我们已经等待过）之后的任何写入都会包含累积的
            // 变更，包括本次调用者请求的变更。只需等待新的尚未完成的写入即可。
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
                // 仅当当前尚未完成的操作与我们刚等待的操作相同时，才将其置为 null，否则
                // 另一个请求可能已经完成了这一操作。
                _outstandingWriteStateOperation = null;
            }
        }
    }
}
