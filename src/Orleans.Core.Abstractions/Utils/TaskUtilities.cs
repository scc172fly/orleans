using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Orleans.Internal;

internal static class TaskUtilities
{
    /// <summary>
    /// 等待每项提供的任务,将单个失败直接或多次失败作为 <see cref="AggregateException"/>来宣传。
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
