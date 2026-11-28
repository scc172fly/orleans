using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Internal
{
    /// <summary>
    /// 此类是一个便捷工具，用于执行可重试的异步函数，
    /// 并允许指定自定义的重试筛选器和策略。
    /// </summary>
    public static class AsyncExecutorWithRetries
    {
        /// <summary>
        /// 用于请求无限次重试的常量。
        /// </summary>
        public static readonly int INFINITE_RETRIES = -1;

        /// <summary>
        /// 根据重试配置参数执行给定函数若干次。
        /// </summary>
        /// <param name="action">
        /// 要执行的操作。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 最大重试次数。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 重试异常筛选器。
        /// </param>
        /// <param name="maxExecutionTime">
        /// 最长执行时间。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 退避策略提供程序。
        /// </param>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 <see langword="null"/>。</exception>
        public static Task ExecuteWithRetries(
            Func<int, Task> action,
            int maxNumErrorTries,
            Func<Exception, int, bool>? retryExceptionFilter,
            TimeSpan maxExecutionTime,
            IBackoffProvider? onErrorBackOff)
        {
            ArgumentNullException.ThrowIfNull(action);

            async Task<bool> function(int i) { await action(i); return true; }
            return ExecuteWithRetriesHelper<bool>(
                function,
                0,
                maxNumErrorTries,
                maxExecutionTime,
                DateTime.UtcNow,
                null,
                retryExceptionFilter,
                null,
                onErrorBackOff);
        }

        /// <summary>
        /// 根据重试配置参数执行给定函数若干次。
        /// </summary>
        /// <param name="function">
        /// 要执行的委托。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 最大重试次数。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 重试异常筛选器。
        /// </param>
        /// <param name="maxExecutionTime">
        /// 最长执行时间。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 退避策略提供程序。
        /// </param>
        /// <param name="cancellationToken">
        /// 取消令牌。
        /// </param>
        /// <returns>
        /// 所提供函数成功调用后返回的值。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="function"/> 为 <see langword="null"/>。</exception>
        public static Task<T> ExecuteWithRetries<T>(
            Func<int, Task<T>> function,
            int maxNumErrorTries,
            Func<Exception, int, bool>? retryExceptionFilter,
            TimeSpan maxExecutionTime,
            IBackoffProvider? onErrorBackOff,
            CancellationToken cancellationToken = default)
        {
            return ExecuteWithRetries<T>(
                function,
                0,
                maxNumErrorTries,
                null,
                retryExceptionFilter,
                maxExecutionTime,
                null,
                onErrorBackOff,
                cancellationToken);
        }

        /// <summary>
        /// 根据重试配置参数执行给定的 <paramref name="function"/> 若干次。
        /// </summary>
        /// <typeparam name="T">
        /// <paramref name="function"/> 的底层返回类型。
        /// </typeparam>
        /// <param name="function">
        /// 要执行的函数。
        /// </param>
        /// <param name="maxNumSuccessTries">
        /// 成功执行尝试的最大次数。<see cref="ExecuteWithRetries"/> 会在给定的 <paramref name="function"/> 满足 <paramref name="retryValueFilter"/> 指定的重试条件时尝试再次执行。
        /// 设置为 <c>-1</c> 表示不限成功后的重试次数，直到满足 <paramref name="retryValueFilter"/>；设置为 <c>0</c> 表示只允许一次成功，此时 <paramref name="retryValueFilter"/> 将被
        /// 忽略，给定的 <paramref name="function"/> 仅执行到首次成功为止。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 因错误而执行的最大尝试次数。设置为 -1 表示不限错误重试次数，直到满足 <paramref name="retryExceptionFilter"/>。
        /// </param>
        /// <param name="retryValueFilter">
        /// 用于指示 <paramref name="function"/> 成功执行后是否重试的筛选器。设置为 <see langword="null"/> 可禁用成功后的重试。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 用于指示 <paramref name="function"/> 执行失败后是否重试的筛选器。设置为 <see langword="null"/> 可禁用错误重试。
        /// </param>
        /// <param name="maxExecutionTime">
        /// <see cref="ExecuteWithRetries"/> 函数的最长执行时间。
        /// </param>
        /// <param name="onSuccessBackOff">
        /// 退避策略提供程序对象，用于确定两次成功重试之间的等待时间。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 退避策略提供程序对象，用于确定两次错误重试之间的等待时间。
        /// </param>
        /// <param name="cancellationToken">
        /// 取消令牌。
        /// </param>
        /// <returns>
        /// <paramref name="function"/> 成功调用后返回的值。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="function"/> 为 <see langword="null"/>。</exception>
        public static Task<T> ExecuteWithRetries<T>(
            Func<int, Task<T>> function,
            int maxNumSuccessTries,
            int maxNumErrorTries,
            Func<T, int, bool>? retryValueFilter,
            Func<Exception, int, bool>? retryExceptionFilter,
            TimeSpan maxExecutionTime = default,
            IBackoffProvider? onSuccessBackOff = null,
            IBackoffProvider? onErrorBackOff = null,
            CancellationToken cancellationToken = default)
        {
            return ExecuteWithRetriesHelper<T>(
                function,
                maxNumSuccessTries,
                maxNumErrorTries,
                maxExecutionTime,
                DateTime.UtcNow,
                retryValueFilter,
                retryExceptionFilter,
                onSuccessBackOff,
                onErrorBackOff,
                cancellationToken);
        }

        /// <summary>
        /// 根据重试配置参数执行给定的 <paramref name="function"/> 若干次。
        /// </summary>
        /// <typeparam name="T">
        /// <paramref name="function"/> 的底层返回类型。
        /// </typeparam>
        /// <param name="function">
        /// 要执行的函数。
        /// </param>
        /// <param name="maxNumSuccessTries">
        /// 成功执行尝试的最大次数。<see cref="ExecuteWithRetries"/> 会在 <paramref name="function"/> 所示函数满足 <paramref name="retryValueFilter"/> 指定的重试条件时尝试再次执行该函数。
        /// 设置为 <c>-1</c> 表示不限成功后的重试次数，直到满足 <paramref name="retryValueFilter"/>；设置为 <c>0</c> 表示只允许一次成功，此时 <paramref name="retryValueFilter"/> 将被
        /// 忽略，给定的 <paramref name="function"/> 仅执行到首次成功为止。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 因错误而执行的最大尝试次数。设置为 -1 表示不限错误重试次数，直到满足 <paramref name="retryExceptionFilter"/>。
        /// </param>
        /// <param name="maxExecutionTime">
        /// <see cref="ExecuteWithRetries"/> 函数的最长执行时间。
        /// </param>
        /// <param name="startExecutionTime">
        /// 开始执行的时间。
        /// </param>
        /// <param name="retryValueFilter">
        /// 用于指示 <paramref name="function"/> 成功执行后是否重试的筛选器。设置为 <see langword="null"/> 可禁用成功后的重试。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 用于指示 <paramref name="function"/> 执行失败后是否重试的筛选器。设置为 <see langword="null"/> 可禁用错误重试。
        /// </param>
        /// <param name="onSuccessBackOff">
        /// 退避策略提供程序对象，用于确定两次成功重试之间的等待时间。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 退避策略提供程序对象，用于确定两次错误重试之间的等待时间。
        /// </param>
        /// <param name="cancellationToken">
        /// 取消令牌。
        /// </param>
        /// <returns>
        /// <paramref name="function"/> 成功调用后返回的值。
        /// </returns>
        private static async Task<T> ExecuteWithRetriesHelper<T>(
            Func<int, Task<T>> function,
            int maxNumSuccessTries,
            int maxNumErrorTries,
            TimeSpan maxExecutionTime,
            DateTime startExecutionTime,
            Func<T, int, bool>? retryValueFilter = null,
            Func<Exception, int, bool>? retryExceptionFilter = null,
            IBackoffProvider? onSuccessBackOff = null,
            IBackoffProvider? onErrorBackOff = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(function);

            T result = default!;
            ExceptionDispatchInfo? lastExceptionInfo = null;
            bool retry;
            var callCounter = 0;

            do
            {
                retry = false;
                cancellationToken.ThrowIfCancellationRequested();

                if (maxExecutionTime != Timeout.InfiniteTimeSpan && maxExecutionTime != default)
                {
                    DateTime now = DateTime.UtcNow;
                    if (now - startExecutionTime > maxExecutionTime)
                    {
                        if (lastExceptionInfo == null)
                        {
                            throw new TimeoutException(
                                $"ExecuteWithRetries has exceeded its max execution time of {maxExecutionTime}. Now is {LogFormatter.PrintDate(now)}, started at {LogFormatter.PrintDate(startExecutionTime)}, passed {now - startExecutionTime}");
                        }

                        lastExceptionInfo.Throw();
                    }
                }

                int counter = callCounter;

                try
                {
                    callCounter++;
                    result = await function(counter);
                    lastExceptionInfo = null;

                    if (callCounter < maxNumSuccessTries || maxNumSuccessTries == INFINITE_RETRIES) // -1 表示无限次重试。
                    {
                        if (retryValueFilter != null)
                            retry = retryValueFilter(result, counter);
                    }

                    if (retry && !cancellationToken.IsCancellationRequested)
                    {
                        TimeSpan? delay = onSuccessBackOff?.Next(counter);

                        if (delay.HasValue)
                        {
                            await Task.Delay(delay.Value, cancellationToken);
                        }
                    }
                }
                catch (Exception exc)
                {
                    retry = false;

                    if (callCounter < maxNumErrorTries || maxNumErrorTries == INFINITE_RETRIES)
                    {
                        if (retryExceptionFilter != null)
                            retry = retryExceptionFilter(exc, counter);
                    }

                    if (!retry || cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    lastExceptionInfo = ExceptionDispatchInfo.Capture(exc);

                    TimeSpan? delay = onErrorBackOff?.Next(counter);

                    if (delay.HasValue)
                    {
                        await Task.Delay(delay.Value, cancellationToken);
                    }
                }
            } while (retry);

            return result;
        }
    }

    /// <summary>
    /// 用于确定连续操作尝试之间等待时间的功能。
    /// </summary>
    public interface IBackoffProvider
    {
        /// <summary>
        /// 返回尝试后续操作之前需要等待的时间。
        /// </summary>
        /// <param name="attempt">已经进行的操作尝试次数。</param>
        /// <returns>尝试后续操作前需要等待的时间。</returns>
        TimeSpan Next(int attempt);
    }

    /// <summary>
    /// 固定时长退避实现，始终返回配置的延迟。
    /// </summary>
    public class FixedBackoff : IBackoffProvider
    {
        private readonly TimeSpan fixedDelay;

        /// <summary>
        /// 初始化 <see cref="FixedBackoff"/> 类的新实例。
        /// </summary>
        /// <param name="delay">
        /// 两次尝试之间的固定延迟。
        /// </param>
        public FixedBackoff(TimeSpan delay)
        {
            fixedDelay = delay;
        }

        /// <inheritdoc/>
        public TimeSpan Next(int attempt)
        {
            return fixedDelay;
        }
    }

    /// <summary>
    /// 指数退避实现，最初返回配置的最小延迟，
    /// 随后按 2 的尝试次数次幂指数级增加延迟，直到
    /// 达到最大退避延迟。
    /// </summary>
    internal class ExponentialBackoff : IBackoffProvider
    {
        private readonly TimeSpan minDelay;
        private readonly TimeSpan maxDelay;
        private readonly TimeSpan step;

        /// <summary>
        /// 初始化 <see cref="ExponentialBackoff"/> 类的新实例。
        /// </summary>
        /// <param name="minDelay">
        /// 最小延迟。
        /// </param>
        /// <param name="maxDelay">
        /// 最大延迟。
        /// </param>
        /// <param name="step">
        /// 步长：将其乘以 2 的尝试次数次幂，再加上最小延迟，即可计算每次迭代的延迟。
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// 一个或多个参数值超出有效范围。
        /// </exception>
        public ExponentialBackoff(TimeSpan minDelay, TimeSpan maxDelay, TimeSpan step)
        {
            if (minDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minDelay), minDelay, "ExponentialBackoff min delay must be a positive number.");
            if (maxDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxDelay), maxDelay, "ExponentialBackoff max delay must be a positive number.");
            if (step <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(step), step, "ExponentialBackoff step must be a positive number.");
            if (minDelay >= maxDelay) throw new ArgumentOutOfRangeException(nameof(minDelay), minDelay, "ExponentialBackoff min delay must be greater than max delay.");

            this.minDelay = minDelay;
            this.maxDelay = maxDelay;
            this.step = step;
        }

        /// <inheritdoc/>
        public TimeSpan Next(int attempt)
        {
            TimeSpan currMax;
            try
            {
                long multiple = checked(1 << attempt);
                currMax = minDelay + step.Multiply(multiple); // 可能引发 OverflowException。
                if (currMax <= TimeSpan.Zero)
                {
                    throw new OverflowException();
                }
            }
            catch (OverflowException)
            {
                currMax = maxDelay;
            }
            currMax = StandardExtensions.Min(currMax, maxDelay);

            if (minDelay >= currMax) throw new ArgumentOutOfRangeException($"minDelay {minDelay}, currMax = {currMax}");
            return RandomTimeSpan.Next(minDelay, currMax);
        }
    }
}
