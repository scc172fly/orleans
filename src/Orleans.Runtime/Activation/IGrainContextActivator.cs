using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime.Placement;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Session;

namespace Orleans.Runtime
{
    /// <summary>
    /// 创建 Grain 上下文的中心入口。
    /// </summary>
    public sealed class GrainContextActivator
    {
        private readonly Lock _lockObj = new();
        private readonly IGrainContextActivatorProvider[] _activatorProviders;
        private readonly IConfigureGrainContextProvider[] _configuratorProviders;
        private readonly GrainPropertiesResolver _resolver;
        private ImmutableDictionary<GrainType, (IGrainContextActivator Activator, IConfigureGrainContext[] ConfigureActions)> _activators
            = ImmutableDictionary<GrainType, (IGrainContextActivator, IConfigureGrainContext[])>.Empty;

        /// <summary>
        /// 初始化 <see cref="GrainContextActivator"/> 类的新实例。
        /// </summary>
        /// <param name="providers">Grain 上下文激活器提供程序。</param>
        /// <param name="configureContextActions"><see cref="IConfigureGrainContext"/> 提供程序。</param>
        /// <param name="grainPropertiesResolver">Grain 属性解析器。</param>
        public GrainContextActivator(
            IEnumerable<IGrainContextActivatorProvider> providers,
            IEnumerable<IConfigureGrainContextProvider> configureContextActions,
            GrainPropertiesResolver grainPropertiesResolver)
        {
            _resolver = grainPropertiesResolver;
            _activatorProviders = providers.ToArray();
            _configuratorProviders = configureContextActions.ToArray();
        }

        /// <summary>
        /// 为提供的 Grain 地址创建新的 Grain 上下文。
        /// </summary>
        /// <param name="address">Grain 地址。</param>
        /// <returns>Grain 上下文。</returns>
        public IGrainContext CreateInstance(GrainAddress address)
        {
            var grainId = address.GrainId;
            if (!_activators.TryGetValue(grainId.Type, out var activator))
            {
                activator = this.CreateActivator(grainId.Type);
            }

            return activator.Activator.CreateContext(address, activator.ConfigureActions);
        }

        private (IGrainContextActivator, IConfigureGrainContext[]) CreateActivator(GrainType grainType)
        {
            lock (_lockObj)
            {
                if (!_activators.TryGetValue(grainType, out var configuredActivator))
                {
                    IGrainContextActivator? unconfiguredActivator = null;
                    foreach (var provider in this._activatorProviders)
                    {
                        if (provider.TryGet(grainType, out unconfiguredActivator))
                        {
                            break;
                        }
                    }

                    if (unconfiguredActivator is null)
                    {
                        throw new InvalidOperationException($"Unable to find an {nameof(IGrainContextActivatorProvider)} for grain type {grainType}");
                    }

                    var properties = _resolver.GetGrainProperties(grainType);
                    List<IConfigureGrainContext> configureActions = new List<IConfigureGrainContext>();
                    foreach (var provider in _configuratorProviders)
                    {
                        if (provider.TryGetConfigurator(grainType, properties, out var configurator))
                        {
                            configureActions.Add(configurator);
                        }
                    }

                    configuredActivator = (unconfiguredActivator, configureActions.ToArray());
                    _activators = _activators.SetItem(grainType, configuredActivator);
                }

                return configuredActivator;
            }
        }
    }

    /// <summary>
    /// 为指定 Grain 类型提供 <see cref="IGrainContextActivator"/>。
    /// </summary>
    public interface IGrainContextActivatorProvider
    {
        /// <summary>
        /// 返回给定 Grain 类型的 Grain 上下文激活器。
        /// </summary>
        /// <param name="grainType">Grain 的类型。</param>
        /// <param name="activator">Grain 上下文激活器。</param>
        /// <returns>若找到合适的激活器则为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
        bool TryGet(GrainType grainType, [NotNullWhen(true)] out IGrainContextActivator? activator);
    }

    /// <summary>
    /// 为给定的 Grain 地址创建 Grain 上下文。
    /// </summary>
    public interface IGrainContextActivator
    {
        /// <summary>
        /// 为给定的 Grain 地址创建 Grain 上下文。
        /// </summary>
        /// <param name="address">Grain 地址。</param>
        /// <param name="configureActions">必须在开始构造 Grain 之前用于配置上下文的操作。</param>
        /// <returns>新创建的 Grain 上下文。</returns>
        public IGrainContext CreateContext(GrainAddress address, IConfigureGrainContext[] configureActions);
    }

    /// <summary>
    /// 为提供的 Grain 类型提供 <see cref="IConfigureGrainContext"/> 实例。
    /// </summary>
    public interface IConfigureGrainContextProvider
    {
        /// <summary>
        /// 为提供的 Grain 类型提供 <see cref="IConfigureGrainContext" /> 实例。
        /// </summary>
        /// <param name="grainType">Grain 的类型。</param>
        /// <param name="properties">Grain 属性。</param>
        /// <param name="configurator">配置提供程序。</param>
        /// <returns>若找到配置提供程序则为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
        bool TryGetConfigurator(GrainType grainType, GrainProperties properties, [NotNullWhen(true)] out IConfigureGrainContext? configurator);
    }

    /// <summary>
    /// 配置提供的 Grain 上下文。
    /// </summary>
    public interface IConfigureGrainContext
    {
        /// <summary>
        /// 配置提供的 Grain 上下文。
        /// </summary>
        /// <param name="context">Grain 上下文。</param>
        void Configure(IGrainContext context);
    }

    /// <summary>
    /// 解析给定 Grain 类型的所有实例共有的组件。
    /// </summary>
    public class GrainTypeSharedContextResolver
    {
        private readonly ConcurrentDictionary<GrainType, GrainTypeSharedContext> _components = new();
        private readonly IConfigureGrainTypeComponents[] _configurators;
        private readonly GrainPropertiesResolver _grainPropertiesResolver;
        private readonly Func<GrainType, GrainTypeSharedContext> _createFunc;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="GrainTypeSharedContextResolver"/> 类的新实例。
        /// </summary>
        /// <param name="configurators">Grain 类型组件配置提供程序。</param>
        /// <param name="grainPropertiesResolver">Grain 属性解析器。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
        public GrainTypeSharedContextResolver(
            IEnumerable<IConfigureGrainTypeComponents> configurators,
            GrainPropertiesResolver grainPropertiesResolver,
            IServiceProvider serviceProvider)
        {
            _configurators = configurators.ToArray();
            _grainPropertiesResolver = grainPropertiesResolver;
            _serviceProvider = serviceProvider;
            _createFunc = Create;
        }

        /// <summary>
        /// 返回提供的 Grain 类型的共享 Grain 组件。
        /// </summary>
        /// <param name="grainType">Grain 类型。</param>
        /// <returns>提供的类型的所有 Grain 的共享上下文。</returns>
        public GrainTypeSharedContext GetComponents(GrainType grainType) => _components.GetOrAdd(grainType, _createFunc);

        private GrainTypeSharedContext Create(GrainType grainType)
        {
            var result = ActivatorUtilities.CreateInstance<GrainTypeSharedContext>(_serviceProvider, grainType);
            var properties = _grainPropertiesResolver.GetGrainProperties(grainType);
            foreach (var configurator in _configurators)
            {
                configurator.Configure(grainType, properties, result);
            }

            result.CompleteConfiguration();
            return result;
        }
    }

    /// <summary>
    /// 配置给定 Grain 类型的所有实例共有的共享组件。
    /// </summary>
    public interface IConfigureGrainTypeComponents
    {
        /// <summary>
        /// 配置给定 Grain 类型的所有实例共有的共享组件。
        /// </summary>
        /// <param name="grainType">Grain 类型。</param>
        /// <param name="properties">Grain 属性。</param>
        /// <param name="shared">指定类型的所有 Grain 的共享上下文。</param>
        void Configure(GrainType grainType, GrainProperties properties, GrainTypeSharedContext shared);
    }

    internal class ReentrantSharedComponentsConfigurator : IConfigureGrainTypeComponents
    {
        public void Configure(GrainType grainType, GrainProperties properties, GrainTypeSharedContext shared)
        {
            if (properties.Properties.TryGetValue(WellKnownGrainTypeProperties.Reentrant, out var value) && bool.Parse(value))
            {
                var component = shared.GetComponent<GrainCanInterleave>();
                if (component is null)
                {
                    component = new GrainCanInterleave();
                    shared.SetComponent<GrainCanInterleave>(component);
                }

                component.MayInterleavePredicates.Add(ReentrantPredicate.Instance);
            }
        }
    }

    internal class MayInterleaveConfiguratorProvider : IConfigureGrainContextProvider
    {
        private readonly GrainClassMap _grainClassMap;

        public MayInterleaveConfiguratorProvider(GrainClassMap grainClassMap)
        {
            _grainClassMap = grainClassMap;
        }

        public bool TryGetConfigurator(GrainType grainType, GrainProperties properties, [NotNullWhen(true)] out IConfigureGrainContext? configurator)
        {
            if (properties.Properties.TryGetValue(WellKnownGrainTypeProperties.MayInterleavePredicate, out _)
                && _grainClassMap.TryGetGrainClass(grainType, out var grainClass))
            {
                var predicate = GetMayInterleavePredicate(grainClass);
                configurator = new MayInterleaveConfigurator(predicate);
                return true;
            }

            configurator = null;
            return false;
        }

        /// <summary>
        /// 根据类是否标记了 <see cref="MayInterleaveAttribute"/> 返回交错执行谓词。
        /// </summary>
        /// <param name="grainType">Grain 类。</param>
        private static IMayInterleavePredicate? GetMayInterleavePredicate(Type grainType)
        {
            var attribute = grainType.GetCustomAttribute<MayInterleaveAttribute>();
            if (attribute is null)
            {
                return null;
            }

            // 此处
            var callbackMethodName = attribute.CallbackMethodName;
            var method = grainType.GetMethod(callbackMethodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Class {grainType.FullName} doesn't declare public method " +
                    $"with name {callbackMethodName} specified in MayInterleave attribute");
            }

            if (method.ReturnType != typeof(bool) ||
                method.GetParameters().Length != 1 ||
                method.GetParameters()[0].ParameterType != typeof(IInvokable))
            {
                throw new InvalidOperationException(
                    $"Wrong signature of callback method {callbackMethodName} " +
                    $"specified in MayInterleave attribute for grain class {grainType.FullName}. \n" +
                    $"Expected: public bool {callbackMethodName}(IInvokable req)");
            }

            if (method.IsStatic)
            {
                return new MayInterleaveStaticPredicate(method.CreateDelegate<Func<IInvokable, bool>>());
            }

            var predicateType = typeof(MayInterleaveInstancedPredicate<>).MakeGenericType(grainType);
            return (IMayInterleavePredicate?)Activator.CreateInstance(predicateType, method);
        }
    }

    internal interface IMayInterleavePredicate
    {
        bool Invoke(object instance, IInvokable bodyObject);
    }

    internal class ReentrantPredicate : IMayInterleavePredicate
    {
        private ReentrantPredicate()
        {
        }

        public static ReentrantPredicate Instance { get; } = new();

        public bool Invoke(object _, IInvokable bodyObject) => true;
    }

    internal class MayInterleaveStaticPredicate : IMayInterleavePredicate
    {
        private readonly Func<IInvokable, bool> _mayInterleavePredicate;

        public MayInterleaveStaticPredicate(Func<IInvokable, bool> mayInterleavePredicate)
        {
            _mayInterleavePredicate = mayInterleavePredicate;
        }

        public bool Invoke(object _, IInvokable bodyObject) => _mayInterleavePredicate(bodyObject);
    }

    internal class MayInterleaveInstancedPredicate<T> : IMayInterleavePredicate where T : class
    {
        private readonly Func<T, IInvokable, bool> _mayInterleavePredicate;

        public MayInterleaveInstancedPredicate(MethodInfo mayInterleavePredicateInfo)
        {
            _mayInterleavePredicate = mayInterleavePredicateInfo.CreateDelegate<Func<T, IInvokable, bool>>();
        }

        public bool Invoke(object instance, IInvokable bodyObject) => _mayInterleavePredicate((T)instance, bodyObject);
    }

    internal class MayInterleaveConfigurator : IConfigureGrainContext
    {
        private readonly IMayInterleavePredicate? _mayInterleavePredicate;

        public MayInterleaveConfigurator(IMayInterleavePredicate? mayInterleavePredicate)
        {
            _mayInterleavePredicate = mayInterleavePredicate;
        }

        public void Configure(IGrainContext context)
        {
            var component = context.GetComponent<GrainCanInterleave>();
            if (component is null)
            {
                component = new GrainCanInterleave();
                context.SetComponent<GrainCanInterleave>(component);
            }

            component.MayInterleavePredicates.Add(_mayInterleavePredicate);
        }
    }

    internal class GrainCanInterleave
    {
        public List<IMayInterleavePredicate?> MayInterleavePredicates { get; } = new List<IMayInterleavePredicate?>();
        public bool MayInterleave(object? instance, Message message)
        {
            foreach (var predicate in this.MayInterleavePredicates)
            {
                if (predicate!.Invoke(instance!, (IInvokable)message.BodyObject!)) return true;
            }

            return false;
        }
    }
}
