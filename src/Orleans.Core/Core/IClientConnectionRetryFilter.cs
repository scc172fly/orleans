using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;

namespace Orleans
{
    /// <summary>
    /// 用于判断是否应重试集群连接的筛选器。
    /// </summary>
    public interface IClientConnectionRetryFilter
    {
        /// <summary>
        /// 返回一个值，指示是否应重新尝试连接 Orleans 集群。
        /// </summary>
        /// <param name="exception">上一次连接尝试引发的异常。</param>
        /// <param name="cancellationToken">用于通知连接已被外部中止的取消令牌。</param>
        /// <returns><see langword="true"/> 表示应重新尝试连接，<see langword="false"/> 表示应中止连接集群的尝试。</returns>
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
