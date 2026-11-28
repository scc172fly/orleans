namespace Orleans.Durable.Journaling;

/// <summary>
/// 描述 <see cref="IDurableValue{T}"/> 的最新已提交值。
/// </summary>
/// <typeparam name="T">值类型。</typeparam>
/// <remarks>
/// 值观察投递最新已提交值。落后的读取方收到最新值并跳过
/// 中间值。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.ValueChange`1")]
public readonly record struct ValueChange<T>
{
    /// <summary>
    /// 获取值。
    /// </summary>
    [Id(0)]
    public T? Value { get; init; }

    /// <summary>
    /// 获取使 <see cref="Value"/> 持久化的日志写入的激活实例本地版本。
    /// </summary>
    /// <remarks>版本在激活实例内部递增，不可跨激活实例比较。</remarks>
    [Id(1)]
    public long Version { get; init; }
}
