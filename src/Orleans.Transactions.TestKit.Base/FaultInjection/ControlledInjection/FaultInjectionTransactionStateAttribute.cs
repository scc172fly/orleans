using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 配置错误输入交易状态的状态。
    /// </summary>
    public interface IFaultInjectionTransactionalStateConfiguration : ITransactionalStateConfiguration
    {
    }

    /// <summary>
    /// 将构建参数确定为错误输入交易状态。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class FaultInjectionTransactionalStateAttribute : Attribute, IFacetMetadata, IFaultInjectionTransactionalStateConfiguration
    {
        /// <summary>
        /// 获得州名。
        /// </summary>
        public string StateName { get; }

        /// <summary>
        /// 获得存储供应商的名字。
        /// </summary>
        public string? StorageName { get; }

        /// <summary>
        /// 初始化 <see cref="FaultInjectionTransactionalStateAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="stateName">国名。</param>
        /// <param name="storageName">储存提供者的名称,或 <see langword="null"/> 使用默认提供者。</param>
        public FaultInjectionTransactionalStateAttribute(string stateName, string? storageName = null)
        {
            this.StateName = stateName;
            this.StorageName = storageName;
        }
    }

    /// <summary>
    /// 创建错误输入交易状态的状态。
    /// </summary>
    public interface IFaultInjectionTransactionalStateFactory
    {
        /// <summary>
        /// 创建错误输入交易状态的状态元。
        /// </summary>
        /// <typeparam name="TState">状态类型。</typeparam>
        /// <param name="config">交易状态配置。</param>
        /// <returns>错误输入交易状态表。</returns>
        IFaultInjectionTransactionalState<TState> Create<TState>(IFaultInjectionTransactionalStateConfiguration config) where TState : class, new();
    }

    /// <summary>
    /// 创建错误输入交易状态的状态。
    /// </summary>
    public class FaultInjectionTransactionalStateFactory : IFaultInjectionTransactionalStateFactory
    {
        private readonly IGrainContextAccessor contextAccessor;

        /// <summary>
        /// 初始化 <see cref="FaultInjectionTransactionalStateFactory"/> 类的新实例。
        /// </summary>
        /// <param name="contextAccessor">当前 grain 上下文的存取器。</param>
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
    /// 映射国家半国营工厂的 <see cref="FaultInjectionTransactionalStateAttribute"/> 实例。
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
            // 使用通用类型参数定义收藏类型。
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
