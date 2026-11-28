using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 将带有 <see cref="TransactionalStateAttribute"/> 的参数映射到事务状态实例。
    /// </summary>
    public class TransactionalStateAttributeMapper : TransactionalStateAttributeMapper<TransactionalStateAttribute>
    {
        /// <inheritdoc/>
        protected override TransactionalStateConfiguration AttributeToConfig(TransactionalStateAttribute attribute)
        {
            return new TransactionalStateConfiguration(attribute);
        }
    }

    /// <summary>
    /// 将事务状态组件特性映射到事务状态实例。
    /// </summary>
    /// <typeparam name="TAttribute">事务状态组件特性的类型。</typeparam>
    public abstract class TransactionalStateAttributeMapper<TAttribute> : IAttributeToFactoryMapper<TAttribute>
        where TAttribute : IFacetMetadata, ITransactionalStateConfiguration
    {
        private static readonly MethodInfo create = typeof(ITransactionalStateFactory).GetMethod("Create")!;

        /// <inheritdoc/>
        public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, TAttribute attribute)
        {
            TransactionalStateConfiguration config = AttributeToConfig(attribute);
            // 使用泛型类型参数定义集合类型。
            MethodInfo genericCreate = create.MakeGenericMethod(parameter.ParameterType.GetGenericArguments());
            object[] args = new object[] { config };
            return context => Create(context, genericCreate, args);
        }

        private object Create(IGrainContext context, MethodInfo genericCreate, object[] args)
        {
            ITransactionalStateFactory factory = context.ActivationServices.GetRequiredService<ITransactionalStateFactory>();
            return genericCreate.Invoke(factory, args)!;
        }

        /// <summary>
        /// 从组件特性创建事务状态配置。
        /// </summary>
        /// <param name="attribute">事务状态组件特性。</param>
        /// <returns>事务状态配置。</returns>
        protected abstract TransactionalStateConfiguration AttributeToConfig(TAttribute attribute);
    }
}
