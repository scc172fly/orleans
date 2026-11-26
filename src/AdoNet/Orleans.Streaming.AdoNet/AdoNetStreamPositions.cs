using System.Threading;

namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 指定队列缓存位置以解排信件, 并解决其信件标识符的方位 。
/// </summary>
/// <remarks>
/// 队列缓存需要序列符号, 增加取消顺序的顺序会读取 。
/// 电文识别特征不能提供这一保证,因为先前的所有人所发电文、
/// 交付失败后重新处理,或发送时间过晚在信息具有较大识别特征之后可见。
/// 提供方内部的位置是独一无二的, 所以一个位置永远无法解决由另一个接收器解密的信息。
/// </remarks>
internal sealed class AdoNetStreamPositions
{
    private readonly ConcurrentDictionary<long, long> _messageIds = new();
    private long _lastPosition;

    /// <summary>
    /// 指定指定指定信件的下一个位置。
    /// </summary>
    public long Add(long messageId)
    {
        var position = Interlocked.Increment(ref _lastPosition);
        _messageIds[position] = messageId;
        return position;
    }

    /// <summary>
    /// 获取尚未删除位置的信息标识符。
    /// </summary>
    public bool TryGetMessageId(long position, out long messageId) => _messageIds.TryGetValue(position, out messageId);

    /// <summary>
    /// 一旦信件离开队列缓存或接收器不再持有, 则删除位置 。
    /// </summary>
    public void Remove(long position) => _messageIds.TryRemove(position, out _);
}
