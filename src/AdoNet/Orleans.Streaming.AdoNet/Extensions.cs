namespace Orleans.Streaming.AdoNet;

/// <summary>
/// 内部语法糖。
/// </summary>
internal static class Extensions
{
    /// <inheritdoc cref="Math.Ceiling(double)"/>
    public static int Int32Ceiling(this double value) => (int)Math.Ceiling(value);

    /// <summary>
    /// 将指定时间跨度向上舍入到整秒，并以整数返回总秒数。
    /// </summary>
    public static int TotalSecondsCeiling(this TimeSpan value) => value.TotalSeconds.Int32Ceiling();

    /// <summary>
    /// 将指定时间跨度向上舍入到整秒，并以整数返回总秒数。
    /// </summary>
    public static TimeSpan SecondsCeiling(this TimeSpan value) => TimeSpan.FromSeconds(value.TotalSecondsCeiling());
}
