namespace Orleans.Runtime;

/// <summary>
/// 提供功能,记录 grain 计时器的创建和删除。
/// </summary>
internal interface IGrainTimerRegistry
{
    /// <summary>
    /// 向登记处发出信号,显示创建了定时器。
    /// </summary>
    /// <param name="timer">
    /// 计时器
    /// </param>
    void OnTimerCreated(IGrainTimer timer);

    /// <summary>
    /// 向登记处发出关于处置定时器的信号。
    /// </summary>
    /// <param name="timer">
    /// 计时器
    /// </param>
    void OnTimerDisposed(IGrainTimer timer);
}

