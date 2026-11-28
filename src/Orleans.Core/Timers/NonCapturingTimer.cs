using System;
using System.Threading;
using Orleans.Runtime.Internal;

namespace Orleans.Runtime
{
    /// <summary>
    /// 便于使用 System.Threading.Timer 的 API，
    /// 且不会捕获 ExecutionContext。我们应在所有使用定时器的地方采用此 API（或等效方式），
    /// 以免使存储在 AsyncLocal 中的任何值因根引用而无法被回收。
    /// </summary>
    /// <see href="https://github.com/dotnet/extensions/blob/a1389576a3bbc85a48bdcadce4f16bcf7cdfa088/src/Shared/src/NonCapturingTimer/NonCapturingTimer.cs"/>
    internal static class NonCapturingTimer
    {
        public static Timer Create(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            // 不要将当前 ExecutionContext 及其 AsyncLocal 捕获到定时器中。
            using var suppressExecutionContext = new ExecutionContextSuppressor();

            return new Timer(callback, state, dueTime, period);
        }
    }
}
