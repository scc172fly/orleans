using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Orleans
{
    /// <summary>
    /// 用于确定是否要重新尝试集连接的过滤器 。
    /// </summary>
    public interface IClientConnectionRetryFilter
    {
        /// <summary>
        /// 返回一个数值,表示是否应当重新尝试连接到 Orleans 组。
        /// </summary>
        /// <param name="exception">上次试图连接时的例外。</param>
        /// <param name="cancellationToken">外部中断连接时用来通知连接的取消牌。</param>
        /// <returns><see langword="true"/> 如果连接应重新尝试, <see langword="false"/> 如果试图连接到该组的尝试应该中止 。</returns>
        Task<bool> ShouldRetryConnectionAttempt(Exception exception, CancellationToken cancellationToken);
    }

    internal sealed class LinearBackoffClientConnectionRetryFilter : IClientConnectionRetryFilter
    {
        private const int MaxRetry = 15;
        private const int Delay = 1_500;
        private int _retryCount;

        public async Task<bool> ShouldRetryConnectionAttempt(
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (_retryCount >= MaxRetry)
            {
                return false;
            }

            if (!cancellationToken.IsCancellationRequested && exception is OrleansMessageRejectionException or ConnectionFailedException)
            {
                ++_retryCount;
                var currentDelay = TimeSpan.FromMilliseconds(Delay * _retryCount);
                await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }
    }
}
