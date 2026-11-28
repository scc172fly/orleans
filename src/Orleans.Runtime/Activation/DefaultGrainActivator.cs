using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime
{
    /// <summary>
    /// 默认的 <see cref="IGrainActivator"/> 实现。
    /// </summary>
    public class DefaultGrainActivator : IGrainActivator
    {
        private readonly ObjectFactory _grainInstanceFactory;
        private readonly GrainConstructorArgumentFactory _argumentFactory;
        private readonly Type _grainClass;

        /// <summary>
        /// 初始化新的 <see cref="DefaultGrainActivator"/> 实例。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="grainClass">保留公共构造函数以供依赖注入使用的 Grain 类。</param>
        public DefaultGrainActivator(
            IServiceProvider serviceProvider,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type grainClass)
        {
            _argumentFactory = new GrainConstructorArgumentFactory(serviceProvider, grainClass);
            _grainInstanceFactory = ActivatorUtilities.CreateFactory(grainClass, _argumentFactory.ArgumentTypes);
            _grainClass = grainClass;
        }

        /// <inheritdoc/>
        public object CreateInstance(IGrainContext context)
        {
            try
            {
                var args = _argumentFactory.CreateArguments(context);
                return _grainInstanceFactory(context.ActivationServices, args);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to create an instance of grain type '{_grainClass}'. See {nameof(Exception.InnerException)} for details.",
                    exception);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeInstance(IGrainContext context, object instance)
        {
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }
}
