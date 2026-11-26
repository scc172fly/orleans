using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 使一个组件成为交易参与者的接口。
    /// </summary>
    public interface ITransactionalResource
    {
        /// <summary>
        /// 技术援助局向只读交易(一阶段承诺)的所有参与者发出请求。 
        /// 参与者在承诺或中止阅读后作出答复。
        /// </summary>
        /// <param name="transactionId">准备交易的编号</param>
        /// <param name="accessCount">交易中该参与者的读数/写数</param>
        /// <param name="timeStamp">交易的承付时间戳</param>
        /// <returns>其结果是资源报告的交易状况。</returns>
        Task<TransactionalStatus> CommitReadOnly(Guid transactionId, AccessCounter accessCount, DateTime timeStamp);

        /// <summary>
        /// 提交只读交易。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易所获取的资源数量。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>交易状况。</returns>
        Task<TransactionalStatus> CommitReadOnly(Guid transactionId, AccessCounter accessCount, DateTime timeStamp, CancellationToken cancellationToken)
            => CommitReadOnly(transactionId, accessCount, timeStamp);

        /// <summary>
        /// TA向所有与会者发送单向信息,TM除外。  
        /// </summary>
        /// <param name="transactionId">准备交易的编号</param>
        /// <param name="accessCount">交易中该参与者的读数/写数</param>
        /// <param name="timeStamp">交易的承付时间戳</param>
        /// <param name="transactionManager">本交易的交易管理人</param>
        /// <returns>代表操作的任务。</returns>
        Task Prepare(Guid transactionId, AccessCounter accessCount,
            DateTime timeStamp, ParticipantId transactionManager);

        /// <summary>
        /// 准备交易
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="accessCount">交易所获取的资源数量。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="transactionManager">交易经理</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Prepare(Guid transactionId, AccessCounter accessCount, DateTime timeStamp, ParticipantId transactionManager, CancellationToken cancellationToken)
            => Prepare(transactionId, accessCount, timeStamp, transactionManager);

        /// <summary>
        /// 技术管理局向参与者发送单向信息,让他们知道交易已经中止。
        /// </summary>
        /// <param name="transactionId">中止交易的代号</param>
        Task Abort(Guid transactionId);

        /// <summary>
        /// 中止交易。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Abort(Guid transactionId, CancellationToken cancellationToken) => Abort(transactionId);

        /// <summary>
        /// TM向参与者发送单向信息,让他们知道交易已经中止。
        /// </summary>
        /// <param name="transactionId">中止交易的代号</param>
        /// <param name="timeStamp">中止交易的承付时间戳</param>
        /// <param name="status">中止原因</param>
        Task Cancel(Guid transactionId, DateTime timeStamp, TransactionalStatus status);

        /// <summary>
        /// 取消交易。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="status">取消状态。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Cancel(Guid transactionId, DateTime timeStamp, TransactionalStatus status, CancellationToken cancellationToken)
            => Cancel(transactionId, timeStamp, status);

        /// <summary>
        /// TM向与会者发出要求,请他们知道已进行的交易。
        /// 与会者在清理所有准备记录后作出回应。
        /// </summary>
        /// <param name="transactionId">承诺交易的代号</param>
        /// <param name="timeStamp">承诺交易的承付时间戳</param>
        Task Confirm(Guid transactionId, DateTime timeStamp);

        /// <summary>
        /// 确认交易
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timeStamp">承诺时间戳。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Confirm(Guid transactionId, DateTime timeStamp, CancellationToken cancellationToken)
            => Confirm(transactionId, timeStamp);
    }
}
