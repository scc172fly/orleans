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
/// 功能由 grain 类型的所有情况分担。
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
    /// <param name="grainType">下 grain 由此上下文表示的类型。</param>
    /// <param name="clusterManifestProvider">集群提供方 grain 数据。</param>
    /// <param name="grainClassMap">从 grain 类型向执行类别绘图。</param>
    /// <param name="placementStrategyResolver">类型 grain 的安置策略的解决者。</param>
    /// <param name="messagingOptions"> silo 邮件发件选项。</param>
    /// <param name="collectionOptions">激活收集选项。</param>
    /// <param name="schedulingOptions">下 grain 列表选项。</param>
    /// <param name="grainRuntime">运行时间 grain 。</param>
    /// <param name="loggerFactory">伐木厂</param>
    /// <param name="grainReferenceActivator">下 grain 参考激活器。</param>
    /// <param name="serviceProvider">服务供应商。</param>
    /// <param name="serializerSessionPool">序列器会话集合。</param>
    /// <exception cref="KeyNotFoundException">A grain 用于不注册的 <paramref name="grainType"/>.</exception>
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
    /// 如果有,则获取 grain 示例类型名称。
    /// </summary>
    public string GrainTypeName => GrainTypeMetrics.GrainClassName!;

    /// <summary>
    /// 添加同步设置动作,运行此 grain 类型每次激活的操作。
    /// </summary>
    /// <param name="setup">设置动作。</param>
    /// <remarks>
    /// <para>
    /// <see cref="IConfigureGrainTypeComponents.Configure"/>中添加动作。 Orleans 缓存组成动作
    /// 与这一共同背景一起,在 grain 建造器完成后按登记顺序运行和运行。
    /// <see cref="IGrainContext.GrainInstance"/> 是在 grain 物体参与生命周期和
    /// 每种添加都运行一次每次激活,包括每个无国籍工人的激活。
    /// </para>
    /// <para>
    /// 不同激活时可以同时执行。
    /// 安全状态,以用于同时使用。解析 <see cref="IGrainContext.ActivationServices"/>中的启动范围状态
    /// 动作。一个动作可以使用 <see cref="ILifecycleParticipant{TLifecycleObservable}"/>注册状态
    /// 或订阅回调到 <see cref="IGrainContext.ObservableLifecycle"/>。
    /// 使用生命周期回调进行不同步的启动和关闭,并有不同的订单施工阶段。
    /// </para>
    /// <para>
    /// 设置例外失败激活, 跳过随后的设置动作和生命周期启动, 触发正常
    /// grain 和启动范围清理。每个行动都拥有它解决的服务注册。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">共享 grain 类型配置已完成。</exception>
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
    /// 得到一个组件。
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
    /// 得到一个组件。
    /// </summary>
    /// <param name="componentType">组件的类型。</param>
    /// <returns>带有指定类型的组成部分。</returns>
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
    /// 登记一个组成部分。
    /// </summary>
    /// <typeparam name="TComponent">可用于作为 <see cref="GetComponent{TComponent}"/>的密钥的类型。</typeparam>
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
    /// 获得闲置 grains 有资格被收藏的时间间隔。
    /// </summary>
    public TimeSpan CollectionAgeLimit { get; }

    /// <summary>
    /// 得到伐木者。
    /// </summary>
    public ILogger Logger { get; }

    internal ILogger SchedulerLogger { get; }

    /// <summary>
    /// 获得连环器会话池。
    /// </summary>
    public SerializerSessionPool SerializerSessionPool { get; }

    /// <summary>
    /// 获取 silo 信息选项。
    /// </summary>
    public SiloMessagingOptions MessagingOptions { get; }

    /// <summary>
    /// 获取 grain 引用激活器。
    /// </summary>
    public GrainReferenceActivator GrainReferenceActivator { get; }

    /// <summary>
    /// 获得最大时间,我们期望在被视作悬吊之前继续处理的请求。
    /// </summary>
    public TimeSpan MaxRequestProcessingTime { get; }

    /// <summary>
    /// 获得最大时间,我们期望在警告被登录之前继续处理的请求。
    /// </summary>
    public TimeSpan MaxWarningRequestProcessingTime { get; }

    /// <summary>
    /// 获取这种类型 grains 所使用的安置策略。
    /// </summary>
    public PlacementStrategy PlacementStrategy { get; }

    /// <summary>
    /// 得到 grain 使用的目录 grains 类型,如果使用 grain 目录。
    /// </summary>
    public IGrainDirectory? GrainDirectory { get; }

    /// <summary>
    /// 获取调度选项。
    /// </summary>
    public SchedulingOptions SchedulingOptions { get; }

    /// <summary>
    /// 得到 grain 运行时间。
    /// </summary>
    public IGrainRuntime Runtime { get; }

    /// <summary>
    /// 找当地启动移民经理
    /// </summary>
    internal IActivationMigrationManager? MigrationManager { get; }

    /// <summary>
    /// 获取内部 grain 运行时间。
    /// </summary>
    internal InternalGrainRuntime InternalRuntime => field ??= _serviceProvider.GetRequiredService<InternalGrainRuntime>();

    /// <summary>
    /// 请求创建启动程序
    /// </summary>
    /// <param name="grainContext">启动 grain 。</param>
    public void OnCreateActivation(IGrainContext grainContext)
    {
        GrainInstruments.IncrementGrainCounts(GrainTypeMetrics);
    }

    /// <summary>
    /// 启动处理后呼叫
    /// </summary>
    /// <param name="grainContext">启动 grain 。</param>
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
