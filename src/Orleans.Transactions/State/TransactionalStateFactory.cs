using System;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 为当前 Grain 激活实例创建并初始化事务状态组件。
    /// </summary>
    public class TransactionalStateFactory : ITransactionalStateFactory
    {
        private readonly IGrainContextAccessor contextAccessor;

        /// <summary>
        /// 初始化 <see cref="TransactionalStateFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 Grain 上下文的访问器。</param>
        public TransactionalStateFactory(IGrainContextAccessor contextAccessor)
        {
            this.contextAccessor = contextAccessor;
        }

        /// <inheritdoc />
        public ITransactionalState<TState> Create<TState>(TransactionalStateConfiguration config) where TState : class, new()
        {
            var currentContext = this.contextAccessor.GrainContext;
            TransactionalState<TState> transactionalState = ActivatorUtilities.CreateInstance<TransactionalState<TState>>(currentContext.ActivationServices, config, this.contextAccessor);
            transactionalState.Participate(currentContext.ObservableLifecycle);
            return transactionalState;
        }

        /// <summary>
        /// 创建适用于持久化事务状态的 JSON 序列化器设置。
        /// </summary>
        /// <param name="serviceProvider">用于解析序列化服务的服务提供程序。</param>
        /// <returns>配置完成的 JSON 序列化器设置。</returns>
        public static JsonSerializerSettings GetJsonSerializerSettings(IServiceProvider serviceProvider)
        {
            var serializerSettings = OrleansJsonSerializerSettings.GetDefaultSerializerSettings(serviceProvider);
            serializerSettings.PreserveReferencesHandling = PreserveReferencesHandling.None;
            return serializerSettings;
        }
    }
}
