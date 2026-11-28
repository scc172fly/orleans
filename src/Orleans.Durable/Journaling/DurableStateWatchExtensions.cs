namespace Orleans.Durable.Journaling;

/// <summary>
/// 为持久化值提供观察流。
/// </summary>
/// <remarks>
/// <para>
/// 观察流在当前值已持久化后从该值开始，并在每次改变其值的日志
/// 写入得到确认后投递最新值，因此读取方只观察已持久化状态。流只保留最新值。
/// 没有观察者的值不会发布任何内容。
/// </para>
/// <para>
/// 在所属激活实例的执行上下文中枚举该流。所属管理器停止时流完成；
/// 若日志操作失败导致管理器停止，则流进入故障状态，因为只有新的激活实例才能确定
/// 哪些变更已持久化。
/// </para>
/// </remarks>
public static class DurableStateWatchExtensions
{
    /// <summary>
    /// 观察持久化值的已提交值。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="value">持久化值。</param>
    /// <param name="cancellationToken">用于结束观察的令牌。</param>
    /// <returns>一个从当前已提交值开始，并在每次变更提交后投递最新值的流。</returns>
    /// <exception cref="NotSupportedException"><paramref name="value"/> 不是已向日志状态管理器注册的内置持久化值。</exception>
    public static IAsyncEnumerable<ValueChange<T>> WatchAsync<T>(this IDurableValue<T> value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value is DurableValue<T> durableValue
            ? durableValue.WatchAsync(cancellationToken)
            : throw new NotSupportedException($"State of type '{value.GetType()}' does not support watching. Watch a built-in durable value.");
    }
}
