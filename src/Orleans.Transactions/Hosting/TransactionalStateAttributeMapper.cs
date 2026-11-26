using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 映射 <see cref="TransactionalStateAttribute"/> 参数以显示交易状态实例。
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
    /// 映射交易状态属性与交易状态实例的属性。
    /// </summary>
    /// <typeparam name="TAttribute">交易状态表面属性类型。</typeparam>
    public abstract class TransactionalStateAttributeMapper<TAttribute> : IAttributeToFactoryMapper<TAttribute>
        where TAttribute : IFacetMetadata, ITransactionalStateConfiguration
    {
        private static readonly MethodInfo create = typeof(ITransactionalStateFactory).GetMethod("Create")!;

        /// <inheritdoc/>
        public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, TAttribute attribute)
        {
            TransactionalStateConfiguration config = AttributeToConfig(attribute);
            // 使用通用类型参数定义收藏类型。
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
        /// 从 entat 属性创建交易状态配置 。
        /// </summary>
        /// <param name="attribute">交易状态表面属性。</param>
        /// <returns>交易状态配置。</returns>
        protected abstract TransactionalStateConfiguration AttributeToConfig(TAttribute attribute);
    }
}
