
using System;

namespace Orleans.Transactions
{

    /// <summary>
    /// 描述事务协议操作的结果。
    /// </summary>
    public enum TransactionalStatus
    {
        /// <summary>
        /// 操作成功完成。
        /// </summary>
        Ok,

        /// <summary>
        /// 事务管理器未在截止时间之前完成准备阶段。
        /// </summary>
        PrepareTimeout,

        /// <summary>
        /// 事务因其依赖的事务中止而中止。
        /// </summary>
        CascadingAbort,

        /// <summary>
        /// 事务因超时、并发仲裁或故障而失去锁。
        /// </summary>
        BrokenLock,

        /// <summary>
        /// 执行期间记录的访问情况与准备期间持有的锁不一致。
        /// </summary>
        LockValidationFailed,

        /// <summary>
        /// 事务代理等待只读事务参与者时超时。
        /// </summary>
        ParticipantResponseTimeout,

        /// <summary>
        /// 事务代理等待事务管理器时超时。
        /// </summary>
        TMResponseTimeout,

        /// <summary>
        /// 事务存储被另一个竞争的 Grain 激活实例修改。
        /// </summary>
        StorageConflict,

        /// <summary>
        /// 事务管理器没有该事务的记录，因此推定该事务已中止。
        /// </summary>
        PresumedAbort,

        /// <summary>
        /// 未分类的异常中断了事务协议。
        /// </summary>
        UnknownException,

        /// <summary>
        /// 内部事务协议断言失败。
        /// </summary>
        AssertionFailed,

        /// <summary>
        /// 无法提交事务。
        /// </summary>
        CommitFailure,
    }

    /// <summary>
    /// 提供解释事务状态的操作。
    /// </summary>
    public static class TransactionalStatusExtensions
    {
        /// <summary>
        /// 确定某个状态能否保证事务已中止。
        /// </summary>
        /// <param name="status">事务状态。</param>
        /// <returns>
        /// <see langword="true"/>，如果确定事务已中止；否则为 <see langword="false"/>。
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
        /// 创建由故障状态所表示的面向用户的事务异常。
        /// </summary>
        /// <param name="status">事务失败状态。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="exception">导致失败的异常（如果有）。</param>
        /// <returns>描述事务结果的异常。</returns>
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
