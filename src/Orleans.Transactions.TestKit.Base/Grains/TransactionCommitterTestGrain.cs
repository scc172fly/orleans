using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 将远程提交操作登记到事务中。
    /// </summary>
    public class TransactionCommitterTestGrain : Grain, ITransactionCommitterTestGrain
    {
        /// <summary>
        /// 用于登记远程操作的事务提交器。
        /// </summary>
        protected ITransactionCommitter<IRemoteCommitService> committer;
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// 此 Grain 激活实例的日志记录器。
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="TransactionCommitterTestGrain"/> 类的新实例。
        /// </summary>
        /// <param name="committer">事务提交器。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public TransactionCommitterTestGrain(
            [TransactionCommitter(TransactionTestConstants.RemoteCommitService, TransactionTestConstants.TransactionStore)] ITransactionCommitter<IRemoteCommitService> committer,
            ILoggerFactory loggerFactory)
        {
            this.committer = committer;
            this.loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.logger = this.loggerFactory.CreateLogger(this.GetGrainId().ToString());
            return base.OnActivateAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public Task Commit(ITransactionCommitOperation<IRemoteCommitService> operation)
        {
            return this.committer.OnCommit(operation);
        }
    }
}
