using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Scheduler;
using Orleans.Runtime.Placement;
using Orleans.Serialization.Session;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Runtime;

/// <summary>
/// Grain 类型的所有实例共享的功能。
/// </summary>
public sealed class GrainTypeSharedContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, object> _components = [];
    private Action<IGrainContext>? _activationSetup;
    private bool _configurationCompleted;

    /// <summary>
    /// 初始化 <see cref="GrainTypeSharedContext"/> 类的新实例。
    /// </summary>
    /// <param name="grainType">此上下文表示的 Grain 类型。</param>
    /// <param name="clusterManifestProvider">集群 Grain 清单提供程序。</param>
    /// <param name="grainClassMap">Grain 类型到实现类的映射。</param>
    /// <param name="placementStrategyResolver">Grain 类型放置策略的解析器。</param>
    /// <param name="messagingOptions">Silo 消息传递选项。</param>
    /// <param name="collectionOptions">激活实例回收选项。</param>
    /// <param name="schedulingOptions">Grain 调度选项。</param>
    /// <param name="grainRuntime">Grain 运行时。</param>
    /// <param name="loggerFactory">日志记录器工厂。</param>
    /// <param name="grainReferenceActivator">Grain 引用激活器。</param>
    /// <param name="serviceProvider">服务提供程序。</param>
    /// <param name="serializerSessionPool">序列化器会话池。</param>
    /// <exception cref="KeyNotFoundException">未为 <paramref name="grainType"/> 注册 Grain 实现类。</exception>
    public GrainTypeSharedContext(
        GrainType grainType,
        IClusterManifestProvider clusterManifestProvider,
        GrainClassMap grainClassMap,
        PlacementStrategyResolver placementStrategyResolver,
        IOptions<SiloMessagingOptions> messagingOptions,
        IOptions<GrainCollectionOptions> collectionOptions,
        IOptions<SchedulingOptions> schedulingOptions,
        IGrainRuntime grainRuntime,
        ILoggerFactory loggerFactory,
        GrainReferenceActivator grainReferenceActivator,
        IServiceProvider serviceProvider,
        SerializerSessionPool serializerSessionPool)
    {
        if (!grainClassMap.TryGetGrainClass(grainType, out var grainClass))
        {
            throw new KeyNotFoundException($"Could not find corresponding grain class for grain of type {grainType}");
        }

        CatalogInstruments = serviceProvider.GetRequiredService<CatalogInstruments>();
        GrainTypeMetrics = CatalogInstruments.GetGrainTypeMetrics(grainType);
        Interlocked.CompareExchange(ref GrainTypeMetrics.GrainClassName, RuntimeTypeNameFormatter.Format(grainClass), null);
        SerializerSessionPool = serializerSessionPool;
        Logger = loggerFactory.CreateLogger("Orleans.Grain");
        SchedulerLogger = loggerFactory.CreateLogger<WorkItemGroup>();
        MessagingOptions = messagingOptions.Value;
        GrainReferenceActivator = grainReferenceActivator;
        _serviceProvider = serviceProvider;
        MaxWarningRequestProcessingTime = messagingOptions.Value.ResponseTimeout.Multiply(5);
        MaxRequestProcessingTime = messagingOptions.Value.MaxRequestProcessingTime;
        PlacementStrategy = placementStrategyResolver.GetPlacementStrategy(grainType);
        var grainDirectoryResolver = serviceProvider.GetRequiredService<GrainDirectoryResolver>();
        GrainDirectory = PlacementStrategy.IsUsingGrainDirectory ? grainDirectoryResolver.Resolve(grainType) : null;
        SchedulingOptions = schedulingOptions.Value;
        Runtime = grainRuntime;
        MigrationManager = _serviceProvider.GetService<IActivationMigrationManager>();
        GrainInstruments = serviceProvider.GetRequiredService<GrainInstruments>();
        MessagingProcessingInstruments = serviceProvider.GetRequiredService<MessagingProcessingInstruments>();

        CollectionAgeLimit = GetCollectionAgeLimit(
            grainType,
            grainClass,
            clusterManifestProvider.LocalGrainManifest,
            collectionOptions.Value);
    }

    /// <summary>
    /// 获取 Grain 实例类型名称（如果可用）。
    /// </summary>
    public string GrainTypeName => GrainTypeMetrics.GrainClassName!;

    /// <summary>
    /// 添加为此 Grain 类型的每个激活实例运行的同步初始化操作。
    /// </summary>
    /// <param name="setup">初始化操作。</param>
    /// <remarks>
    /// <para>
    /// 在 <see cref="IConfigureGrainTypeComponents.Configure"/> 期间添加操作。Orleans 将组合后的操作缓存
    /// 在此共享上下文中，并在 Grain 构造函数执行完毕且
    /// <see cref="IGrainContext.GrainInstance"/> 已赋值之后，Grain 对象参与生命周期之前，
    /// 以及生命周期启动之前，按注册顺序运行这些操作。每项添加的操作在每个激活实例上执行一次，包括每个无状态工作者激活实例。
    /// </para>
    /// <para>
    /// 操作在激活实例之间共享，可以针对不同激活实例并发执行。确保捕获的
    /// 状态可安全地并发使用。在操作内部从 <see cref="IGrainContext.ActivationServices"/>
    /// 解析激活实例作用域的状态。操作可以使用 <see cref="ILifecycleParticipant{TLifecycleObservable}"/> 登记该状态，
    /// 或向 <see cref="IGrainContext.ObservableLifecycle"/> 订阅回调。
    /// 使用生命周期回调进行异步启动和关闭，并通过不同阶段实现有序工作。
    /// </para>
    /// <para>
    /// 初始化操作抛出异常会导致激活失败，跳过后续初始化操作及生命周期启动，并触发正常的
    /// Grain 和激活实例作用域清理。每项操作负责登记它所解析的服务。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">共享 Grain 类型配置已完成。</exception>
    public void AddActivationSetup(Action<IGrainContext> setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (_configurationCompleted)
        {
            throw new InvalidOperationException("Activation setup actions must be added during grain type configuration.");
        }

        _activationSetup += setup;
    }

    internal void CompleteConfiguration() => _configurationCompleted = true;

    internal void SetupActivation(IGrainContext context) => _activationSetup?.Invoke(context);

    internal GrainTypeMetrics GrainTypeMetrics { get; }
    internal string GrainTypeMetricName => GrainTypeMetrics.GrainTypeTagValue;
    internal CatalogInstruments CatalogInstruments { get; }
    internal GrainInstruments GrainInstruments { get; }
    internal MessagingProcessingInstruments MessagingProcessingInstruments { get; }

    private static TimeSpan GetCollectionAgeLimit(GrainType grainType, Type grainClass, GrainManifest siloManifest, GrainCollectionOptions collectionOptions)
    {
        if (siloManifest.Grains.TryGetValue(grainType, out var properties)
            && properties.Properties.TryGetValue(WellKnownGrainTypeProperties.IdleDeactivationPeriod, out var idleTimeoutString))
        {
            if (string.Equals(idleTimeoutString, WellKnownGrainTypeProperties.IndefiniteIdleDeactivationPeriodValue, StringComparison.Ordinal))
            {
                return Timeout.InfiniteTimeSpan;
            }

            if (TimeSpan.TryParse(idleTimeoutString, out var result))
            {
                return result;
            }
        }

        if (collectionOptions.ClassSpecificCollectionAge.TryGetValue(grainClass.FullName!, out var specified))
        {
            return specified;
        }

        return collectionOptions.CollectionAge;
    }

    /// <summary>
    /// 获取组件。
    /// </summary>
    /// <typeparam name="TComponent">相应 <see cref="SetComponent{TComponent}"/> 调用中指定的类型。</typeparam>
    public TComponent? GetComponent<TComponent>()
    {
        if (typeof(TComponent) == typeof(PlacementStrategy) && PlacementStrategy is TComponent component)
        {
            return component;
        }

        if (typeof(TComponent) == typeof(ILogger))
        {
            return (TComponent)Logger;
        }

        if (_components is null) return default;
        _components.TryGetValue(typeof(TComponent), out var resultObj);
        return (TComponent?)resultObj;
    }

    /// <summary>
    /// 获取组件。
    /// </summary>
    /// <param name="componentType">组件的类型。</param>
    /// <returns>具有指定类型的组件。</returns>
    public object? GetComponent(Type componentType)
    {
        if (componentType == typeof(PlacementStrategy))
        {
            return PlacementStrategy;
        }

        if (componentType == typeof(ILogger))
        {
            return Logger;
        }

        if (_components is null) return default;
        _components.TryGetValue(componentType, out var resultObj);
        return resultObj;
    }

    /// <summary>
    /// 注册组件。
    /// </summary>
    /// <typeparam name="TComponent">可用作 <see cref="GetComponent{TComponent}"/> 的键的类型。</typeparam>
    public void SetComponent<TComponent>(TComponent? instance)
    {
        if (instance == null)
        {
            _components.Remove(typeof(TComponent));
            return;
        }

        _components[typeof(TComponent)] = instance;
    }

    /// <summary>
    /// 获取空闲 Grain 达到回收条件之前所需的时长。
    /// </summary>
    public TimeSpan CollectionAgeLimit { get; }

    /// <summary>
    /// 获取日志记录器。
    /// </summary>
    public ILogger Logger { get; }

    internal ILogger SchedulerLogger { get; }

    /// <summary>
    /// 获取序列化器会话池。
    /// </summary>
    public SerializerSessionPool SerializerSessionPool { get; }

    /// <summary>
    /// 获取 Silo 消息传递选项。
    /// </summary>
    public SiloMessagingOptions MessagingOptions { get; }

    /// <summary>
    /// 获取 Grain 引用激活器。
    /// </summary>
    public GrainReferenceActivator GrainReferenceActivator { get; }

    /// <summary>
    /// 获取请求持续处理到被视为卡住之前的最大预期时长。
    /// </summary>
    public TimeSpan MaxRequestProcessingTime { get; }

    /// <summary>
    /// 获取请求持续处理到可能记录警告之前的最大预期时长。
    /// </summary>
    public TimeSpan MaxWarningRequestProcessingTime { get; }

    /// <summary>
    /// 获取此类型的 Grain 所使用的放置策略。
    /// </summary>
    public PlacementStrategy PlacementStrategy { get; }

    /// <summary>
    /// 获取此类型的 Grain 所使用的 Grain 目录（如果此类型使用 Grain 目录）。
    /// </summary>
    public IGrainDirectory? GrainDirectory { get; }

    /// <summary>
    /// 获取调度选项。
    /// </summary>
    public SchedulingOptions SchedulingOptions { get; }

    /// <summary>
    /// 获取 Grain 运行时。
    /// </summary>
    public IGrainRuntime Runtime { get; }

    /// <summary>
    /// 获取本地激活实例迁移管理器。
    /// </summary>
    internal IActivationMigrationManager? MigrationManager { get; }

    /// <summary>
    /// 获取内部 Grain 运行时。
    /// </summary>
    internal InternalGrainRuntime InternalRuntime => field ??= _serviceProvider.GetRequiredService<InternalGrainRuntime>();

    /// <summary>
    /// 创建激活实例时调用。
    /// </summary>
    /// <param name="grainContext">Grain 激活实例。</param>
    public void OnCreateActivation(IGrainContext grainContext)
    {
        GrainInstruments.IncrementGrainCounts(GrainTypeMetrics);
    }

    /// <summary>
    /// 释放激活实例时调用。
    /// </summary>
    /// <param name="grainContext">Grain 激活实例。</param>
    public void OnDestroyActivation(IGrainContext grainContext)
    {
        GrainInstruments.DecrementGrainCounts(GrainTypeMetrics);
    }
}

internal interface IActivationLifecycleObserver
{
    void OnCreateActivation(IGrainContext grainContext);
    void OnDestroyActivation(IGrainContext grainContext);
}
