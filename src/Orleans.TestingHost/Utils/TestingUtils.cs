using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost.Logging;

namespace Orleans.TestingHost.Utils
{
    /// <summary> 收集测试用设施 </summary>
    public static class TestingUtils
    {
        private static long uniquifier = Stopwatch.GetTimestamp();

        /// <summary>
        /// 配置配置 <paramref name="builder" /> 具有 a 的 <see cref="FileLoggerProvider" /> 日志的日志 <paramref name="filePath" />
        /// 违约;
        /// </summary>
        /// <param name="builder">建筑工。</param>
        /// <param name="filePath">文件路径。</param>
        public static void ConfigureDefaultLoggingBuilder(ILoggingBuilder builder, string filePath)
        {
            builder.AddFile(filePath);
        }

        /// <summary>
        /// 在特定部署中为特定节点或客户端创建跟踪文件名
        /// </summary>
        /// <param name="nodeName">节点的名称。</param>
        /// <param name="clusterId">集束识别符号。</param>
        /// <returns>新的追踪文件名称。</returns>
        public static string CreateTraceFileName(string nodeName, string clusterId)
        {
            const string traceFileFolder = "logs";

            if (!Directory.Exists(traceFileFolder))
            {
                Directory.CreateDirectory(traceFileFolder);
            }

            var traceFileName = Path.Combine(traceFileFolder, $"{clusterId}_{Interlocked.Increment(ref uniquifier):X}_{nodeName}.log");

            return traceFileName;
        }

        /// <summary>
        /// 创建默认的日志工厂, 该工厂将配置一个 <see cref="FileLoggerProvider" /> 记录厂, 它将将日志写到 <paramref name="filePath" /> 和控台。
        /// 违约;
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <returns>ILoggerFactory.</returns>
        public static ILoggerFactory CreateDefaultLoggerFactory(string filePath)
        {
            return CreateDefaultLoggerFactory(filePath, new LoggerFilterOptions());
        }

        /// <summary>
        /// 创建默认的日志工厂, 该工厂将配置一个 <see cref="FileLoggerProvider"/> 记录厂, 它将将日志写到 <paramref name="filePath"/> 和控台。
        /// 违约;
        /// </summary>
        /// <param name="filePath">日志器文件路径</param>
        /// <param name="filters">日志过滤器</param>
        /// <returns></returns>
        public static ILoggerFactory CreateDefaultLoggerFactory(string filePath, LoggerFilterOptions filters)
        {
            var factory = new LoggerFactory(new List<ILoggerProvider>(), filters);
            try
            {
#pragma warning disable CA2000 // LoggerFactory.AddProvider owns the provider; the factory is disposed before construction failures propagate.
                factory.AddProvider(new FileLoggerProvider(filePath));
#pragma warning restore CA2000
                return factory;
            }
            catch
            {
                factory.Dispose();
                throw;
            }
        }

        /// <summary>运行上游直到它成功或超时。</summary>
        /// <param name="predicate">所运行的上游</param>
        /// <param name="timeout">超时值</param>
        /// <param name="delayOnFail">下次对失败通知的延迟时间</param>
        /// <returns>代表操作的任务。</returns>
        /// <exception cref="TimeoutException">延时之前,上游没有成功。</exception>
        public static Task WaitUntilAsync(Func<bool, Task<bool>> predicate, TimeSpan timeout, TimeSpan? delayOnFail = null)
            => WaitUntilAsync(predicate, timeout, delayOnFail, TimeProvider.System);

        internal static Task WaitUntilAsync(
            Func<bool, Task<bool>> predicate,
            TimeSpan timeout,
            TimeSpan? delayOnFail,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(timeProvider);

            var predicateName = $"{predicate.Method.DeclaringType?.FullName ?? "<unknown>"}.{predicate.Method.Name}";
            return WaitUntilAsync((lastTry, _) => predicate(lastTry), timeout, delayOnFail, default, timeProvider, predicateName);
        }

        /// <summary>运行上游直到它成功或超时。</summary>
        /// <param name="predicate">
        /// 第一个论点表明最后的尝试,允许上游提出详细的断言失败。
        /// 当最后期限到期或请求取消时, 命令会被取消 。
        /// </param>
        /// <param name="timeout">超时值。</param>
        /// <param name="delayOnFail">尝试失败后重新尝试前的延误。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <param name="predicateExpression">提供给 <paramref name="predicate"/>的表达式。</param>
        /// <returns>代表操作的任务。</returns>
        /// <exception cref="TimeoutException">延时之前,上游没有成功。</exception>
        public static Task WaitUntilAsync(
            Func<bool, CancellationToken, Task<bool>> predicate,
            TimeSpan timeout,
            TimeSpan? delayOnFail = null,
            CancellationToken cancellationToken = default,
            [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
            => WaitUntilAsync(predicate, timeout, delayOnFail, cancellationToken, TimeProvider.System, predicateExpression);

        internal static async Task WaitUntilAsync(
            Func<bool, CancellationToken, Task<bool>> predicate,
            TimeSpan timeout,
            TimeSpan? delayOnFail,
            CancellationToken cancellationToken,
            TimeProvider timeProvider,
            [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(timeProvider);

            if (!await WaitUntilSucceededAsync(
                predicate,
                timeout,
                delayOnFail,
                cancellationToken,
                invokeFinalAttempt: true,
                timeProvider))
            {
                throw new TimeoutException(
                    $"The condition evaluated by '{predicateExpression ?? "<unknown>"}' was not satisfied within {timeout} "
                    + $"using a retry delay of {delayOnFail ?? TimeSpan.FromSeconds(1)}. "
                    + "The predicate was not invoked again after the deadline.");
            }
        }

        /// <summary>运行上游,直到其成功或单声道最后期限到期。</summary>
        /// <param name="predicate">要运行的上游。当最后期限到期或请求取消时,该符号将被取消 。</param>
        /// <param name="timeout">超时值。</param>
        /// <param name="delayOnFail">尝试失败后重新尝试前的延误。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns><see langword="true"/> 如果在最后期限之前成功,则该上游;否则, <see langword="false"/>。</returns>
        public static async Task<bool> WaitUntilSucceededAsync(
            Func<CancellationToken, Task<bool>> predicate,
            TimeSpan timeout,
            TimeSpan? delayOnFail = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return await WaitUntilSucceededAsync(predicate, timeout, delayOnFail, cancellationToken, TimeProvider.System);
        }

        internal static async Task<bool> WaitUntilSucceededAsync(
            Func<CancellationToken, Task<bool>> predicate,
            TimeSpan timeout,
            TimeSpan? delayOnFail,
            CancellationToken cancellationToken,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(timeProvider);
            return await WaitUntilSucceededAsync(
                (_, token) => predicate(token),
                timeout,
                delayOnFail,
                cancellationToken,
                invokeFinalAttempt: false,
                timeProvider);
        }

        private static async Task<bool> WaitUntilSucceededAsync(
            Func<bool, CancellationToken, Task<bool>> predicate,
            TimeSpan timeout,
            TimeSpan? delayOnFail,
            CancellationToken cancellationToken,
            bool invokeFinalAttempt,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);

            var retryDelay = delayOnFail ?? TimeSpan.FromSeconds(1);
            ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = timeProvider.GetTimestamp();
            using var deadlineCancellation = new CancellationTokenSource(timeout, timeProvider);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineCancellation.Token);
            var finalAttempt = false;
            var finalAttemptWindow = retryDelay > TimeSpan.Zero ? retryDelay : TimeSpan.FromMilliseconds(10);

            while (timeProvider.GetElapsedTime(startedAt) < timeout)
            {
                if (deadlineCancellation.IsCancellationRequested || timeProvider.GetElapsedTime(startedAt) >= timeout)
                {
                    return false;
                }

                bool succeeded;
                try
                {
                    succeeded = await predicate(finalAttempt, linkedCancellation.Token);
                }
                catch (OperationCanceledException) when (deadlineCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var elapsed = timeProvider.GetElapsedTime(startedAt);
                if (elapsed >= timeout)
                {
                    return false;
                }

                if (succeeded)
                {
                    return true;
                }

                var remaining = timeout - elapsed;
                if (finalAttempt)
                {
                    try
                    {
                        await Task.Delay(remaining, timeProvider, linkedCancellation.Token);
                    }
                    catch (OperationCanceledException) when (deadlineCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    return false;
                }

                if (invokeFinalAttempt && remaining <= finalAttemptWindow + retryDelay)
                {
                    finalAttempt = true;
                    continue;
                }

                var delay = retryDelay < remaining ? retryDelay : remaining;
                if (invokeFinalAttempt)
                {
                    var delayUntilFinalAttempt = remaining - finalAttemptWindow;
                    if (delayUntilFinalAttempt < delay)
                    {
                        delay = delayUntilFinalAttempt;
                    }
                }

                try
                {
                    await Task.Delay(delay, timeProvider, linkedCancellation.Token);
                }
                catch (OperationCanceledException) when (deadlineCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (delay >= remaining)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 乘以一个值的超时
        /// </summary>
        /// <param name="time">时间。</param>
        /// <param name="value">数值。</param>
        /// <returns>由此得出的时间跨值。</returns>
        public static TimeSpan Multiply(TimeSpan time, double value)
        {
            double ticksD = checked(time.Ticks * value);
            long ticks = checked((long)ticksD);
            return TimeSpan.FromTicks(ticks);
        }
    }
}
