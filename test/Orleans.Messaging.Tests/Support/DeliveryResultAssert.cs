namespace Orleans.Durable.Messaging.Tests;

/// <summary>断言 <see cref="DeliveryResult"/> 包含哪个结果分支。</summary>
internal static class DeliveryResultAssert
{
    public static T IsCase<T>(DeliveryResult result) where T : class => Xunit.Assert.IsType<T>(result.Value);

    /// <summary>断言两个结果包含相同的分支和数据；分支按引用比较。</summary>
    public static void Equal(DeliveryResult expected, DeliveryResult actual) => Xunit.Assert.Equal(Key(expected), Key(actual));

    /// <summary>断言两个结果序列依次包含相同的分支和数据。</summary>
    public static void Equal(IEnumerable<DeliveryResult> expected, IEnumerable<DeliveryResult> actual) =>
        Xunit.Assert.Equal(expected.Select(Key), actual.Select(Key));

    /// <summary>断言 <paramref name="actual"/> 包含一个与 <paramref name="expected"/> 具有相同分支和数据的结果。</summary>
    public static void Contains(DeliveryResult expected, IEnumerable<DeliveryResult> actual) =>
        Xunit.Assert.Contains(Key(expected), actual.Select(Key));

    // 对分支及其字段进行结构化比较，区分 null 与空代码，也区分字段内的分隔符。
    private static (Type Case, string? Reason, string? Code) Key(DeliveryResult result) => result switch
    {
        DeliveryResult.DeadLettered { Reason: var reason, Code: var code } => (typeof(DeliveryResult.DeadLettered), reason, code),
        _ => (result.Value!.GetType(), null, null)
    };
}
