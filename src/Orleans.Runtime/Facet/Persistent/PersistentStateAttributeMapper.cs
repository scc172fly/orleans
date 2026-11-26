using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 属性映射器,绘制一个相应的工厂实例的持久性状态属性。
    /// </summary>
    public class PersistentStateAttributeMapper : IAttributeToFactoryMapper<PersistentStateAttribute>
    {
        private static readonly MethodInfo CreateMethodInfo = typeof(IPersistentStateFactory).GetMethod("Create")!;

        /// <inheritdoc/>
        public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, PersistentStateAttribute attribute)
        {
            IPersistentStateConfiguration config = attribute;
            // 如果尚未指定, 则将状态名称设置为参数名称
            if (string.IsNullOrEmpty(config.StateName))
            {
                config = new PersistentStateConfiguration() { StateName = parameter.Name!, StorageName = attribute.StorageName };
            }

            if (!parameter.ParameterType.IsGenericType || !typeof(IPersistentState<>).Equals(parameter.ParameterType.GetGenericTypeDefinition()))
            {
                throw new ArgumentException(
                    $"Parameter '{parameter.Name}' on the constructor for '{parameter.Member.DeclaringType}' has an unsupported type, '{parameter.ParameterType}'. "
                    + $"It must be an instance of generic type '{typeof(IPersistentState<>)}' because it has an associated [PersistentState(...)] attribute.",
                    parameter.Name);
            }

            // 使用通用类型参数定义收藏类型。
            MethodInfo genericCreate = CreateMethodInfo.MakeGenericMethod(parameter.ParameterType.GetGenericArguments());
            return context => Create(context, genericCreate, config);
        }

        private static object Create(IGrainContext context, MethodInfo genericCreate, IPersistentStateConfiguration config)
        {
            IPersistentStateFactory factory = context.ActivationServices.GetRequiredService<IPersistentStateFactory>();
            object[] args = [context, config];
            return genericCreate.Invoke(factory, args)!;
        }

        private class PersistentStateConfiguration : IPersistentStateConfiguration
        {
            public string StateName { get; set; } = null!;

            public string? StorageName { get; set; }
        }
    }
}
