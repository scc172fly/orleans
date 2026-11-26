using System;
using System.Threading;
using Orleans.Runtime.Internal;

namespace Orleans.Runtime
{
    /// <summary>
    /// 方便便利 API 与 System.Threading.Timer 以某种方式
    /// 无法捕捉 ExecutionContext。我们应该使用这个(或同等的)
    /// 各地我们使用计时器避免根植存储在 AsyncLocals中的任何值。
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

            // 不要将当前 ExecutionContext 及其 AsyncLocals 记录在计时器上
            using var suppressExecutionContext = new ExecutionContextSuppressor();

            return new Timer(callback, state, dueTime, period);
        }
    }
}
