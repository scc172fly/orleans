using System.Collections.Immutable;
using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// Orleans grain 接口 IChirperPublisher。
/// </summary>
public interface IChirperPublisher : IGrainWithStringKey
{
    /// <summary>
    /// 请从指定的起始位置获取该出版商最新发来的“ n” Chirp 信息副本。
    /// </summary>
    /// <param name="n">请求的千分点消息数。 -1 值是指全部信件。</param>
    /// <param name="start">返回的信件的起始位置。 0 值意味着以最近的信件开头。正值意味着跳过许多最近的邮件</param>
    /// <returns>该出版商所发的 Chirp 信息散装列表</returns>
    /// <remarks>由于信息保留政策,出版商可能只能归还部分历史事件记录。</remarks>
    ValueTask<ImmutableList<ChirperMessage>> GetPublishedMessagesAsync(int n = 10, int start = 0);

    /// <summary>
    /// 订阅该出版商发出的关于新奇普的通知。
    /// </summary>
    /// <param name="userName">要添加的用户用户名。</param>
    /// <param name="subscriber">用户将添加。</param>
    /// <returns>AsyncCompletion 此项操作的状态</returns>
    ValueTask AddFollowerAsync(string userName, IChirperSubscriber subscriber);

    /// <summary>
    /// 不接受该出版商发出的关于新Chirps的通知。
    /// </summary>
    /// <param name="userName">要删除的用户用户名。</param>
    /// <returns>AsyncCompletion 用于此操作的</returns>
    ValueTask RemoveFollowerAsync(string userName);
}
