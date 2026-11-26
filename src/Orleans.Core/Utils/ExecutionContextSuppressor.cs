using System.Threading;

namespace Orleans.Runtime.Internal;

/// <summary>
/// 禁止 <see cref="ExecutionContext"/> 流动,直至处置为止。
/// </summary>
/// <remarks>
/// 请注意,这是避免在合成方法中使用的再加工装置。
/// </remarks>
public ref struct ExecutionContextSuppressor
{
    private readonly bool _restoreFlow;

    /// <summary>
    /// 初始化一个新的 <see cref="ExecutionContextSuppressor"/> 实例。
    /// </summary>
    public ExecutionContextSuppressor()
    {
        if (!ExecutionContext.IsFlowSuppressed())
        {
            ExecutionContext.SuppressFlow();
            _restoreFlow = true;
        }
        else
        {
            _restoreFlow = false;
        }
    }

    /// <summary>
    /// 执行与释放、释放或重新设置未管理的资源有关的应用程序界定的任务。
    /// </summary>
    public readonly void Dispose()
    {
        if (_restoreFlow)
        {
            ExecutionContext.RestoreFlow();
        }
    }
}
