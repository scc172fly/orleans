using System.Threading;

namespace Orleans.Runtime.Internal;

/// <summary>
/// 抑制 <see cref="ExecutionContext"/> 的流动，直至此对象被释放。
/// </summary>
/// <remarks>
/// 注意，这是一个 ref struct，以防止它在异步方法中被使用。
/// </remarks>
public ref struct ExecutionContextSuppressor
{
    private readonly bool _restoreFlow;

    /// <summary>
    /// 初始化新的 <see cref="ExecutionContextSuppressor"/> 实例。
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
    /// 执行应用程序定义的任务，用于释放或重置非托管资源。
    /// </summary>
    public readonly void Dispose()
    {
        if (_restoreFlow)
        {
            ExecutionContext.RestoreFlow();
        }
    }
}
