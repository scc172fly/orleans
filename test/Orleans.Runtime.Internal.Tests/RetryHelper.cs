using System.Diagnostics;

namespace UnitTests
{
    /// <summary>
    /// 预设重试操作,用于 <see cref="RetryHelper"/>。
    /// </summary>
    /// <remarks>一种非常粗糙的系统 用于目前的测试</remarks>
    public static class RetryOperation
    {
        /// <summary>
        /// 产生小类形延迟曲线。
        /// </summary>
        /// <param name="retryAttempt">当前重试尝试。</param>
        /// <returns>下次尝试重试的延迟。</returns>
        /// <remarks>此函数未接受深查。</remarks>
        public static TimeSpan Sigmoid(int retryAttempt)
        {
            const int MaxDurationInSeconds = 50;
            return TimeSpan.FromMilliseconds(Convert.ToInt32(Math.Round((1 / (1 + Math.Exp(-retryAttempt + 3))) * MaxDurationInSeconds)) * 1000);
        }


        /// <summary>
        /// 产生线性延缓曲线,加量为10毫秒。
        /// </summary>
        /// <param name="retryAttempt">当前重试尝试。</param>
        /// <remarks>此函数未接受深查。</remarks>
        public static TimeSpan LinearWithTenMilliseconds(int retryAttempt)
        {
            return TimeSpan.FromMilliseconds(retryAttempt * 10);
        }
    }


    /// <summary>
    /// 简单的再试辅助器, 使测试更稳健 。
    /// </summary>
    internal static class RetryHelper
    {
        /// <summary>
        /// 用于根据参数重试操作的脚架功能。
        /// </summary>
        /// <typeparam name="TResult">要重试的函数的结果类型。</typeparam>
        /// <param name="maxAttempts">尝试重试的最大次数。</param>
        /// <param name="retryFunction">函数, 使用当前重试参数, 提供等待下次尝试的时间。</param>
        /// <param name="operation">重试操作。</param>
        /// <param name="cancellation">取消牌。</param>
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
