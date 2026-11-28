
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 为当前 Grain 激活实例创建事务提交器。
    /// </summary>
    public class TransactionCommitterFactory : ITransactionCommitterFactory
    {
        private readonly IGrainContextAccessor contextAccessor;

        /// <summary>
        /// 初始化 <see cref="TransactionCommitterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 Grain 激活实例上下文的访问器。</param>
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
