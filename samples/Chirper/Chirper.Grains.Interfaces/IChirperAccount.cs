using System.Collections.Immutable;
using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// Orleans Grain 接口 IChirperAccount——面向用户的控制 Grain，对应特定用户。
/// 经过适当身份验证的外部应用可以通过此 Grain 代表用户执行发布者和订阅者的功能。
/// 此 Grain 在外部应用与该用户的发布者和订阅者代理 Grain 之间协调命令和通信。
/// </summary>
public interface IChirperAccount : IChirperPublisher, IChirperSubscriber
{
    /// <summary>为此用户添加对指定发布者的关注订阅。</summary>
    /// <param name="userNameToFollow">此用户新关注的发布者的用户名。</param>
    /// <returns>此操作的任务状态。</returns>
    ValueTask FollowUserIdAsync(string userNameToFollow);

    /// <summary>移除此用户对指定发布者的关注订阅。</summary>
    /// <param name="userNameToUnfollow">此用户不再关注的发布者的用户名。</param>
    /// <returns>此操作的异步完成状态。</returns>
    ValueTask UnfollowUserIdAsync(string userNameToUnfollow);

    /// <summary>获取此用户正在关注的发布者列表。</summary>
    /// <returns>正在关注的用户列表。</returns>
    ValueTask<ImmutableList<string>> GetFollowingListAsync();

    /// <summary>获取正在关注此用户的订阅者列表。</summary>
    /// <returns>正在关注此用户的用户列表。</returns>
    ValueTask<ImmutableList<string>> GetFollowersListAsync();

    /// <summary>发布一条新的 Chirp 消息。</summary>
    /// <param name="chirpMessage">作为新 Chirp 消息发布的文本。</param>
    /// <returns>发布操作的完成状态。</returns>
    ValueTask PublishMessageAsync(string chirpMessage);

    /// <summary>从指定起始位置请求此订阅者最近收到的 n 条 Chirp 消息。</summary>
    /// <param name="n">请求的 Chirp 消息数量。-1 表示全部消息。</param>
    /// <param name="start">返回消息的起始位置。0 表示从最新消息开始；正数表示跳过相应数量的最新消息。</param>
    /// <returns>此订阅者收到的 Chirp 消息列表。</returns>
    /// <remarks>由于消息保留策略，订阅者可能只返回部分历史记录。</remarks>
    ValueTask<ImmutableList<ChirperMessage>> GetReceivedMessagesAsync(int n = 10, int start = 0);

    /// <summary>
    /// 订阅来自此 Grain 的实时通知。
    /// </summary>
    /// <param name="viewer">用于接收通知的观察者。</param>
    ValueTask SubscribeAsync(IChirperViewer viewer);

    /// <summary>
    /// 取消指定查看者对来自此 Grain 的实时通知的订阅。
    /// </summary>
    /// <param name="viewer"></param>
    ValueTask UnsubscribeAsync(IChirperViewer viewer);
}
