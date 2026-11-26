
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 协调交易协议的准备和履行。
    /// </summary>
    public interface ITransactionManager
    {
        /// <summary>
        /// TM在交易实施或中止后作出反应。
        /// </summary>
        /// <param name="transactionId">要准备的交易标识符。</param>
        /// <param name="accessCount">交易中对该参与者的读数和写数。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <param name="writerResources">交易期间写作的参与者。</param>
        /// <param name="totalParticipants">交易参与者总数。</param>
        /// <returns>其结果是最终交易状况。</returns>
        Task<TransactionalStatus> PrepareAndCommit(Guid transactionId, AccessCounter accessCount, DateTime timeStamp,
            List<ParticipantId> writerResources, int totalParticipants);

        /// <summary>
        /// 准备并进行交易。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易所获取的资源数量。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="writerResources">交易期间写作的参与者。</param>
        /// <param name="totalParticipants">参加者总数。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>交易状况。</returns>
        Task<TransactionalStatus> PrepareAndCommit(
            Guid transactionId,
            AccessCounter accessCount,
            DateTime timeStamp,
            List<ParticipantId> writerResources,
            int totalParticipants,
            CancellationToken cancellationToken)
            => PrepareAndCommit(transactionId, accessCount, timeStamp, writerResources, totalParticipants);

        /// <summary>
        /// 由参加方在TM会议(成功或失败)准备后向TM会议发送的单向信息。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <param name="resource">参与者报告其准备结果。</param>
        /// <param name="status">参与者的准备结果</param>
        /// <returns>代表操作的任务。</returns>
        Task Prepared(Guid transactionId, DateTime timeStamp, ParticipantId resource, TransactionalStatus status);

        /// <summary>
        /// 一位与会者编制的报告。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="resource">提交报告的参与者。</param>
        /// <param name="status">准备状态</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Prepared(Guid transactionId, DateTime timeStamp, ParticipantId resource, TransactionalStatus status, CancellationToken cancellationToken)
            => Prepared(transactionId, timeStamp, resource, status);

        /// <summary>
        /// 向TMT发出单向信息,让TMT知道他们还在等待听到
        /// 交易的命运。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">交易承诺时间戳。</param>
        /// <param name="resource">等待交易结果的参与人。</param>
        /// <returns>代表操作的任务。</returns>
        Task Ping(Guid transactionId, DateTime timeStamp, ParticipantId resource);

        /// <summary>
        /// 据报,一名参与人正在等待交易结果。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="resource">提交报告的参与者。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Ping(Guid transactionId, DateTime timeStamp, ParticipantId resource, CancellationToken cancellationToken)
            => Ping(transactionId, timeStamp, resource);
    }

    /// <summary>
    /// 计数交易参与者的读写权限。
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
        /// 写入权限数 。
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
        /// 确定两个访问计数器是否相等。
        /// </summary>
        public static bool operator ==(AccessCounter left, AccessCounter right) => left.Equals(right);

        /// <summary>
        /// 确定两个接入柜台是否不平等。
        /// </summary>
        public static bool operator !=(AccessCounter left, AccessCounter right) => !left.Equals(right);

        /// <summary>
        /// 从两个值中添加读写计数。
        /// </summary>
        /// <param name="c1">第一个访问计数。</param>
        /// <param name="c2">第二个访问计数。</param>
        /// <returns>合并访问计数。</returns>
        public static AccessCounter operator +(AccessCounter c1, AccessCounter c2)
        {
            return new AccessCounter { Reads = c1.Reads + c2.Reads, Writes = c1.Writes + c2.Writes };
        }
    }
}


