namespace Orleans.Durable.Journaling;

/// <summary>
/// 报告阻止日志写入的状态准备失败。
/// </summary>
/// <remarks>
/// 待提交变更仍保持暂存且可以安全提交。调用方可以恢复前置条件
/// 并显式重试持久化，也可以使其所有者退役并从持久化状态恢复。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.JournaledStatePreCommitException")]
public sealed class JournaledStatePreCommitException : Exception
{
    /// <summary>
    /// 初始化一个状态准备失败异常。
    /// </summary>
    /// <param name="innerException">原始的准备失败。</param>
    public JournaledStatePreCommitException(Exception innerException)
        : base("The journal write was prevented by a state preparation failure.", innerException)
    {
    }
}
