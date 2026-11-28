using System.Diagnostics.CodeAnalysis;

namespace Orleans.Durable.Tasks;

/// <summary>提供宿主运行持久化任务定义的入口点。</summary>
public static class DurableTaskRuntimeHelper
{
    /// <summary>运行 <paramref name="task"/>，使用上下文 <paramref name="context"/>，并将终态异常转换为响应。</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Durable task failures are represented as terminal responses.")]
    public static async ValueTask<DurableTaskResponse> RunAsync(DurableTask task, DurableTaskContext context)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);
        using var scope = DurableTaskContext.Enter(context);
        try
        {
            return await task.RunAsync(context);
        }
        catch (Exception exception)
        {
            return DurableTaskResponse.FromException(exception);
        }
    }
}
