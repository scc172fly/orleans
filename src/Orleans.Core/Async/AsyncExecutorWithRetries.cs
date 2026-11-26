using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Internal
{
    /// <summary>
    /// 此类是一个方便的通用类, 用来执行某些与回溯( Restri) 相近函数,
    /// 允许指定自定义的再试过滤器和政策。
    /// </summary>
    public static class AsyncExecutorWithRetries
    {
        /// <summary>
        /// 用于要求无限次重试的常数。
        /// </summary>
        public static readonly int INFINITE_RETRIES = -1;

        /// <summary>
        /// 根据重试配置参数执行给定函数多次 。
        /// </summary>
        /// <param name="action">
        /// 要执行的动作 。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 最大重试次数。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 重试例外过滤器 。
        /// </param>
        /// <param name="maxExecutionTime">
        /// 最长执行时间。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 后退供应商
        /// </param>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 是 <see langword="null"/>.</exception>
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
        /// 根据重试配置参数执行给定函数多次 。
        /// </summary>
        /// <param name="function">
        /// 执行代表。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 最大重试次数。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 重试例外过滤器 。
        /// </param>
        /// <param name="maxExecutionTime">
        /// 最长执行时间。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 后退供应商
        /// </param>
        /// <param name="cancellationToken">
        /// 取消标记 。
        /// </param>
        /// <returns>
        /// 成功援引所提供的函数后返回的值。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="function"/> 是 <see langword="null"/>.</exception>
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
        /// 根据重试配置参数执行给定 <paramref name="function"/> 多次。
        /// </summary>
        /// <typeparam name="T">
        /// 基本返回类型 <paramref name="function"/>。
        /// </typeparam>
        /// <param name="function">
        /// 要执行的函数
        /// </param>
        /// <param name="maxNumSuccessTries">
        /// 成功执行尝试的最大数量。 <see cref="ExecuteWithRetries"/> 将尝试重新执行给定 <paramref name="function"/> 如经指示,如经 <paramref name="retryValueFilter"/> .
        /// 设置为 <c>-1</c> ,用于不限制数量的成功重试,直到满足 <paramref name="retryValueFilter"/> 。设为 <c>0</c> ,仅一次成功尝试,这将导致 <paramref name="retryValueFilter"/>
        /// 被忽略,在第一次成功之前只执行过一次给定的 <paramref name="function"/> 。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 由于错误而执行尝试的最大次数。 设置为 - 1, 用于无限的错误重写, 直到 <paramref name="retryExceptionFilter"/> 得到满足 。
        /// </param>
        /// <param name="retryValueFilter">
        /// 过滤器 <paramref name="function"/> 以显示是否要重新尝试成功执行。 设置为 <see langword="null"/> 以禁用成功重试 。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 过滤器 <paramref name="function"/> 以表明是否应该重新检查错误执行。 设置为 <see langword="null"/> 以禁用错误重写 。
        /// </param>
        /// <param name="maxExecutionTime">
        /// <see cref="ExecuteWithRetries"/> 函数的最大执行时间。
        /// </param>
        /// <param name="onSuccessBackOff">
        /// 后端提供者对象, 它决定成功重试之间要等待多少时间 。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 后卸提供者对象, 确定错误重试之间要等多少
        /// </param>
        /// <param name="cancellationToken">
        /// 取消标记 。
        /// </param>
        /// <returns>
        /// 成功援引 <paramref name="function"/>后返回的值。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="function"/> 是 <see langword="null"/>.</exception>
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
        /// 根据重试配置参数执行给定 <paramref name="function"/> 多次。
        /// </summary>
        /// <typeparam name="T">
        /// 基本返回类型 <paramref name="function"/>。
        /// </typeparam>
        /// <param name="function">
        /// 要执行的功能 。
        /// </param>
        /// <param name="maxNumSuccessTries">
        /// 成功执行尝试的最大数量。 <see cref="ExecuteWithRetries"/> 将尝试重新执行给定 <paramref name="function"/> 如经指示,如经 <paramref name="retryValueFilter"/> .
        /// 设置为 <c>-1</c> ,用于不限制数量的成功重试,直到满足 <paramref name="retryValueFilter"/> 。设为 <c>0</c> ,仅一次成功尝试,这将导致 <paramref name="retryValueFilter"/>
        /// 被忽略,在第一次成功之前只执行过一次给定的 <paramref name="function"/> 。
        /// </param>
        /// <param name="maxNumErrorTries">
        /// 由于错误而执行尝试的最大次数。 设置为 - 1, 用于无限的错误重写, 直到 <paramref name="retryExceptionFilter"/> 得到满足 。
        /// </param>
        /// <param name="maxExecutionTime">
        /// <see cref="ExecuteWithRetries"/> 函数的最大执行时间。
        /// </param>
        /// <param name="startExecutionTime">
        /// 处决开始的时间。
        /// </param>
        /// <param name="retryValueFilter">
        /// 过滤器 <paramref name="function"/> 以显示是否要重新尝试成功执行。 设置为 <see langword="null"/> 以禁用成功重试 。
        /// </param>
        /// <param name="retryExceptionFilter">
        /// 过滤器 <paramref name="function"/> 以表明是否应该重新检查错误执行。 设置为 <see langword="null"/> 以禁用错误重写 。
        /// </param>
        /// <param name="onSuccessBackOff">
        /// 后端提供者对象, 它决定成功重试之间要等待多少时间 。
        /// </param>
        /// <param name="onErrorBackOff">
        /// 后卸提供者对象, 确定错误重试之间要等多少
        /// </param>
        /// <param name="cancellationToken">
        /// 取消标记 。
        /// </param>
        /// <returns>
        /// 成功援引 <paramref name="function"/>后返回的值。
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

                    if (callCounter < maxNumSuccessTries || maxNumSuccessTries == INFINITE_RETRIES) // - 无限重试的 1
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
    /// 确定连续操作尝试之间要等多久的职能。
    /// </summary>
    public interface IBackoffProvider
    {
        /// <summary>
        /// 返回在尝试后续操作前等待的时间。
        /// </summary>
        /// <param name="attempt">企图行动的次数。</param>
        /// <returns>尝试随后的操作前等待的时间。</returns>
        TimeSpan Next(int attempt);
    }

    /// <summary>
    /// 固定期限后卸执行, 总是返回配置的延迟 。
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
    /// 指数反向后卸执行, 最初返回配置最小延迟的初始值
    /// 并迅速增加其延迟时间, 两次升到尝试号的能量
    /// 最大回撤延迟已达。
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
        /// 最小的延迟。
        /// </param>
        /// <param name="maxDelay">
        /// 最大延迟时间。
        /// </param>
        /// <param name="step">
        /// 将该步骤乘以两次,再加到尝试号的功率上,并加上最短的延迟,以计算每次迭代的延迟。
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// 一个或多个参数值超出其有效范围。
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
                currMax = minDelay + step.Multiply(multiple); // 键可投 OverflowException
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
