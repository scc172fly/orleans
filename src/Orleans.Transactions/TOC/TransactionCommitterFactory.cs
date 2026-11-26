
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 创建当前 grain 激活的交易承诺方。
    /// </summary>
    public class TransactionCommitterFactory : ITransactionCommitterFactory
    {
        private readonly IGrainContextAccessor contextAccessor;

        /// <summary>
        /// 初始化 <see cref="TransactionCommitterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 grain 激活环境的存取器。</param>
        public TransactionCommitterFactory(IGrainContextAccessor contextAccessor)
        {
            this.contextAccessor = contextAccessor;
        }

        /// <inheritdoc/>
        public ITransactionCommitter<TService> Create<TService>(ITransactionCommitterConfiguration config) where TService : class
        {
            var currentContext = contextAccessor.GrainContext;
            TransactionCommitter<TService> transactionalState = ActivatorUtilities.CreateInstance<TransactionCommitter<TService>>(currentContext.ActivationServices, config, this.contextAccessor);
            transactionalState.Participate(currentContext.ObservableLifecycle);
            return transactionalState;
        }
    }
}
