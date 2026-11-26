using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 将远程操作输入到交易中。
    /// </summary>
    public class TransactionCommitterTestGrain : Grain, ITransactionCommitterTestGrain
    {
        /// <summary>
        /// 交易承诺者用来进行远程操作
        /// </summary>
        protected ITransactionCommitter<IRemoteCommitService> committer;
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// 启动 grain 的登录器 。
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="TransactionCommitterTestGrain"/> 类的新实例。
        /// </summary>
        /// <param name="committer">交易承诺者</param>
        /// <param name="loggerFactory">伐木厂</param>
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
