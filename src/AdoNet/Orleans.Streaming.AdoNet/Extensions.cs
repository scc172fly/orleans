namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 内部语法糖
/// </summary>
internal static class Extensions
{
    /// <inheritdoc cref="Math.Ceiling(double)"/>
    public static int Int32Ceiling(this double value) => (int)Math.Ceiling(value);

    /// <summary>
    /// 将指定的时间间隔舍入到最近的上秒, 并将秒总数以整数返回。
    /// </summary>
    public static int TotalSecondsCeiling(this TimeSpan value) => value.TotalSeconds.Int32Ceiling();

    /// <summary>
    /// 将指定的时间间隔舍入到最近的上秒, 并将秒总数以整数返回。
    /// </summary>
    public static TimeSpan SecondsCeiling(this TimeSpan value) => TimeSpan.FromSeconds(value.TotalSecondsCeiling());
}
