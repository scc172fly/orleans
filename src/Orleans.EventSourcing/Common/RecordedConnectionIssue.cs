using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 用于记录连接问题的实用类。
    /// 此类是 public 而非 internal，因为它是实现其他一致性提供程序时有用的基础组件。
    /// </summary>
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This mutable coordinator tracks connection state and notifications rather than representing a comparable value.")]
    public struct RecordedConnectionIssue
    {
        /// <summary>
        /// 已记录的连接问题；如果没有则为 null。
        /// </summary>
        public ConnectionIssue? Issue { get; private set; }

        /// <summary>
        /// 记录连接问题，并填充时间戳等信息，
        /// 然后通知侦听器。
        /// </summary>
        /// <param name="newIssue">要记录的连接问题。</param>
        /// <param name="listener">连接问题的侦听器。</param>
        /// <param name="services">用于报告侦听器中发生的异常。</param>
        /// <exception cref="ArgumentNullException">必需的参数为 <see langword="null"/>。</exception>
        public void Record(ConnectionIssue newIssue, IConnectionIssueListener listener, ILogConsistencyProtocolServices services)
        {
            ArgumentNullException.ThrowIfNull(newIssue);
            ArgumentNullException.ThrowIfNull(listener);
            ArgumentNullException.ThrowIfNull(services);

            newIssue.TimeStamp = DateTime.UtcNow;
            if (Issue != null)
            {
                newIssue.TimeOfFirstFailure = Issue.TimeOfFirstFailure;
                newIssue.NumberOfConsecutiveFailures = Issue.NumberOfConsecutiveFailures + 1;
                newIssue.RetryDelay = newIssue.ComputeRetryDelay(Issue.RetryDelay);
            }
            else
            {
                newIssue.TimeOfFirstFailure = newIssue.TimeStamp;
                newIssue.NumberOfConsecutiveFailures = 1;
                newIssue.RetryDelay = newIssue.ComputeRetryDelay(null);
            }

            Issue = newIssue;

            try
            {
                listener.OnConnectionIssue(newIssue);
            }
            catch (Exception e)
            {
                services.CaughtUserCodeException("OnConnectionIssue", nameof(Record), e);
            }
        }

        /// <summary>
        /// 如果存在已记录的问题，则通知侦听器并清除该问题。
        /// </summary>
        /// <param name="listener">连接问题的侦听器。</param>
        /// <param name="services">用于报告侦听器中发生的异常。</param>
        /// <exception cref="ArgumentNullException">必需的参数为 <see langword="null"/>。</exception>
        public void Resolve(IConnectionIssueListener listener, ILogConsistencyProtocolServices services)
        {
            ArgumentNullException.ThrowIfNull(listener);
            ArgumentNullException.ThrowIfNull(services);

            if (Issue != null)
            {
                try
                {
                    listener.OnConnectionIssueResolved(Issue);
                }
                catch (Exception e)
                {
                    services.CaughtUserCodeException("OnConnectionIssueResolved", nameof(Resolve), e);
                }
                Issue = null;
            }
        }

        /// <summary>
        /// 如果上一次尝试出现问题，则按重试延迟所指定的时长等待。
        /// </summary>
        /// <returns></returns>
        public async readonly Task DelayBeforeRetry()
        {
            if (Issue == null)
                return;

            await Task.Delay(Issue.RetryDelay);
        }

        /// <inheritdoc/>
        public override readonly string? ToString()
        {
            if (Issue == null)
                return "";
            else
                return Issue.ToString();
        }
    }

}
