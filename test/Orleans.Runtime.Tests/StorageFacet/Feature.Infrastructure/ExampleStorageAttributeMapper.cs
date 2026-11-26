using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Tester.StorageFacet.Abstractions;

namespace Tester.StorageFacet.Infrastructure
{
    public class ExampleStorageAttributeMapper : IAttributeToFactoryMapper<ExampleStorageAttribute>
    {
        private static readonly MethodInfo create = typeof(INamedExampleStorageFactory).GetMethod("Create")!;

        public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, ExampleStorageAttribute attribute)
        {
            IExampleStorageConfig config = attribute;
            // 如果尚未指定, 则将状态名称设置为参数名称
            if (string.IsNullOrEmpty(config.StateName))
            {
                config = new ExampleStorageConfig(parameter.Name!);
            }
            // 使用通用类型参数定义收藏类型。
            MethodInfo genericCreate = create.MakeGenericMethod(parameter.ParameterType.GetGenericArguments());
            object?[] args = new object?[] { attribute.StorageProviderName, config };
            return context => Create(context, genericCreate, args);
        }

        private object Create(IGrainContext context, MethodInfo genericCreate, object?[] args)
        {
            INamedExampleStorageFactory factory = context.ActivationServices.GetRequiredService<INamedExampleStorageFactory>();
            return genericCreate.Invoke(factory, args)!;
        }
    }
}
