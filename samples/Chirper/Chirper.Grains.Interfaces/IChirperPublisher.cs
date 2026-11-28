using System.Collections.Immutable;
using Chirper.Grains.Models;

namespace Chirper.Grains;

/// <summary>
/// Orleans Grain 接口 IChirperPublisher。
/// </summary>
public interface IChirperPublisher : IGrainWithStringKey
{
    /// <summary>
    /// 从指定起始位置请求此发布者最近发布的 n 条 Chirp 消息的副本。
    /// </summary>
    /// <param name="n">请求的 Chirp 消息数量。-1 表示全部消息。</param>
    /// <param name="start">返回消息的起始位置。0 表示从最新消息开始；正数表示跳过相应数量的最新消息。</param>
    /// <returns>此发布者发布的 Chirp 消息批量列表。</returns>
    /// <remarks>由于消息保留策略，发布者可能只返回部分历史记录。</remarks>
    ValueTask<ImmutableList<ChirperMessage>> GetPublishedMessagesAsync(int n = 10, int start = 0);

    /// <summary>
    /// 订阅此发布者发出的新 Chirp 消息的通知。
    /// </summary>
    /// <param name="userName">要添加的订阅者的用户名。</param>
    /// <param name="subscriber">要添加的订阅者。</param>
    /// <returns>此操作的异步完成状态。</returns>
    ValueTask AddFollowerAsync(string userName, IChirperSubscriber subscriber);

    /// <summary>
    /// 取消订阅此发布者发出的新 Chirp 消息的通知。
    /// </summary>
    /// <param name="userName">要移除的订阅者的用户名。</param>
    /// <returns>此操作的异步完成状态。</returns>
    ValueTask RemoveFollowerAsync(string userName);
}
