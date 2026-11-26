namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供持久价值的观察流。
/// </summary>
/// <remarks>
/// <para>
/// 观察流以当前值开始, 当它可以耐久时, 然后在每日记后提供最新值
/// 写入后会确认修改, 因此读者只观察耐久状态。 流仅保留最新值 。
/// 无人观看的价值不会公布任何信息。
/// </para>
/// <para>
/// 在自控激活的执行背景中将流数数。 当自控管理器完成时, 流会完整
/// 当一个失败日记账操作停止管理器时, 停止停止时停止和断缺, 因为只有新激活才能建立
/// 变化是持久的。
/// </para>
/// </remarks>
public static class DurableStateWatchExtensions
{
    /// <summary>
    /// 观察持久价值的坚定价值。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="value">持久价值。</param>
    /// <param name="cancellationToken">结束表的标记。</param>
    /// <returns>以当前承诺值开始的流,在每次承诺变化后提供最新值。</returns>
    /// <exception cref="NotSupportedException"><paramref name="value"/> 并非由已日记的国家管理人员登记在册的内在耐久价值。</exception>
    public static IAsyncEnumerable<ValueChange<T>> WatchAsync<T>(this IDurableValue<T> value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value is DurableValue<T> durableValue
            ? durableValue.WatchAsync(cancellationToken)
            : throw new NotSupportedException($"State of type '{value.GetType()}' does not support watching. Watch a built-in durable value.");
    }
}
