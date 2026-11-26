using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Internal
{
    /// <summary>
    /// 与 <see cref="Task"/> 和 <see cref="Task{TResult}"/>合作的扩展。
    /// </summary>
    internal static class OrleansTaskExtentions
    {
        private static readonly ConcurrentDictionary<ErrorCode, Action<ILogger, string, Exception?>> LogExceptionDelegates = new();

        public static ConfiguredTaskAwaitable SuppressThrowing(this ValueTask task) => task.AsTask().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        public static ConfiguredTaskAwaitable SuppressThrowing(this Task task) => task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

        public static void Ignore(this ValueTask valueTask)
        {
            if (valueTask.IsCompletedSuccessfully)
            {
                valueTask.GetAwaiter().GetResult();
            }
            else
            {
                valueTask.AsTask().Ignore();
            }
        }

        public static async Task LogException(this Task task, ILogger logger, ErrorCode errorCode, string message)
        {
            try
            {
                await task;
            }
            catch (Exception exc)
            {
                var log = LogExceptionDelegates.GetOrAdd(
                    errorCode,
                    static code => LoggerMessage.Define<string>(LogLevel.Error, new EventId((int)code), "{Message}"));
                log(logger, message, exc);
                throw;
            }
        }

        // 执行一个自动同步函数,例如例外从不抛出,而是总是作为中断的任务返回。
        public static async Task SafeExecute(Func<Task> action)
        {
            await action();
        }

        public static async Task ExecuteAndIgnoreException(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception)
            {
                // 不要再拉,就吃吧
            }
        }

        internal static string ToString(this Task? t) => t == null ? "null" : $"[Id={t.Id}, Status={t.Status}]";

        /// <summary>
        /// 这将对任务造成超时延迟, 使我们提前退出
        /// </summary>
        /// <param name="taskToComplete">任务在 timeSpan</param>之后我们将超时
        /// <param name="timeout">计时前等待时间的长度</param>
        /// <param name="exceptionMessage">用于超时发送例外电文的文本</param>
        /// <exception cref="TimeoutException">如果我们暂停时间 就会破例</exception>
        /// <returns>已完成的任务</returns>
        public static async Task WithTimeout(this Task taskToComplete, TimeSpan timeout, string? exceptionMessage = null)
        {
            if (taskToComplete.IsCompleted)
            {
                await taskToComplete;
                return;
            }

            using var timeoutCancellationTokenSource = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(taskToComplete, Task.Delay(timeout, timeoutCancellationTokenSource.Token));

            // 暂停前我们完成的 或者在代码运行之前完成的 返回结果
            if (taskToComplete == completedTask)
            {
                await timeoutCancellationTokenSource.CancelAsync();
                // 等待这个,以便正确宣传例外
                await taskToComplete;
                return;
            }

            // 超时前我们没有完成,我们开火并忘记确保遵守任何例外
            taskToComplete.Ignore();
            var errorMessage = exceptionMessage ?? $"WithTimeout has timed out after {timeout}";
            throw new TimeoutException(errorMessage);
        }

        /// <summary>
        /// 这将对任务造成超时延迟, 使我们提前退出
        /// </summary>
        /// <param name="taskToComplete">任务在 timeSpan</param>之后我们将超时
        /// <param name="timeSpan">计时前等待时间的长度</param>
        /// <param name="exceptionMessage">用于超时发送例外电文的文本</param>
        /// <exception cref="TimeoutException">如果我们暂停时间 就会破例</exception>
        /// <exception cref="TimeoutException">如果我们暂停时间 就会破例</exception>
        /// <returns>已完成任务值</returns>
        public static async Task<T> WithTimeout<T>(this Task<T> taskToComplete, TimeSpan timeSpan, string? exceptionMessage = null)
        {
            if (taskToComplete.IsCompleted)
            {
                return await taskToComplete;
            }

            using var timeoutCancellationTokenSource = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(taskToComplete, Task.Delay(timeSpan, timeoutCancellationTokenSource.Token));

            // 暂停前我们完成的 或者在代码运行之前完成的 返回结果
            if (taskToComplete == completedTask)
            {
                await timeoutCancellationTokenSource.CancelAsync();
                // 等待这个,以便正确宣传例外
                return await taskToComplete;
            }

            // 超时前我们没有完成,我们开火并忘记确保遵守任何例外
            taskToComplete.Ignore();
            var errorMessage = exceptionMessage ?? $"WithTimeout has timed out after {timeSpan}";
            throw new TimeoutException(errorMessage);
        }

        /// <summary>
        /// 使不可挽回的任务可以被消除,忽视其结果。
        /// </summary>
        /// <param name="taskToComplete">除非取消否则等待的任务</param>
        /// <param name="message">信件要置入例外</param>
        /// <param name="cancellationToken">取消等待的取消标记</param>
        /// <returns></returns>
        internal static async Task WithCancellation(
            this Task taskToComplete,
            string message,
            CancellationToken cancellationToken)
        {
            try
            {
                await taskToComplete.WithCancellation(cancellationToken);
            }
            catch (TaskCanceledException ex)
            {
                throw new TaskCanceledException(message, ex);
            }
        }

        /// <summary>
        /// 使不可挽回的任务可以被消除,忽视其结果。
        /// </summary>
        /// <param name="taskToComplete">除非取消否则等待的任务</param>
        /// <param name="cancellationToken">取消等待的取消标记</param>
        /// <returns></returns>
        internal static Task WithCancellation(this Task taskToComplete, CancellationToken cancellationToken)
        {
            if (taskToComplete.IsCompleted || !cancellationToken.CanBeCanceled)
            {
                return taskToComplete;
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<object>(cancellationToken);
            }
            else
            {
                return MakeCancellable(taskToComplete, cancellationToken);
            }
        }

        private static async Task MakeCancellable(Task task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() =>
                      tcs.TrySetCanceled(cancellationToken), useSynchronizationContext: false))
            {
                var firstToComplete = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);

                if (firstToComplete != task)
                {
                    task.Ignore();
                }

                await firstToComplete.ConfigureAwait(false);
            }
        }

        internal static Task WrapInTask(Action action)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception exc)
            {
                return Task.FromException<object>(exc);
            }
        }

        //GetAwaiter()的理由理由[。GetResult() 而不是.
        //以 https://github.com/aspnet/Security/issues/59表示。
        internal static T GetResult<T>(this Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }

        internal static Task WhenCancelled(this CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            var waitForCancellation = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(obj =>
            {
                var tcs = (TaskCompletionSource<object?>)obj!;
                tcs.TrySetResult(null);
            }, waitForCancellation);

            return waitForCancellation.Task;
        }
    }
}
