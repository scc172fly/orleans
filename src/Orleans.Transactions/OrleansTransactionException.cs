using Orleans.Runtime;
using System;
using System.Runtime.Serialization;

namespace Orleans.Transactions
{
    /// <summary>
    /// 所有事务异常的基类。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class OrleansTransactionException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionException"/> 类的新实例。
        /// </summary>
        public OrleansTransactionException() : base("Orleans transaction error.") { }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的消息。</param>
        public OrleansTransactionException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的消息。</param>
        /// <param name="innerException">导致当前异常的异常。</param>
        public OrleansTransactionException(string message, Exception? innerException) : base(message, innerException!) { }

        /// <summary>
        /// 从序列化数据初始化 <see cref="OrleansTransactionException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化后的对象数据。</param>
        /// <param name="context">关于序列化源或目标的上下文信息。</param>
        [Obsolete]
        protected OrleansTransactionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Orleans 事务已禁用。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionsDisabledException : OrleansTransactionException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionsDisabledException"/> 类的新实例。
        /// </summary>
        public OrleansTransactionsDisabledException()
            : base("Orleans transactions have not been enabled. Transactions are disabled by default and must be configured to be used.")
        {
        }

        [Obsolete]
        private OrleansTransactionsDisabledException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示运行时无法启动事务。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansStartTransactionFailedException : OrleansTransactionException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansStartTransactionFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="innerException">阻止事务启动的异常。</param>
        public OrleansStartTransactionFailedException(Exception innerException)
            : base("Failed to start transaction. Check InnerException for details", innerException)
        {
        }

        [Obsolete]
        private OrleansStartTransactionFailedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示事务运行时过载。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionOverloadException : OrleansTransactionException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionOverloadException"/> 类的新实例。
        /// </summary>
        public OrleansTransactionOverloadException()
            : base("Transaction is overloaded on current silo, please try again later.")
        {
        }
    }

    /// <summary>
    /// 表示运行时无法确定事务是否
    /// 已提交。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionInDoubtException : OrleansTransactionException
    {
        /// <summary>
        /// 获取无法确定结果的事务的标识符。
        /// </summary>
        [Id(0)]
        public string TransactionId { get; private set; }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionInDoubtException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法确定结果的事务的标识符。</param>
        public OrleansTransactionInDoubtException(string transactionId) : base(string.Format("Transaction {0} is InDoubt", transactionId))
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionInDoubtException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法确定结果的事务的标识符。</param>
        /// <param name="exc">导致无法确定结果的异常。</param>
        public OrleansTransactionInDoubtException(string transactionId, Exception? exc) : base(string.Format("Transaction {0} is InDoubt", transactionId), exc)
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionInDoubtException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法确定结果的事务的标识符。</param>
        /// <param name="msg">关于不确定结果的附加信息。</param>
        /// <param name="innerException">导致无法确定结果的异常。</param>
        public OrleansTransactionInDoubtException(string transactionId, string msg, Exception? innerException) : base(string.Format("Transaction {0} is InDoubt: {1}", transactionId, msg), innerException)
        {
            this.TransactionId = transactionId;
        }

        [Obsolete]
        private OrleansTransactionInDoubtException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.TransactionId = info.GetString(nameof(this.TransactionId))!;
        }

        /// <inheritdoc/>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(this.TransactionId), this.TransactionId);
        }
    }

    /// <summary>
    /// 表示正在执行的事务已中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class OrleansTransactionAbortedException : OrleansTransactionException
    {
        /// <summary>
        /// 已中止事务的唯一标识符。
        /// </summary>
        [Id(0)]
        public string TransactionId { get; private set; }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionAbortedException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="msg">描述中止情况的消息。</param>
        /// <param name="innerException">导致事务中止的异常。</param>
        public OrleansTransactionAbortedException(string transactionId, string msg, Exception? innerException) : base(msg, innerException)
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionAbortedException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="msg">描述中止情况的消息。</param>
        public OrleansTransactionAbortedException(string transactionId, string msg) : base(msg)
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionAbortedException"/> 类的新实例，表示由
        /// 未处理的 Grain 方法异常导致的事务中止。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="innerException">导致事务中止的异常。</param>
        public OrleansTransactionAbortedException(string transactionId, Exception? innerException)
            : base($"Transaction {transactionId} Aborted because of an unhandled exception in a grain method call. See InnerException for details.", innerException)
        {
            TransactionId = transactionId;
        }

        /// <summary>
        /// 从序列化数据初始化 <see cref="OrleansTransactionAbortedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化后的对象数据。</param>
        /// <param name="context">关于序列化源或目标的上下文信息。</param>
        [Obsolete]
        protected OrleansTransactionAbortedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.TransactionId = info.GetString(nameof(this.TransactionId))!;
        }

        /// <inheritdoc/>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(this.TransactionId), this.TransactionId);
        }
    }

    /// <summary>
    /// 表示正在执行的事务因其依赖的事务中止而中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansCascadingAbortException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 获取已中止的依赖事务的标识符（如果已知）。
        /// </summary>
        [Id(0)]
        public string? DependentTransactionId { get; private set; }

        /// <summary>
        /// 初始化 <see cref="OrleansCascadingAbortException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="dependentId">已中止的依赖事务的标识符。</param>
        public OrleansCascadingAbortException(string transactionId, string dependentId)
            : base(transactionId, string.Format("Transaction {0} aborted because its dependent transaction {1} aborted", transactionId, dependentId))
        {
            this.DependentTransactionId = dependentId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansCascadingAbortException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        public OrleansCascadingAbortException(string transactionId)
            : base(transactionId, string.Format("Transaction {0} aborted because a dependent transaction aborted", transactionId))
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansCascadingAbortException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="innerException">与依赖事务中止相关的异常。</param>
        public OrleansCascadingAbortException(string transactionId, Exception? innerException)
            : base(transactionId, string.Format("Transaction {0} aborted because a dependent transaction aborted", transactionId), innerException)
        {
        }

        [Obsolete]
        private OrleansCascadingAbortException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.DependentTransactionId = info.GetString(nameof(this.DependentTransactionId));
        }

        /// <inheritdoc/>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(this.DependentTransactionId), this.DependentTransactionId);
        }
    }

    /// <summary>
    /// 表示正在执行的事务因某个方法未等待其所有待处理调用而中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansOrphanCallException : OrleansTransactionAbortedException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansOrphanCallException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="pendingCalls">事务结束时尚未完成的调用数量。</param>
        public OrleansOrphanCallException(string transactionId, int pendingCalls)
            : base(
                transactionId,
                $"Transaction {transactionId} aborted because method did not await all its outstanding calls ({pendingCalls})")
        {
        }

        [Obsolete]
        private OrleansOrphanCallException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示正在执行的只读事务因尝试写入 Grain 而中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansReadOnlyViolatedException : OrleansTransactionAbortedException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansReadOnlyViolatedException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">尝试写入的只读事务的标识符。</param>
        public OrleansReadOnlyViolatedException(string transactionId)
            : base(transactionId, string.Format("Transaction {0} aborted because it attempted to write a grain", transactionId))
        {
        }

        [Obsolete]
        private OrleansReadOnlyViolatedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示所需的事务服务不可用。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionServiceNotAvailableException : OrleansTransactionException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionServiceNotAvailableException"/> 类的新实例。
        /// </summary>
        public OrleansTransactionServiceNotAvailableException() : base("Transaction service not available")
        {
        }

        [Obsolete]
        private OrleansTransactionServiceNotAvailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示正在执行的事务因其执行锁被解除而中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansBrokenTransactionLockException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansBrokenTransactionLockException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="situation">检测到锁被解除时的事务阶段或条件。</param>
        public OrleansBrokenTransactionLockException(string transactionId, string situation)
            : base(transactionId, $"Transaction {transactionId} aborted because a broken lock was detected, {situation}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansBrokenTransactionLockException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="situation">检测到锁被解除时的事务阶段或条件。</param>
        /// <param name="innerException">与锁被解除相关的异常。</param>
        public OrleansBrokenTransactionLockException(string transactionId, string situation, Exception? innerException)
            : base(transactionId, $"Transaction {transactionId} aborted because a broken lock was detected, {situation}", innerException)
        {
        }

        [Obsolete]
        private OrleansBrokenTransactionLockException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示正在执行的事务因无法升级某个锁而中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionLockUpgradeException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionLockUpgradeException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法升级其锁的事务的标识符。</param>
        public OrleansTransactionLockUpgradeException(string transactionId) :
            base(transactionId, $"Transaction {transactionId} Aborted because it could not upgrade a lock, because of a higher-priority conflicting transaction")
        {
        }

        [Obsolete]
        private OrleansTransactionLockUpgradeException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示正在执行的事务因 TM 未及时收到所有已准备消息而中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionPrepareTimeoutException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionPrepareTimeoutException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">准备阶段超时的事务的标识符。</param>
        /// <param name="innerException">与准备超时相关的异常。</param>
        public OrleansTransactionPrepareTimeoutException(string transactionId, Exception? innerException)
            : base(transactionId, $"Transaction {transactionId} Aborted because the prepare phase did not complete within the timeout limit", innerException)
        {
        }

        [Obsolete]
        private OrleansTransactionPrepareTimeoutException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示正在执行的事务因某些可能是暂时性的问题而中止，例如内部
    /// 锁或协议响应超时，或推测执行失败。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class OrleansTransactionTransientFailureException : OrleansTransactionAbortedException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionTransientFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="msg">描述暂时性故障的消息。</param>
        /// <param name="innerException">导致暂时性故障的异常。</param>
        public OrleansTransactionTransientFailureException(string transactionId, string msg, Exception? innerException)
            : base(transactionId, msg, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionTransientFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">已中止事务的标识符。</param>
        /// <param name="msg">描述暂时性故障的消息。</param>
        public OrleansTransactionTransientFailureException(string transactionId, string msg)
            : base(transactionId, msg)
        {
        }

        /// <summary>
        /// 从序列化数据初始化 <see cref="OrleansTransactionTransientFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化后的对象数据。</param>
        /// <param name="context">关于序列化源或目标的上下文信息。</param>
        [Obsolete]
        protected OrleansTransactionTransientFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
