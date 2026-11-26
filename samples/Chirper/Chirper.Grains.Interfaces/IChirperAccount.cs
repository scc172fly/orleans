using System.Collections.Immutable;
using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// Orleans grain 接口 IChirperAccount -- 这是特定用户的用户端控制 grain 。
/// 适当认证的外部应用程序可通过该 grain代表用户履行出版商和订阅者的职能。
/// 该 grain 介质将外部应用程序与用户的出版商 &amp; 订阅代理器 grains之间的命令和通信连接。
/// </summary>
public interface IChirperAccount : IChirperPublisher, IChirperSubscriber
{
    /// <summary>向指定的出版商添加用户的新的后续订阅</summary>
    /// <param name="userNameToFollow">用户现在跟随的新出版商的用户名</param>
    /// <returns>运行此操作的任务状态</returns>
    ValueTask FollowUserIdAsync(string userNameToFollow);

    /// <summary>从用户中删除一个跟随器订阅到指定的出版商</summary>
    /// <param name="userNameToUnfollow">用户不再跟随出版商的用户名</param>
    /// <returns>AsyncCompletion 此项操作的状态</returns>
    ValueTask UnfollowUserIdAsync(string userNameToUnfollow);

    /// <summary>获取用户跟踪的出版商名单</summary>
    /// <returns>被跟踪的用户名单</returns>
    ValueTask<ImmutableList<string>> GetFollowingListAsync();

    /// <summary>获取跟踪此用户的订阅者名单</summary>
    /// <returns>跟踪此用户的用户列表</returns>
    ValueTask<ImmutableList<string>> GetFollowersListAsync();

    /// <summary>发布新的千里普消息</summary>
    /// <param name="chirpMessage">将作为新的 Chirp</param>发布的信息文本
    /// <returns>出版作业的完成状态</returns>
    ValueTask PublishMessageAsync(string chirpMessage);

    /// <summary>请求用户从指定的起始位置收到最新“n” Chirp 消息。</summary>
    /// <param name="n">请求的千分点消息数。 -1 值是指全部信件。</param>
    /// <param name="start">返回的信件的起始位置。 0 值意味着以最近的信件开头。正值意味着跳过许多最近的邮件</param>
    /// <returns>该订阅者收到的千里普电文列表</returns>
    /// <remarks>由于信息保留政策,用户可能只能归还部分历史事件记录。</remarks>
    ValueTask<ImmutableList<ChirperMessage>> GetReceivedMessagesAsync(int n = 10, int start = 0);

    /// <summary>
    /// 订阅本 grain的实时通知。
    /// </summary>
    /// <param name="viewer">接收通知的观察员。</param>
    ValueTask SubscribeAsync(IChirperViewer viewer);

    /// <summary>
    /// 取消从此 grain的实时通知中订阅给定查看器。
    /// </summary>
    /// <param name="viewer"></param>
    ValueTask UnsubscribeAsync(IChirperViewer viewer);
}
