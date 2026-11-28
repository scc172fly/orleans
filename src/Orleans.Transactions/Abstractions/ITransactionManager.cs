
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 协调事务的准备与提交协议。
    /// </summary>
    public interface ITransactionManager
    {
        /// <summary>
        /// 由事务代理（TA）发送给事务管理器（TM）的请求。TM 在提交或中止事务后作出响应。
        /// </summary>
        /// <param name="transactionId">要准备的事务标识符。</param>
        /// <param name="accessCount">该事务对当前参与者执行的读写次数。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <param name="writerResources">在事务期间执行过写入的参与者。</param>
        /// <param name="totalParticipants">事务参与者总数。</param>
        /// <returns>其结果为最终事务状态的任务。</returns>
        Task<TransactionalStatus> PrepareAndCommit(Guid transactionId, AccessCounter accessCount, DateTime timeStamp,
            List<ParticipantId> writerResources, int totalParticipants);

        /// <summary>
        /// 准备并提交事务。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="accessCount">该事务执行的资源访问次数。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="writerResources">在事务期间执行过写入的参与者。</param>
        /// <param name="totalParticipants">参与者总数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>事务状态。</returns>
        Task<TransactionalStatus> PrepareAndCommit(
            Guid transactionId,
            AccessCounter accessCount,
            DateTime timeStamp,
            List<ParticipantId> writerResources,
            int totalParticipants,
            CancellationToken cancellationToken)
            => PrepareAndCommit(transactionId, accessCount, timeStamp, writerResources, totalParticipants);

        /// <summary>
        /// 参与者完成准备（无论成功还是失败）后向 TM 发送的单向消息。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <param name="resource">报告准备结果的参与者。</param>
        /// <param name="status">参与者的准备结果。</param>
        /// <returns>表示该操作的任务。</returns>
        Task Prepared(Guid transactionId, DateTime timeStamp, ParticipantId resource, TransactionalStatus status);

        /// <summary>
        /// 报告某个参与者已完成准备。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="resource">报告方参与者。</param>
        /// <param name="status">准备状态。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Prepared(Guid transactionId, DateTime timeStamp, ParticipantId resource, TransactionalStatus status, CancellationToken cancellationToken)
            => Prepared(transactionId, timeStamp, resource, status);

        /// <summary>
        /// 参与者向 TM 发送的单向消息，告知 TM 自身仍在等待
        /// 该事务的最终结果。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">事务提交时间戳。</param>
        /// <param name="resource">正在等待事务结果的参与者。</param>
        /// <returns>表示该操作的任务。</returns>
        Task Ping(Guid transactionId, DateTime timeStamp, ParticipantId resource);

        /// <summary>
        /// 报告某个参与者正在等待事务结果。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timeStamp">提交时间戳。</param>
        /// <param name="resource">报告方参与者。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Ping(Guid transactionId, DateTime timeStamp, ParticipantId resource, CancellationToken cancellationToken)
            => Ping(transactionId, timeStamp, resource);
    }

    /// <summary>
    /// 统计对事务参与者的读写访问次数。
    /// </summary>
    [GenerateSerializer]
    [Serializable]
    public struct AccessCounter : IEquatable<AccessCounter>
    {
        /// <summary>
        /// 读取访问次数。
        /// </summary>
        [Id(0)]
        public int Reads;

        /// <summary>
        /// 写入访问次数。
        /// </summary>
        [Id(1)]
        public int Writes;

        /// <inheritdoc/>
        public readonly bool Equals(AccessCounter other) => Reads == other.Reads && Writes == other.Writes;

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj) => obj is AccessCounter other && Equals(other);

        /// <inheritdoc/>
        public override readonly int GetHashCode() => HashCode.Combine(Reads, Writes);

        /// <summary>
        /// 确定两个访问计数是否相等。
        /// </summary>
        public static bool operator ==(AccessCounter left, AccessCounter right) => left.Equals(right);

        /// <summary>
        /// 确定两个访问计数是否不相等。
        /// </summary>
        public static bool operator !=(AccessCounter left, AccessCounter right) => !left.Equals(right);

        /// <summary>
        /// 将两个值中的读计数和写计数分别相加。
        /// </summary>
        /// <param name="c1">第一个访问计数。</param>
        /// <param name="c2">第二个访问计数。</param>
        /// <returns>合并后的访问计数。</returns>
        public static AccessCounter operator +(AccessCounter c1, AccessCounter c2)
        {
            return new AccessCounter { Reads = c1.Reads + c2.Reads, Writes = c1.Writes + c2.Writes };
        }
    }
}


