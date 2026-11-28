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
/// 提供用于配置日志持久化服务的扩展方法。
/// </summary>
public static class JournalingHostingExtensions
{
    /// <summary>
    /// 添加日志持久化所需的服务、持久化状态类型及日志格式。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    /// <remarks>
    /// 解析标准 Grain 作用域的 <see cref="IDurableStateManager"/> 会将其加入 Grain 生命周期。
    /// 其已注册持久化状态在 <see cref="GrainLifecycleStage.SetupState"/> 阶段恢复，早于 Grain 激活。
    /// <see cref="JournalStorageProviderAttribute"/> 为激活实例中的所有持久化状态选择提供程序。
    /// 没有显式选择的 Grain 类型使用 <see cref="ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/>。
    /// 通过 <see cref="IJournaledStateManagerFactory"/> 创建的管理器由调用方负责初始化及释放。
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

        // 注册 JSON 为默认格式系列，并保留 Orleans 二进制格式供现有数据使用。
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
    /// 注册自定义持久化状态契约及其状态机实现。
    /// </summary>
    /// <typeparam name="TState">应用状态契约。</typeparam>
    /// <typeparam name="TImplementation">状态机实现。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    /// <remarks>
    /// 需要由 <see cref="AddJournaling"/> 注册的日志持久化服务。
    /// 构造函数依赖项从 Grain 激活实例的服务作用域解析。
    /// 构造需要状态名称时，使用工厂重载。
    /// 管理器负责注册实例；其构造函数无需自行注册。
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
    /// 注册自定义持久化状态契约的工厂。
    /// </summary>
    /// <typeparam name="TState">应用状态契约。</typeparam>
    /// <typeparam name="TImplementation">状态机实现。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="factory">使用其激活实例的服务作用域和稳定状态名称创建状态组件。</param>
    /// <returns>服务集合。</returns>
    /// <remarks>
    /// 需要由 <see cref="AddJournaling"/> 注册的日志持久化服务。
    /// 带键注入与管理器返回同一个命名实例。管理器注册状态，
    /// 并由激活实例作用域释放它。对于现有兼容状态，不调用工厂。
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
    /// 注册命名日志存储提供程序及其日志状态管理器工厂。
    /// </summary>
    /// <typeparam name="TProvider">提供程序实现类型。</typeparam>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="factory">创建单例提供程序的工厂。</param>
    /// <returns>Silo 构建器。</returns>
    /// <remarks>
    /// 如果 <typeparamref name="TProvider"/> 实现 <see cref="IJournalStorageCatalog"/> 或
    /// <see cref="ILifecycleParticipant{TLifecycleObservable}"/>，则为同一个提供程序注册这些服务。
    /// 所有提供程序使用共享的日志格式配置。
    /// <see cref="ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME"/> 绑定还提供无键服务。
    /// 默认绑定遵循无键单例 <see cref="IJournalStorageProvider"/> 的自定义配置。替换提供程序
    /// 必须实现 <typeparamref name="TProvider"/> 所声明的目录及生命周期契约。
    /// 使用相同名称和实现类型重复注册会保留原始工厂。
    /// </remarks>
    /// <exception cref="ArgumentNullException">构建器或工厂为 null。</exception>
    /// <exception cref="ArgumentException">提供程序名称为空或仅包含空白字符。</exception>
    /// <exception cref="InvalidOperationException">该名称已被其他提供程序类型注册。</exception>
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
    /// 将易失性内存日志存储注册为默认提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddVolatileJournalStorage(this ISiloBuilder builder)
        => builder.AddVolatileJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);

    /// <summary>
    /// 注册独立的命名易失性内存日志存储提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddVolatileJournalStorage(this ISiloBuilder builder, string name)
        => builder.AddVolatileJournalStorage(name, configureOptions: null);

    /// <summary>
    /// 注册具有可配置快照阈值的默认易失性日志提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">配置提供程序的快照阈值。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddVolatileJournalStorage(this ISiloBuilder builder, Action<VolatileJournalStorageOptions> configureOptions)
        => builder.AddVolatileJournalStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);

    /// <summary>
    /// 注册具有可配置快照阈值的命名易失性日志提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configureOptions">可选地配置提供程序的快照阈值。</param>
    /// <returns>Silo 构建器。</returns>
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
