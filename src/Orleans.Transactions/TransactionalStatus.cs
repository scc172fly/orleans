
using System;

namespace Orleans.Transactions
{

    /// <summary>
    /// 描述交易协议运作的结果。
    /// </summary>
    public enum TransactionalStatus
    {
        /// <summary>
        /// 操作成功完成 。
        /// </summary>
        Ok,

        /// <summary>
        /// 交易经理没有在最后期限之前完成准备阶段。
        /// </summary>
        PrepareTimeout,

        /// <summary>
        /// 交易中止是因为它所依赖的交易中断。
        /// </summary>
        CascadingAbort,

        /// <summary>
        /// 由于超时、货币仲裁或失败,交易失去了锁。
        /// </summary>
        BrokenLock,

        /// <summary>
        /// 执行期间记录的出入证与准备期间锁的锁不匹配。
        /// </summary>
        LockValidationFailed,

        /// <summary>
        /// 交易代理商超时等待只读交易参与者。
        /// </summary>
        ParticipantResponseTimeout,

        /// <summary>
        /// 交易代理商超时等待交易经理
        /// </summary>
        TMResponseTimeout,

        /// <summary>
        /// 交易存储被一个竞争性的 grain 激活修改。
        /// </summary>
        StorageConflict,

        /// <summary>
        /// 交易经理没有交易记录,因此推定该交易被中止。
        /// </summary>
        PresumedAbort,

        /// <summary>
        /// 未分类的例外中断交易协议。
        /// </summary>
        UnknownException,

        /// <summary>
        /// 内部交易协议的主张失败 。
        /// </summary>
        AssertionFailed,

        /// <summary>
        /// 无法进行交易。
        /// </summary>
        CommitFailure,
    }

    /// <summary>
    /// 提供解释交易状况的业务。
    /// </summary>
    public static class TransactionalStatusExtensions
    {
        /// <summary>
        /// 确定状态是否保证交易中止。
        /// </summary>
        /// <param name="status">交易状况。</param>
        /// <returns>
        /// <see langword="true"/> ,如果已知交易已经中止;否则, <see langword="false"/>。
        /// </returns>
        public static bool DefinitelyAborted(this TransactionalStatus status)
        {
            switch (status)
            {
                case TransactionalStatus.PrepareTimeout:
                case TransactionalStatus.CascadingAbort:
                case TransactionalStatus.BrokenLock:
                case TransactionalStatus.LockValidationFailed:
                case TransactionalStatus.ParticipantResponseTimeout:
                case TransactionalStatus.CommitFailure:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 创建以故障状态为代表的用户端交易例外。
        /// </summary>
        /// <param name="status">交易失败状态</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="exception">造成失败的例外,如果有的话。</param>
        /// <returns>描述交易结果的例外。</returns>
        public static OrleansTransactionException ConvertToUserException(this TransactionalStatus status, string transactionId, Exception? exception)
        {
            switch (status)
            {
                case TransactionalStatus.PrepareTimeout:
                    return new OrleansTransactionPrepareTimeoutException(transactionId, exception);

                case TransactionalStatus.CascadingAbort:
                    return new OrleansCascadingAbortException(transactionId, exception);

                case TransactionalStatus.BrokenLock:
                    return new OrleansBrokenTransactionLockException(transactionId, "before prepare", exception);

                case TransactionalStatus.LockValidationFailed:
                    return new OrleansBrokenTransactionLockException(transactionId, "when validating accesses during prepare", exception);

                case TransactionalStatus.ParticipantResponseTimeout:
                    return new OrleansTransactionTransientFailureException(transactionId, $"transaction agent timed out waiting for read-only transaction participant responses ({status})", exception);

                case TransactionalStatus.TMResponseTimeout:
                    return new OrleansTransactionInDoubtException(transactionId, $"transaction agent timed out waiting for read-only transaction participant responses ({status})", exception);

                case TransactionalStatus.CommitFailure:
                    return new OrleansTransactionAbortedException(transactionId, $"Unable to commit transaction ({status})", exception);

                default:
                    return new OrleansTransactionInDoubtException(transactionId, $"failure during transaction commit, status={status}", exception);
            }
        }
    }
}
