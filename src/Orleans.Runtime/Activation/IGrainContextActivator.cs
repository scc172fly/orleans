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
    /// 创建 grain 语境的中心点。
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
        /// <param name="providers">下 grain 环境活性器提供者。</param>
        /// <param name="configureContextActions">下 <see cref="IConfigureGrainContext"/> 提供商。</param>
        /// <param name="grainPropertiesResolver"> grain 属性解析器。</param>
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
        /// 创建新 grain 提供的语背景 grain 地址。
        /// </summary>
        /// <param name="address"> grain 地址。</param>
        /// <returns> grain 上下文。</returns>
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
    /// 提供a <see cref="IGrainContextActivator"/> 指定 grain 类型。
    /// </summary>
    public interface IGrainContextActivatorProvider
    {
        /// <summary>
        /// 回返a grain 给定的 grain 类型。
        /// </summary>
        /// <param name="grainType">类型 grain.</param>
        /// <param name="activator"> grain 上下文激活器。</param>
        /// <returns><see langword="true"/> 如果找到适当的活动方,否则 <see langword="false"/>。</returns>
        bool TryGet(GrainType grainType, [NotNullWhen(true)] out IGrainContextActivator? activator);
    }

    /// <summary>
    /// 创建 grain 给给 grain 地址。
    /// </summary>
    public interface IGrainContextActivator
    {
        /// <summary>
        /// 创建 grain 给给 grain 地址。
        /// </summary>
        /// <param name="address"> grain 地址。</param>
        /// <param name="configureActions">配置前上下文的动作 grain 建筑工程开始</param>
        /// <returns>新建的 grain 上下文。</returns>
        public IGrainContext CreateContext(GrainAddress address, IConfigureGrainContext[] configureActions);
    }

    /// <summary>
    /// 提供a <see cref="IConfigureGrainContext"/> 提供的情况 grain 类型。
    /// </summary>
    public interface IConfigureGrainContextProvider
    {
        /// <summary>
        /// 提供a <see cref="IConfigureGrainContext" /> 提供的情况 grain 类型。
        /// </summary>
        /// <param name="grainType">类型 grain.</param>
        /// <param name="properties"> grain 属性。</param>
        /// <param name="configurator">配置提供者。</param>
        /// <returns><see langword="true"/> 如果找到配置供应商,则 <see langword="false"/> 。</returns>
        bool TryGetConfigurator(GrainType grainType, GrainProperties properties, [NotNullWhen(true)] out IConfigureGrainContext? configurator);
    }

    /// <summary>
    /// 配置所提供的 grain 上下文。
    /// </summary>
    public interface IConfigureGrainContext
    {
        /// <summary>
        /// 配置所提供的 grain 上下文。
        /// </summary>
        /// <param name="context"> grain 上下文。</param>
        void Configure(IGrainContext context);
    }

    /// <summary>
    /// 确定特定类型 grain 的所有情况中通用的组件。
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
        /// <param name="configurators">下 grain 类型 构件配置提供者。</param>
        /// <param name="grainPropertiesResolver"> grain 属性解析器。</param>
        /// <param name="serviceProvider">服务供应商。</param>
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
        /// 共享的回返 grain 提供的组件 grain 类型。
        /// </summary>
        /// <param name="grainType"> grain 类型。</param>
        /// <returns>人人共享的共同环境 grains 提供的类型。</returns>
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
    /// 配置共享组件,这些组件在给定 grain 类型的所有情况下都是常见的。
    /// </summary>
    public interface IConfigureGrainTypeComponents
    {
        /// <summary>
        /// 配置共享组件,这些组件在给定 grain 类型的所有情况下都是常见的。
        /// </summary>
        /// <param name="grainType"> grain 类型。</param>
        /// <param name="properties"> grain 属性。</param>
        /// <param name="shared">人人共享的共同环境 grains 指定类型。</param>
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
        /// 返回跨叶前端,取决于类是否标有 <see cref="MayInterleaveAttribute"/> 。
        /// </summary>
        /// <param name="grainType">Grain 类.</param>
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
