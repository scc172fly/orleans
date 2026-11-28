using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Orleans.Storage;

namespace Orleans.Transactions
{
    /// <summary>
    /// 使用命名事务存储工厂或 Grain 存储提供程序创建事务状态存储。
    /// </summary>
    public class NamedTransactionalStateStorageFactory : INamedTransactionalStateStorageFactory
    {
        private readonly IGrainContextAccessor contextAccessor;

        /// <summary>
        /// 初始化 <see cref="NamedTransactionalStateStorageFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 Grain 上下文的访问器。</param>
        /// <param name="loggerFactory">为兼容性而保留的日志记录器工厂参数。</param>
        [Obsolete("Use the NamedTransactionalStateStorageFactory(IGrainContextAccessor contextAccessor) constructor.")]
        public NamedTransactionalStateStorageFactory(IGrainContextAccessor contextAccessor, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory) : this(contextAccessor)
        {
        }

        /// <summary>
        /// 初始化 <see cref="NamedTransactionalStateStorageFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 Grain 上下文的访问器。</param>
        public NamedTransactionalStateStorageFactory(IGrainContextAccessor contextAccessor)
        {
            this.contextAccessor = contextAccessor;
        }

        /// <inheritdoc />
        public ITransactionalStateStorage<TState> Create<TState>(string? storageName, string stateName)
            where TState : class, new()
        {
            var currentContext = this.contextAccessor.GrainContext;

            // 尝试从工厂获取 ITransactionalStateStorage。
            ITransactionalStateStorageFactory? factory = string.IsNullOrEmpty(storageName)
                ? currentContext.ActivationServices.GetService<ITransactionalStateStorageFactory>()
                : currentContext.ActivationServices.GetKeyedService<ITransactionalStateStorageFactory>(storageName);
            if (factory != null) return factory.Create<TState>(stateName, currentContext);

            // 否则尝试获取存储提供程序并对其进行包装。
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
