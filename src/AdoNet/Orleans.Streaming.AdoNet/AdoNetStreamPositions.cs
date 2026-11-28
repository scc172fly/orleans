using System.Threading;

namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 为已出队消息分配队列缓存位置，并将位置解析为对应消息标识符。
/// </summary>
/// <remarks>
/// 队列缓存要求序列令牌在多次读取之间按出队顺序递增。
/// 消息标识符不能提供此保证，因为由先前所有者释放、
/// 投递失败后重试或延迟提交的消息，可能在标识符更大的消息之后才变为可见。
/// 位置在提供程序范围内唯一，因此某个位置绝不会被解析为由其他接收器出队的消息。
/// </remarks>
internal sealed class AdoNetStreamPositions
{
    private readonly ConcurrentDictionary<long, long> _messageIds = new();
    private long _lastPosition;

    /// <summary>
    /// 为指定消息分配下一个位置。
    /// </summary>
    public long Add(long messageId)
    {
        var position = Interlocked.Increment(ref _lastPosition);
        _messageIds[position] = messageId;
        return position;
    }

    /// <summary>
    /// 获取尚未移除的位置所对应的消息标识符。
    /// </summary>
    public bool TryGetMessageId(long position, out long messageId) => _messageIds.TryGetValue(position, out messageId);

    /// <summary>
    /// 当消息已离开队列缓存或不再由接收器持有时，移除其位置。
    /// </summary>
    public void Remove(long position) => _messageIds.TryRemove(position, out _);
}
