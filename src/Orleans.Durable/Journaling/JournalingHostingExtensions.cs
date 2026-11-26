using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Durable.Journaling.Json;
using Orleans.Metadata;
using Orleans.Providers;
using Orleans.Runtime;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供配置日记服务的扩展。
/// </summary>
public static class JournalingHostingExtensions
{
    /// <summary>
    /// 添加日记所需的服务、耐久状态类型和日记格式。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    /// <remarks>
    /// 解决标准 grain-范围 <see cref="IDurableStateManager"/> 将它纳入 grain 生命周期。
    /// 其登记为可持久国家的国家在 <see cref="GrainLifecycleStage.SetupState"/>,之前 grain 启动
    /// <see cref="JournalStorageProviderAttribute"/> 选择激活中所有耐久状态的提供者。
    /// Grain 类型没有明确的选择使用 <see cref="ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/>。
    /// 通过 <see cref="IJournaledStateManagerFactory"/> 创建的管理人员拥有呼叫者拥有的初始化和处置。
    /// </remarks>
    public static ISiloBuilder AddJournaling(this ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<JournaledStateManagerOptions>();
        builder.Services.TryAddSingleton(static serviceProvider =>
            serviceProvider.GetService<OrleansInstruments>() is { } instruments
                ? new JournalingInstruments(instruments)
                : JournalingInstruments.CreateForDirectConstruction());
        builder.Services.TryAddSingleton<JournaledStateManagerShared>();
        builder.Services.TryAddScoped<IJournaledStateManager>(static services =>
        {
            var context = services.GetRequiredService<IGrainContext>();
            var properties = services.GetRequiredService<GrainPropertiesResolver>().GetGrainProperties(context.GrainId.Type);
            var providerName = properties.Properties.TryGetValue(JournalStorageProviderAttribute.PropertyKey, out var selection)
                ? selection
                : ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;
            if (string.IsNullOrWhiteSpace(providerName))
            {
                throw new OrleansConfigurationException(
                    $"Journal storage provider '{providerName}' for grain type '{context.GrainId.Type}' must have a non-empty name.");
            }

            var storageProvider = string.Equals(providerName, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal)
                ? services.GetService<IJournalStorageProvider>()
                : services.GetKeyedService<IJournalStorageProvider>(providerName);
            if (storageProvider is null)
            {
                throw new OrleansConfigurationException(
                    $"Journal storage provider '{providerName}' selected for grain type '{context.GrainId.Type}' is not registered. " +
                    "Register it using AddJournalStorage or a provider-specific registration method.");
            }

            return new DurableStateManager(
                services.GetRequiredService<JournaledStateManagerShared>(),
                storageProvider,
                context);
        });
        builder.Services.TryAddScoped<IDurableStateManager>(static services => (IDurableStateManager)services.GetRequiredService<IJournaledStateManager>());
        builder.Services.TryAddSingleton<IJournaledStateManagerFactory>(static services =>
            services.GetKeyedService<IJournaledStateManagerFactory>(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME)
                ?? ActivatorUtilities.CreateInstance<JournaledStateManagerFactory>(services));

        // 将 JSON 登记为默认格式的家族,并保持现有数据的 Orleans 二进制可用。
        builder.Services.AddJsonJournalFormat(tryAdd: true);
        TryAddOrleansBinaryJournalingFormat(builder.Services);

        builder.Services.TryAddKeyedScoped(typeof(IDurableDictionary<,>), KeyedService.AnyKey, typeof(DurableDictionary<,>));
        builder.Services.TryAddKeyedScoped(typeof(IDurableList<>), KeyedService.AnyKey, typeof(DurableList<>));
        builder.Services.TryAddKeyedScoped(typeof(IDurableQueue<>), KeyedService.AnyKey, typeof(DurableQueue<>));
        builder.Services.TryAddKeyedScoped(typeof(IDurableSet<>), KeyedService.AnyKey, typeof(DurableSet<>));
        builder.Services.TryAddKeyedScoped(typeof(IDurableLog<>), KeyedService.AnyKey, typeof(DurableLog<>));
        builder.Services.TryAddKeyedScoped(typeof(IDurableValue<>), KeyedService.AnyKey, typeof(DurableValue<>));
        builder.Services.TryAddKeyedScoped(typeof(IPersistentState<>), KeyedService.AnyKey, typeof(JournaledPersistentState<>));
        builder.Services.TryAddKeyedScoped(typeof(IDurableTaskCompletionSource<>), KeyedService.AnyKey, typeof(DurableTaskCompletionSource<>));
        return builder;
    }

    /// <summary>
    /// 登记一项自定耐用国家合同及其州机器实施。
    /// </summary>
    /// <typeparam name="TState">申请国家合同</typeparam>
    /// <typeparam name="TImplementation">国家机器的安装</typeparam>
    /// <param name="services">服务收藏。</param>
    /// <returns>服务收藏。</returns>
    /// <remarks>
    /// 需要 <see cref="AddJournaling"/>注册的日记服务。
    /// 构造依赖性从 grain 激活的服务范围中解决。
    /// 建筑需要国名时,使用工厂超负荷。
    /// 管理人员登记了案件;其建造者无需自己登记。
    /// </remarks>
    public static IServiceCollection AddStateMachine<TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services)
        where TState : class
        where TImplementation : class, TState, IStateMachine
    {
        ArgumentNullException.ThrowIfNull(services);
        var factory = ActivatorUtilities.CreateFactory<TImplementation>([]);
        return services.AddStateMachine<TState, TImplementation>((serviceProvider, _) => factory(serviceProvider, null));
    }

    /// <summary>
    /// 注册一家工厂,以签订耐用关税的国家合同。
    /// </summary>
    /// <typeparam name="TState">申请国家合同</typeparam>
    /// <typeparam name="TImplementation">国家机器的安装</typeparam>
    /// <param name="services">服务收藏。</param>
    /// <param name="factory">使用激活的服务范围和稳定状态名称创建国家组成部分。</param>
    /// <returns>服务收藏。</returns>
    /// <remarks>
    /// 需要 <see cref="AddJournaling"/>注册的日记服务。
    /// 注射键盘,经理回复同一案件 经理登记州政府
    /// 工厂不能被援引为现有的兼容状态
    /// </remarks>
    public static IServiceCollection AddStateMachine<TState, TImplementation>(
        this IServiceCollection services,
        Func<IServiceProvider, string, TImplementation> factory)
        where TState : class
        where TImplementation : class, TState, IStateMachine
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedScoped<TState>(KeyedService.AnyKey, (serviceProvider, key) =>
        {
            if (key is not string name || string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A durable state service key must be a non-empty string.", nameof(key));
            }

            if (serviceProvider.GetRequiredService<IDurableStateManager>() is not DurableStateManager manager)
            {
                throw new InvalidOperationException("Custom state machine factories require the grain's durable state manager registered by AddJournaling.");
            }

            return manager.GetOrAddState<TState, TImplementation>(name, factory);
        });
        return services;
    }

    /// <summary>
    /// 注册一个有名的日记存储供应商及其国家经理厂。
    /// </summary>
    /// <typeparam name="TProvider">提供方执行类型。</typeparam>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="factory">制造单吨供应商的工厂</param>
    /// <returns>下 silo 建筑商</returns>
    /// <remarks>
    /// 如果 <typeparamref name="TProvider"/> 执行 <see cref="IJournalStorageCatalog"/> 或
    /// <see cref="ILifecycleParticipant{TLifecycleObservable}"/>,这些服务是为同一供应商登记的。
    /// 所有提供者都使用共享日记格式配置。
    /// 约束性<see cref="ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/> 还提供无锁服务。
    /// 默认约束性荣誉不设密钥单子 <see cref="IJournalStorageProvider"/> 自定义。 替换
    /// 提供方必须执行 <typeparamref name="TProvider"/>申报的目录和生命周期合同。
    /// 重复使用同名和执行型号的登记保留原工厂。
    /// </remarks>
    /// <exception cref="ArgumentNullException">建筑工或工厂无效。</exception>
    /// <exception cref="ArgumentException">服务商名称为空或空白。</exception>
    /// <exception cref="InvalidOperationException">名称已经注册为另一类型供应商。</exception>
    public static ISiloBuilder AddJournalStorage<TProvider>(
        this ISiloBuilder builder,
        string name,
        Func<IServiceProvider, TProvider> factory)
        where TProvider : class, IJournalStorageProvider
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        var services = builder.Services;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(JournalStorageRegistration)
                && descriptor.ImplementationInstance is JournalStorageRegistration registration
                && string.Equals(registration.Name, name, StringComparison.Ordinal))
            {
                if (registration.ProviderType != typeof(TProvider))
                {
                    throw new InvalidOperationException(
                        $"Journal storage provider '{name}' is already registered with implementation type " +
                        $"'{registration.ProviderType.FullName}' and cannot be registered as '{typeof(TProvider).FullName}'.");
                }

                return builder;
            }
        }

        if (services.Any(descriptor => descriptor.IsKeyedService
            && Equals(descriptor.ServiceKey, name)
            && (descriptor.ServiceType == typeof(IJournalStorageProvider)
                || descriptor.ServiceType == typeof(IJournalStorageCatalog)
                || descriptor.ServiceType == typeof(IJournaledStateManagerFactory))))
        {
            throw new InvalidOperationException($"Journal storage provider '{name}' already has keyed services registered.");
        }

        builder.AddJournaling();
        services.AddSingleton(new JournalStorageRegistration(name, typeof(TProvider)));
        var isDefault = string.Equals(name, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal);
        if (isDefault)
        {
            services.TryAddSingleton(factory);
            services.TryAddSingleton<IJournalStorageProvider>(static serviceProvider => serviceProvider.GetRequiredService<TProvider>());
            services.AddKeyedSingleton<IJournalStorageProvider>(name, static (serviceProvider, _) =>
                serviceProvider.GetRequiredService<IJournalStorageProvider>());
        }
        else
        {
            services.AddKeyedSingleton<IJournalStorageProvider>(name, (serviceProvider, _) => factory(serviceProvider));
        }

        services.AddKeyedSingleton<IJournaledStateManagerFactory>(name, (serviceProvider, _) =>
            new JournaledStateManagerFactory(
                serviceProvider.GetRequiredService<JournaledStateManagerShared>(),
                serviceProvider.GetRequiredKeyedService<IJournalStorageProvider>(name)));

        var supportsCatalog = typeof(IJournalStorageCatalog).IsAssignableFrom(typeof(TProvider));
        if (supportsCatalog)
        {
            services.AddKeyedSingleton<IJournalStorageCatalog>(name, (serviceProvider, _) =>
                (IJournalStorageCatalog)serviceProvider.GetRequiredKeyedService<IJournalStorageProvider>(name));
        }

        if (typeof(ILifecycleParticipant<ISiloLifecycle>).IsAssignableFrom(typeof(TProvider)))
        {
            services.AddSingleton(serviceProvider =>
                (ILifecycleParticipant<ISiloLifecycle>)serviceProvider.GetRequiredKeyedService<IJournalStorageProvider>(name));
        }

        if (isDefault && supportsCatalog)
        {
            services.TryAddSingleton<IJournalStorageCatalog>(serviceProvider =>
                serviceProvider.GetRequiredKeyedService<IJournalStorageCatalog>(name));
        }

        return builder;
    }

    /// <summary>
    /// 将不稳定的模拟日记存储作为默认提供者注册。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddVolatileJournalStorage(this ISiloBuilder builder)
        => builder.AddVolatileJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);

    /// <summary>
    /// 注册一个独立名称为不稳定的杂志存储供应商。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddVolatileJournalStorage(this ISiloBuilder builder, string name)
        => builder.AddVolatileJournalStorage(name, configureOptions: null);

    /// <summary>
    /// 注册默认的波动日记提供商,并设定可配置快照阈值。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">配置供应商的快照阈值。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddVolatileJournalStorage(this ISiloBuilder builder, Action<VolatileJournalStorageOptions> configureOptions)
        => builder.AddVolatileJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);

    /// <summary>
    /// 注册一个名为波动性期刊供应商,可配置快报门槛。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configureOptions">可选配置供应商的快照阈值。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddVolatileJournalStorage(
        this ISiloBuilder builder, string name, Action<VolatileJournalStorageOptions>? configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var options = builder.Services.AddJournalStorageOptions<VolatileJournalStorageOptions>(name)
            .Validate(static configuration => configuration.MaxAppendsBeforeSnapshot > 0,
                "MaxAppendsBeforeSnapshot must be positive.")
            .Validate(static configuration => configuration.MaxBytesBeforeSnapshot > 0,
                "MaxBytesBeforeSnapshot must be positive.");
        if (configureOptions is not null)
        {
            options.Configure(configureOptions);
        }

        return builder.AddJournalStorage(name, services => new VolatileJournalStorageProvider(
            services.GetRequiredService<IOptions<JournaledStateManagerOptions>>(),
            services.GetJournalStorageOptions<VolatileJournalStorageOptions>(name),
            services.GetService<OrleansInstruments>()));
    }

    private sealed record JournalStorageRegistration(string Name, Type ProviderType);

    internal static OptionsBuilder<TOptions> AddJournalStorageOptions<TOptions>(this IServiceCollection services, string name)
        where TOptions : class
        => services.AddOptions<TOptions>(
            string.Equals(name, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal)
                ? Options.DefaultName
                : name);

    internal static IOptions<TOptions> GetJournalStorageOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(this IServiceProvider services, string name)
        where TOptions : class
        => string.Equals(name, ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, StringComparison.Ordinal)
            ? services.GetRequiredService<IOptions<TOptions>>()
            : Options.Create(services.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name));

    private static void TryAddOrleansBinaryJournalingFormat(IServiceCollection services)
    {
        var key = JournalFormatServices.ValidateJournalFormatKey(OrleansBinaryJournalFormat.JournalFormatKey);

        services.TryAddSingleton<OrleansBinaryJournalFormat>();
        services.TryAddKeyedSingleton<IJournalFormat>(key, static (sp, _) => sp.GetRequiredService<OrleansBinaryJournalFormat>());
        services.TryAddSingleton<IJournalFormat>(static sp => sp.GetRequiredService<OrleansBinaryJournalFormat>());

        services.TryAddKeyedSingleton(typeof(IDurableDictionaryCommandCodec<,>), key, typeof(OrleansBinaryDurableDictionaryCommandCodec<,>));
        services.TryAddKeyedSingleton(typeof(IDurableListCommandCodec<>), key, typeof(OrleansBinaryDurableListCommandCodec<>));
        services.TryAddKeyedSingleton(typeof(IDurableQueueCommandCodec<>), key, typeof(OrleansBinaryDurableQueueCommandCodec<>));
        services.TryAddKeyedSingleton(typeof(IDurableSetCommandCodec<>), key, typeof(OrleansBinaryDurableSetCommandCodec<>));
        services.TryAddKeyedSingleton(typeof(IDurableLogCommandCodec<>), key, typeof(OrleansBinaryDurableLogCommandCodec<>));
        services.TryAddKeyedSingleton(typeof(IDurableValueCommandCodec<>), key, typeof(OrleansBinaryDurableValueCommandCodec<>));
        services.TryAddKeyedSingleton(typeof(IPersistentStateCommandCodec<>), key, typeof(OrleansBinaryPersistentStateCommandCodec<>));
        services.TryAddKeyedSingleton(typeof(IDurableTaskCompletionSourceCommandCodec<>), key, typeof(OrleansBinaryDurableTaskCompletionSourceCommandCodec<>));
    }
}
