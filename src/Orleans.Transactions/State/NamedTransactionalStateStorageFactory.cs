using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Orleans.Storage;

namespace Orleans.Transactions
{
    /// <summary>
    /// 利用名为交易储存工厂或 grain 的存储供应商创建交易性国家储存。
    /// </summary>
    public class NamedTransactionalStateStorageFactory : INamedTransactionalStateStorageFactory
    {
        private readonly IGrainContextAccessor contextAccessor;

        /// <summary>
        /// 初始化 <see cref="NamedTransactionalStateStorageFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 grain 上下文的存取器。</param>
        /// <param name="loggerFactory">记录器厂的参数保留为兼容性。</param>
        [Obsolete("Use the NamedTransactionalStateStorageFactory(IGrainContextAccessor contextAccessor) constructor.")]
        public NamedTransactionalStateStorageFactory(IGrainContextAccessor contextAccessor, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory) : this(contextAccessor)
        {
        }

        /// <summary>
        /// 初始化 <see cref="NamedTransactionalStateStorageFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 grain 上下文的存取器。</param>
        public NamedTransactionalStateStorageFactory(IGrainContextAccessor contextAccessor)
        {
            this.contextAccessor = contextAccessor;
        }

        /// <inheritdoc />
        public ITransactionalStateStorage<TState> Create<TState>(string? storageName, string stateName)
            where TState : class, new()
        {
            var currentContext = this.contextAccessor.GrainContext;

            // 尝试从工厂获取 ITransactionalStateStorage
            ITransactionalStateStorageFactory? factory = string.IsNullOrEmpty(storageName)
                ? currentContext.ActivationServices.GetService<ITransactionalStateStorageFactory>()
                : currentContext.ActivationServices.GetKeyedService<ITransactionalStateStorageFactory>(storageName);
            if (factory != null) return factory.Create<TState>(stateName, currentContext);

            // 尝试找存储供应商包起来
            IGrainStorage? grainStorage = string.IsNullOrEmpty(storageName)
                ? currentContext.ActivationServices.GetService<IGrainStorage>()
                : currentContext.ActivationServices.GetKeyedService<IGrainStorage>(storageName);

            if (grainStorage != null)
            {
                return new TransactionalStateStorageProviderWrapper<TState>(grainStorage, stateName, currentContext);
            }

            throw (string.IsNullOrEmpty(storageName))
                ? new InvalidOperationException($"No default {nameof(ITransactionalStateStorageFactory)} nor {nameof(IGrainStorage)} was found while attempting to create transactional state storage.")
                : new InvalidOperationException($"No {nameof(ITransactionalStateStorageFactory)} nor {nameof(IGrainStorage)} with the name {storageName} was found while attempting to create transactional state storage.");
        }
    }
}
