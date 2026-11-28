using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 配置支持故障注入的事务状态 Facet。
    /// </summary>
    public interface IFaultInjectionTransactionalStateConfiguration : ITransactionalStateConfiguration
    {
    }

    /// <summary>
    /// 将构造函数参数标识为支持故障注入的事务状态。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class FaultInjectionTransactionalStateAttribute : Attribute, IFacetMetadata, IFaultInjectionTransactionalStateConfiguration
    {
        /// <summary>
        /// 获取状态名称。
        /// </summary>
        public string StateName { get; }

        /// <summary>
        /// 获取存储提供程序名称。
        /// </summary>
        public string? StorageName { get; }

        /// <summary>
        /// 初始化 <see cref="FaultInjectionTransactionalStateAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="stateName">状态名称。</param>
        /// <param name="storageName">存储提供程序名称；指定 <see langword="null"/> 则使用默认提供程序。</param>
        public FaultInjectionTransactionalStateAttribute(string stateName, string? storageName = null)
        {
            this.StateName = stateName;
            this.StorageName = storageName;
        }
    }

    /// <summary>
    /// 创建支持故障注入的事务状态 Facet。
    /// </summary>
    public interface IFaultInjectionTransactionalStateFactory
    {
        /// <summary>
        /// 创建支持故障注入的事务状态 Facet。
        /// </summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="config">事务状态配置。</param>
        /// <returns>支持故障注入的事务状态 Facet。</returns>
        IFaultInjectionTransactionalState<TState> Create<TState>(IFaultInjectionTransactionalStateConfiguration config) where TState : class, new();
    }

    /// <summary>
    /// 创建支持故障注入的事务状态 Facet。
    /// </summary>
    public class FaultInjectionTransactionalStateFactory : IFaultInjectionTransactionalStateFactory
    {
        private readonly IGrainContextAccessor contextAccessor;

        /// <summary>
        /// 初始化 <see cref="FaultInjectionTransactionalStateFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 Grain 上下文的访问器。</param>
        public FaultInjectionTransactionalStateFactory(IGrainContextAccessor contextAccessor)
        {
            this.contextAccessor = contextAccessor;
        }

        /// <inheritdoc />
        public IFaultInjectionTransactionalState<TState> Create<TState>(IFaultInjectionTransactionalStateConfiguration config) where TState : class, new()
        {
            var currentContext = this.contextAccessor.GrainContext;
            TransactionalState<TState> transactionalState = ActivatorUtilities.CreateInstance<TransactionalState<TState>>(currentContext.ActivationServices, new TransactionalStateConfiguration(config), this.contextAccessor);
            FaultInjectionTransactionalState<TState> deactivationTransactionalState = ActivatorUtilities.CreateInstance<FaultInjectionTransactionalState<TState>>(currentContext.ActivationServices, transactionalState);
            deactivationTransactionalState.Participate(currentContext.ObservableLifecycle);
            return deactivationTransactionalState;
        }
    }

    /// <summary>
    /// 将 <see cref="FaultInjectionTransactionalStateAttribute"/> 实例映射到事务状态 Facet 工厂。
    /// </summary>
    public class FaultInjectionTransactionalStateAttributeMapper : IAttributeToFactoryMapper<FaultInjectionTransactionalStateAttribute>
    {
        private static readonly MethodInfo create =
            typeof(IFaultInjectionTransactionalStateFactory).GetMethod("Create")!;
        /// <inheritdoc />
        public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, FaultInjectionTransactionalStateAttribute attribute)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            IFaultInjectionTransactionalStateConfiguration config = attribute;
            // 使用泛型类型实参定义集合类型。
            MethodInfo genericCreate = create.MakeGenericMethod(parameter.ParameterType.GetGenericArguments());
            object[] args = new object[] { config };
            return context => Create(context, genericCreate, args);
        }

        private static object Create(IGrainContext context, MethodInfo genericCreate, object[] args)
        {
            IFaultInjectionTransactionalStateFactory factory = context.ActivationServices.GetRequiredService<IFaultInjectionTransactionalStateFactory>();
            return genericCreate.Invoke(factory, args)!;
        }
    }
}
