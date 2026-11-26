namespace Orleans.Durable.Journaling;

/// <summary>
/// 描述一个 <see cref="IDurableValue{T}"/>的最新承诺值。
/// </summary>
/// <typeparam name="T">值类型。</typeparam>
/// <remarks>
/// 值表显示最新的承诺值。 落后的读者会收到最新的值和跳过
/// 中间值。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.ValueChange`1")]
public readonly record struct ValueChange<T>
{
    /// <summary>
    /// 得到价值。
    /// </summary>
    [Id(0)]
    public T? Value { get; init; }

    /// <summary>
    /// 获取使 <see cref="Value"/> 耐久的日记书的激活本地版本 。
    /// </summary>
    /// <remarks>激活中版本的增加,且在所有启动中无法进行比较。</remarks>
    [Id(1)]
    public long Version { get; init; }
}
