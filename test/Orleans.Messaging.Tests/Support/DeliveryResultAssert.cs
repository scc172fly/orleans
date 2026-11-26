namespace Orleans.Durable.Messaging.Tests;

/// <summary>中存在 <see cref="DeliveryResult"/> 的发货。</summary>
internal static class DeliveryResultAssert
{
    public static T IsCase<T>(DeliveryResult result) where T : class => Xunit.Assert.IsType<T>(result.Value);

    /// <summary>断言两个结果都用同一数据维持同一情况;案件参照比较。</summary>
    public static void Equal(DeliveryResult expected, DeliveryResult actual) => Xunit.Assert.Equal(Key(expected), Key(actual));

    /// <summary>断言两个结果序列 都持有相同的病例 数据相同</summary>
    public static void Equal(IEnumerable<DeliveryResult> expected, IEnumerable<DeliveryResult> actual) =>
        Xunit.Assert.Equal(expected.Select(Key), actual.Select(Key));

    /// <summary>说 <paramref name="actual"/> 含有相同案件和数据的 <paramref name="expected"/>.</summary>
    public static void Contains(DeliveryResult expected, IEnumerable<DeliveryResult> actual) =>
        Xunit.Assert.Contains(Key(expected), actual.Select(Key));

    // 比较案例和其字段的结构,因此,空代码和字段内的分隔码与字段内分离码保持不同。
    private static (Type Case, string? Reason, string? Code) Key(DeliveryResult result) => result switch
    {
        DeliveryResult.DeadLettered { Reason: var reason, Code: var code } => (typeof(DeliveryResult.DeadLettered), reason, code),
        _ => (result.Value!.GetType(), null, null)
    };
}
