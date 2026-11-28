using System.Diagnostics;

namespace UnitTests
{
    /// <summary>
    /// 预先确定的重试操作，与 <see cref="RetryHelper"/> 一起使用。
    /// </summary>
    /// <remarks>当前测试使用的一套非常简易的机制。</remarks>
    public static class RetryOperation
    {
        /// <summary>
        /// 生成 S 形延迟曲线。
        /// </summary>
        /// <param name="retryAttempt">当前重试次数。</param>
        /// <returns>下次重试之前的延迟。</returns>
        /// <remarks>此函数尚未经深入审查。</remarks>
        public static TimeSpan Sigmoid(int retryAttempt)
        {
            const int MaxDurationInSeconds = 50;
            return TimeSpan.FromMilliseconds(Convert.ToInt32(Math.Round((1 / (1 + Math.Exp(-retryAttempt + 3))) * MaxDurationInSeconds)) * 1000);
        }


        /// <summary>
        /// 生成以 10 ms 为增量的线性延迟曲线。
        /// </summary>
        /// <param name="retryAttempt">当前重试次数。</param>
        /// <remarks>此函数尚未经深入审查。</remarks>
        public static TimeSpan LinearWithTenMilliseconds(int retryAttempt)
        {
            return TimeSpan.FromMilliseconds(retryAttempt * 10);
        }
    }


    /// <summary>
    /// 用于增强测试稳健性的简单重试辅助类。
    /// </summary>
    internal static class RetryHelper
    {
        /// <summary>
        /// 根据参数重试操作的辅助框架函数。
        /// </summary>
        /// <typeparam name="TResult">待重试函数的结果类型。</typeparam>
        /// <param name="maxAttempts">最大重试次数。</param>
        /// <param name="retryFunction">接收当前重试参数并提供下次尝试前等待时间的函数。</param>
        /// <param name="operation">待重试的操作。</param>
        /// <param name="cancellation">取消令牌。</param>
        /// <returns>重试的结果。</returns>
        internal static async Task<TResult> RetryOnExceptionAsync<TResult>(int maxAttempts, Func<int, TimeSpan> retryFunction, Func<Task<TResult>> operation, CancellationToken cancellation = default)
        {
            const int MinAttempts = 1;
            if (maxAttempts <= MinAttempts)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), $"The count of {maxAttempts} needs to be at least {MinAttempts}.");
            }

            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var attempts = 0;
            while (true)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();

                    attempts++;
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (attempts == maxAttempts)
                    {
                        throw;
                    }

                    Trace.WriteLine(ex);
                    await Task.Delay(retryFunction(attempts), cancellation).ConfigureAwait(false);
                }
            }
        }
    }
}
