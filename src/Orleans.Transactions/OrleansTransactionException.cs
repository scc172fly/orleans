using Orleans.Runtime;
using System;
using System.Runtime.Serialization;

namespace Orleans.Transactions
{
    /// <summary>
    /// 所有交易例外的基准类
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
        /// <param name="message">描述错误的电文。</param>
        public OrleansTransactionException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的电文。</param>
        /// <param name="innerException">造成当前例外的例外。</param>
        public OrleansTransactionException(string message, Exception? innerException) : base(message, innerException!) { }

        /// <summary>
        /// 从序列数据初始化了 <see cref="OrleansTransactionException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列物体数据。</param>
        /// <param name="context">序号来源或目的地的背景资料。</param>
        [Obsolete]
        protected OrleansTransactionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Orleans 交易已禁用。
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
    /// 表示运行时间无法启动交易 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansStartTransactionFailedException : OrleansTransactionException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansStartTransactionFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="innerException">阻止交易启动的例外。</param>
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
    /// 表示交易运行时间超负荷
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
    /// 表示运行时间无法确定交易是否
    /// 已经承诺。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionInDoubtException : OrleansTransactionException
    {
        /// <summary>
        /// 获取无法确定结果的交易识别符。
        /// </summary>
        [Id(0)]
        public string TransactionId { get; private set; }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionInDoubtException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法确定结果的交易识别特征。</param>
        public OrleansTransactionInDoubtException(string transactionId) : base(string.Format("Transaction {0} is InDoubt", transactionId))
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionInDoubtException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法确定结果的交易识别特征。</param>
        /// <param name="exc">例外规定妨碍了结果的确定。</param>
        public OrleansTransactionInDoubtException(string transactionId, Exception? exc) : base(string.Format("Transaction {0} is InDoubt", transactionId), exc)
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionInDoubtException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法确定结果的交易识别特征。</param>
        /// <param name="msg">有关不确定结果的补充资料。</param>
        /// <param name="innerException">例外规定妨碍了结果的确定。</param>
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
    /// 表示执行交易已中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class OrleansTransactionAbortedException : OrleansTransactionException
    {
        /// <summary>
        /// 中止交易的唯一标识符 。
        /// </summary>
        [Id(0)]
        public string TransactionId { get; private set; }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionAbortedException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="msg">描述中止的邮件。</param>
        /// <param name="innerException">导致交易中止的例外。</param>
        public OrleansTransactionAbortedException(string transactionId, string msg, Exception? innerException) : base(msg, innerException)
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionAbortedException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="msg">描述中止的邮件。</param>
        public OrleansTransactionAbortedException(string transactionId, string msg) : base(msg)
        {
            this.TransactionId = transactionId;
        }

        /// <summary>
        /// 初始化一个新实例 <see cref="OrleansTransactionAbortedException"/> 类,以适应导致的中止
        /// 以无处理的, grain 方法例外。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="innerException">导致交易中止的例外。</param>
        public OrleansTransactionAbortedException(string transactionId, Exception? innerException)
            : base($"Transaction {transactionId} Aborted because of an unhandled exception in a grain method call. See InnerException for details.", innerException)
        {
            TransactionId = transactionId;
        }

        /// <summary>
        /// 从序列数据初始化了 <see cref="OrleansTransactionAbortedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列物体数据。</param>
        /// <param name="context">序号来源或目的地的背景资料。</param>
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
    /// 表示执行交易已经中止,因为附属交易中断。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansCascadingAbortException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 如果知道,则获取中止的附属交易的身份识别符。
        /// </summary>
        [Id(0)]
        public string? DependentTransactionId { get; private set; }

        /// <summary>
        /// 初始化 <see cref="OrleansCascadingAbortException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="dependentId">中止的附属交易标识。</param>
        public OrleansCascadingAbortException(string transactionId, string dependentId)
            : base(transactionId, string.Format("Transaction {0} aborted because its dependent transaction {1} aborted", transactionId, dependentId))
        {
            this.DependentTransactionId = dependentId;
        }

        /// <summary>
        /// 初始化 <see cref="OrleansCascadingAbortException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        public OrleansCascadingAbortException(string transactionId)
            : base(transactionId, string.Format("Transaction {0} aborted because a dependent transaction aborted", transactionId))
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansCascadingAbortException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="innerException">与附属交易中止有关的例外。</param>
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
    /// 这表明,由于一种方法没有等待所有待发电话,执行交易已经中止。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansOrphanCallException : OrleansTransactionAbortedException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansOrphanCallException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="pendingCalls">交易结束时未完成的通话次数。</param>
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
    /// 表示执行只读交易已中止, 因为它试图写入 grain。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansReadOnlyViolatedException : OrleansTransactionAbortedException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansReadOnlyViolatedException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">尝试写入的只读交易标识符。</param>
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
    /// 表示没有所需的交易服务。
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
    /// 表示执行交易已中止,因为其执行锁被打破
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansBrokenTransactionLockException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansBrokenTransactionLockException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="situation">检测破锁的交易阶段或条件。</param>
        public OrleansBrokenTransactionLockException(string transactionId, string situation)
            : base(transactionId, $"Transaction {transactionId} aborted because a broken lock was detected, {situation}")
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansBrokenTransactionLockException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="situation">检测破锁的交易阶段或条件。</param>
        /// <param name="innerException">与断锁相关的例外。</param>
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
    /// 表示执行交易已中止, 因为它无法升级某些锁定
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionLockUpgradeException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionLockUpgradeException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">无法更新其锁定的交易标识符。</param>
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
    /// 表示执行交易已中止,因为TMM没有及时收到所有准备好的信件
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansTransactionPrepareTimeoutException : OrleansTransactionTransientFailureException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionPrepareTimeoutException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">交易的识别特征,其准备阶段已超时。</param>
        /// <param name="innerException">与准备超时有关的例外。</param>
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
    /// 执行交易中止是因为某些可能暂时的问题,如内部问题
    /// 锁定或协议反应或投机失败的超时。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class OrleansTransactionTransientFailureException : OrleansTransactionAbortedException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansTransactionTransientFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="msg">描述瞬时故障的电文。</param>
        /// <param name="innerException">导致短暂故障的例外。</param>
        public OrleansTransactionTransientFailureException(string transactionId, string msg, Exception? innerException)
            : base(transactionId, msg, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansTransactionTransientFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="transactionId">中止的交易的标识符。</param>
        /// <param name="msg">描述瞬时故障的电文。</param>
        public OrleansTransactionTransientFailureException(string transactionId, string msg)
            : base(transactionId, msg)
        {
        }

        /// <summary>
        /// 从序列数据初始化了 <see cref="OrleansTransactionTransientFailureException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列物体数据。</param>
        /// <param name="context">序号来源或目的地的背景资料。</param>
        [Obsolete]
        protected OrleansTransactionTransientFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
