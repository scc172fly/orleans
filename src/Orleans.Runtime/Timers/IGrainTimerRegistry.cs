namespace Orleans.Runtime;

/// <summary>
/// 提供记录 Grain 定时器创建及删除的功能。
/// </summary>
internal interface IGrainTimerRegistry
{
    /// <summary>
    /// 向注册表发出已创建定时器的信号。
    /// </summary>
    /// <param name="timer">
    /// 定时器。
    /// </param>
    void OnTimerCreated(IGrainTimer timer);

    /// <summary>
    /// 向注册表发出已释放定时器的信号。
    /// </summary>
    /// <param name="timer">
    /// 定时器。
    /// </param>
    void OnTimerDisposed(IGrainTimer timer);
}

