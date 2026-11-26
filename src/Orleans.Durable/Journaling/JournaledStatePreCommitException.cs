namespace Orleans.Durable.Journaling;

/// <summary>
/// 报告说,由于国家准备失败,无法撰写日记。
/// </summary>
/// <remarks>
/// 有待改变的改动仍处于阶段,可以安全地进行。
/// 明确重新尝试持久性,或退休后从持久状态中恢复过来。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.JournaledStatePreCommitException")]
public sealed class JournaledStatePreCommitException : Exception
{
    /// <summary>
    /// 启动国家准备失败。
    /// </summary>
    /// <param name="innerException">原准备失败。</param>
    public JournaledStatePreCommitException(Exception innerException)
        : base("The journal write was prevented by a state preparation failure.", innerException)
    {
    }
}
