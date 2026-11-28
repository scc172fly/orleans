using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.Internal
{
    /// <summary>
    /// 用于操作 <see cref="Task"/> 和 <see cref="Task{TResult}"/> 的扩展方法。
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

        // 执行异步函数，使异常不会直接抛出，而始终通过故障任务返回。
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
                // 不重新抛出异常，直接将其忽略。
            }
        }

        internal static string ToString(this Task? t) => t == null ? "null" : $"[Id={t.Id}, Status={t.Status}]";

        /// <summary>
        /// 为任务设置超时延迟，使我们可以提前退出等待。
        /// </summary>
        /// <param name="taskToComplete">在 timeSpan 后超时的任务。</param>
        /// <param name="timeout">超时前的等待时长。</param>
        /// <param name="exceptionMessage">写入超时异常消息的文本。</param>
        /// <exception cref="TimeoutException">发生超时时会收到此异常。</exception>
        /// <returns>已完成的任务。</returns>
        public static async Task WithTimeout(this Task taskToComplete, TimeSpan timeout, string? exceptionMessage = null)
        {
            if (taskToComplete.IsCompleted)
            {
                await taskToComplete;
                return;
            }

            using var timeoutCancellationTokenSource = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(taskToComplete, Task.Delay(timeout, timeoutCancellationTokenSource.Token));

            // 在超时前完成了任务，或者在运行此处代码之前任务已经完成，返回结果。
            if (taskToComplete == completedTask)
            {
                await timeoutCancellationTokenSource.CancelAsync();
                // 等待此任务，以正确传播异常。
                await taskToComplete;
                return;
            }

            // 未在超时前完成；以即发即弃的方式观察稍后可能发生的所有异常。
            taskToComplete.Ignore();
            var errorMessage = exceptionMessage ?? $"WithTimeout has timed out after {timeout}";
            throw new TimeoutException(errorMessage);
        }

        /// <summary>
        /// 为任务设置超时延迟，使我们可以提前退出等待。
        /// </summary>
        /// <param name="taskToComplete">在 timeSpan 后超时的任务。</param>
        /// <param name="timeSpan">超时前的等待时长。</param>
        /// <param name="exceptionMessage">写入超时异常消息的文本。</param>
        /// <exception cref="TimeoutException">发生超时时会收到此异常。</exception>
        /// <exception cref="TimeoutException">发生超时时会收到此异常。</exception>
        /// <returns>已完成任务的结果值。</returns>
        public static async Task<T> WithTimeout<T>(this Task<T> taskToComplete, TimeSpan timeSpan, string? exceptionMessage = null)
        {
            if (taskToComplete.IsCompleted)
            {
                return await taskToComplete;
            }

            using var timeoutCancellationTokenSource = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(taskToComplete, Task.Delay(timeSpan, timeoutCancellationTokenSource.Token));

            // 在超时前完成了任务，或者在运行此处代码之前任务已经完成，返回结果。
            if (taskToComplete == completedTask)
            {
                await timeoutCancellationTokenSource.CancelAsync();
                // 等待此任务，以正确传播异常。
                return await taskToComplete;
            }

            // 未在超时前完成；以即发即弃的方式观察稍后可能发生的所有异常。
            taskToComplete.Ignore();
            var errorMessage = exceptionMessage ?? $"WithTimeout has timed out after {timeSpan}";
            throw new TimeoutException(errorMessage);
        }

        /// <summary>
        /// 通过忽略任务结果，让原本无法取消的任务支持取消等待。
        /// </summary>
        /// <param name="taskToComplete">在未取消时要等待的任务。</param>
        /// <param name="message">要设置到异常中的消息。</param>
        /// <param name="cancellationToken">用于取消等待的取消令牌。</param>
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
        /// 通过忽略任务结果，让原本无法取消的任务支持取消等待。
        /// </summary>
        /// <param name="taskToComplete">在未取消时要等待的任务。</param>
        /// <param name="cancellationToken">用于取消等待的取消令牌。</param>
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

        // 使用 GetAwaiter().GetResult() 而不是 .Result 的理由
        // 见 https://github.com/aspnet/Security/issues/59。
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
