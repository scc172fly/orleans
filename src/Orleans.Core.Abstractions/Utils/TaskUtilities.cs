using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Orleans.Internal;

internal static class TaskUtilities
{
    /// <summary>
    /// 等待所有提供的任务，直接传播单个失败，或将多个失败包装为 <see cref="AggregateException"/> 进行传播。
    /// </summary>
    internal static Task WhenAllWithAggregateException(IEnumerable<Task> tasks)
    {
        var combined = Task.WhenAll(tasks);
        return combined.IsCompletedSuccessfully ? combined : AwaitCompletion(combined);

        static async Task AwaitCompletion(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch when (task.Exception is { InnerExceptions.Count: > 1 } exception)
            {
                ExceptionDispatchInfo.Throw(exception);
            }
        }
    }
}
