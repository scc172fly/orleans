using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime
{
    /// <summary>
    /// 构造使用建筑师依赖性注射的 grain 类实例。
    /// </summary>
    public class GrainConstructorArgumentFactory
    {
        private static readonly Type FacetMarkerInterfaceType = typeof(IFacetMetadata);
        private static readonly MethodInfo GetFactoryMethod = typeof(GrainConstructorArgumentFactory).GetMethod(nameof(GetArgumentFactory), BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly List<Factory<IGrainContext, object>> _argumentFactories;

        /// <summary>
        /// 初始化一个新的 <see cref="GrainConstructorArgumentFactory"/> 实例。
        /// </summary>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="grainType">下 grain 类型中保留公共建筑师,以查找论据。</param>
        public GrainConstructorArgumentFactory(
            IServiceProvider serviceProvider,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type grainType)
        {
            _argumentFactories = new List<Factory<IGrainContext, object>>();

            // 寻找建筑师 - 只支持单一的公共建筑师。
            var parameters = grainType.GetConstructors().FirstOrDefault()?.GetParameters() ?? Enumerable.Empty<ParameterInfo>();
            var types = new List<Type>();
            foreach (var parameter in parameters)
            {
                // 使用一个 egt 标记界面查找属性 - 只支持单向的字符
                var attribute = parameter.GetCustomAttributes()
                                         .FirstOrDefault(static attribute => FacetMarkerInterfaceType.IsInstanceOfType(attribute));

                if (attribute is null) continue;

                // 由于 IAttributeToFactoryMapper 是特性专业化所特有的,我们创建了通用方法,提供属性独立调用模式。
                var getFactory = GetFactoryMethod.MakeGenericMethod(attribute.GetType());
                var argumentFactory = (Factory<IGrainContext, object>)getFactory.Invoke(this, [serviceProvider, parameter, attribute, grainType])!;

                // 记录辩论厂
                _argumentFactories.Add(argumentFactory);

                // 记录参数类型
                types.Add(parameter.ParameterType);
            }

            ArgumentTypes = types.ToArray();
        }

        /// <summary>
        /// 获得构建参数的参数类型 。
        /// </summary>
        public Type[] ArgumentTypes { get; }

        /// <summary>
        /// 创建 grain 构建器的参数。
        /// </summary>
        /// <param name="grainContext"> grain 上下文。</param>
        /// <returns>建筑师的争论</returns>
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
