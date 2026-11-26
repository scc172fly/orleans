using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 记录连接问题的实用等级。
    /// 该系统是公开的,而不是内部的,因为它是落实其他一致性提供者的一个有用组成部分。
    /// </summary>
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This mutable coordinator tracks connection state and notifications rather than representing a comparable value.")]
    public struct RecordedConnectionIssue
    {
        /// <summary>
        /// 记录的连接问题,如果没有,则无效
        /// </summary>
        public ConnectionIssue? Issue { get; private set; }

        /// <summary>
        /// 记录连接问题,填写时间戳等。
        /// 通知听众
        /// </summary>
        /// <param name="newIssue">连接问题要记录</param>
        /// <param name="listener">连接问题收听器</param>
        /// <param name="services">记录收听器中例外报告</param>
        /// <exception cref="ArgumentNullException">所需论点是 <see langword="null"/>.</exception>
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
        /// 如果有记录,通知听众并澄清。
        /// </summary>
        /// <param name="listener">连接问题收听器</param>
        /// <param name="services">记录收听器中例外报告</param>
        /// <exception cref="ArgumentNullException">所需论点是 <see langword="null"/>.</exception>
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
        /// 如果在最后一次尝试中出现问题,则在重新试拖延规定的期限内出现延误
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
