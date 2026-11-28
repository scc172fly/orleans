using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime
{
    /// <summary>
    /// 使用构造函数依赖注入创建 Grain 类实例。
    /// </summary>
    public class GrainConstructorArgumentFactory
    {
        private static readonly Type FacetMarkerInterfaceType = typeof(IFacetMetadata);
        private static readonly MethodInfo GetFactoryMethod = typeof(GrainConstructorArgumentFactory).GetMethod(nameof(GetArgumentFactory), BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly List<Factory<IGrainContext, object>> _argumentFactories;

        /// <summary>
        /// 初始化新的 <see cref="GrainConstructorArgumentFactory"/> 实例。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="grainType">保留公共构造函数以供参数发现使用的 Grain 类型。</param>
        public GrainConstructorArgumentFactory(
            IServiceProvider serviceProvider,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type grainType)
        {
            _argumentFactories = new List<Factory<IGrainContext, object>>();

            // 查找构造函数，仅支持一个公共构造函数。
            var parameters = grainType.GetConstructors().FirstOrDefault()?.GetParameters() ?? Enumerable.Empty<ParameterInfo>();
            var types = new List<Type>();
            foreach (var parameter in parameters)
            {
                // 查找实现 facet 标记接口的特性，仅支持一个 facet 特性。
                var attribute = parameter.GetCustomAttributes()
                                         .FirstOrDefault(static attribute => FacetMarkerInterfaceType.IsInstanceOfType(attribute));

                if (attribute is null) continue;

                // 由于 IAttributeToFactoryMapper 针对特定特性类型进行专门化，因此创建泛型方法，以提供与特性类型无关的调用方式。
                var getFactory = GetFactoryMethod.MakeGenericMethod(attribute.GetType());
                var argumentFactory = (Factory<IGrainContext, object>)getFactory.Invoke(this, [serviceProvider, parameter, attribute, grainType])!;

                // 记录参数工厂。
                _argumentFactories.Add(argumentFactory);

                // 记录参数类型。
                types.Add(parameter.ParameterType);
            }

            ArgumentTypes = types.ToArray();
        }

        /// <summary>
        /// 获取构造函数参数类型。
        /// </summary>
        public Type[] ArgumentTypes { get; }

        /// <summary>
        /// 创建 Grain 构造函数的参数。
        /// </summary>
        /// <param name="grainContext">Grain 上下文。</param>
        /// <returns>构造函数参数。</returns>
        public object[] CreateArguments(IGrainContext grainContext)
        {
            var i = 0;
            var results = new object[_argumentFactories.Count];
            foreach (var argumentFactory in _argumentFactories)
            {
                results[i++] = argumentFactory(grainContext);
            }

            return results;
        }

        private static Factory<IGrainContext, object> GetArgumentFactory<TMetadata>(IServiceProvider services, ParameterInfo parameter, IFacetMetadata metadata, Type type)
            where TMetadata : IFacetMetadata
        {
            var factoryMapper = services.GetService<IAttributeToFactoryMapper<TMetadata>>();
            if (factoryMapper is null)
            {
                throw new OrleansException($"Missing attribute mapper for attribute {metadata.GetType()} used in grain constructor for grain type {type}.");
            }

            var factory = factoryMapper.GetFactory(parameter, (TMetadata)metadata);
            if (factory is null)
            {
                throw new OrleansException($"Attribute mapper {factoryMapper.GetType()} failed to create a factory for grain type {type}.");
            }

            return factory;
        }
    }
}
