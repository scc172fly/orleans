using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.Configuration.Validators;
using Orleans.Connections;
using Orleans.Connections.Transport;
using Orleans.Connections.Transport.Sockets;
using Orleans.GrainReferences;
using Orleans.Messaging;
using Orleans.Metadata;
using Orleans.Placement.Repartitioning;
using Orleans.Providers;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.Versions;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using Orleans.Statistics;

namespace Orleans
{
    /// <summary>
    /// 配置客户端的默认服务。
    /// </summary>
    internal static class DefaultClientServices
    {
        private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(ServicesAdded), new ServicesAdded());

        /// <summary>
        /// 配置客户端的默认服务。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        public static void AddDefaultServices(IClientBuilder builder)
        {
            var services = builder.Services;
            if (services.Contains(ServiceDescriptor))
            {
                return;
            }

            services.Add(ServiceDescriptor);

            // 共同事务 共同事务
            services.AddLogging();
            services.AddOptions();
            services.AddMetrics();
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);
            services.TryAddKeyedSingleton<TimeProvider>(KeyedService.AnyKey, static (sp, _) => sp.GetRequiredService<TimeProvider>());
            services.TryAddSingleton<OrleansInstruments>();
            services.TryAddSingleton<ClientInstruments>();
            services.TryAddSingleton<MessagingInstruments>();
            services.TryAddSingleton<MessagingProcessingInstruments>();

            // 选项的伐木
            services.TryAddSingleton(typeof(IOptionFormatter<>), typeof(DefaultOptionsFormatter<>));
            services.TryAddSingleton(typeof(IOptionFormatterResolver<>), typeof(DefaultOptionsFormatterResolver<>));

            services.AddSingleton<ClientOptionsLogger>();
            services.AddFromExisting<ILifecycleParticipant<IClusterClientLifecycle>, ClientOptionsLogger>();

            // 生命周期
            services.AddSingleton<ServiceLifecycle<IClusterClientLifecycle>>();
            services.TryAddFromExisting<IServiceLifecycle, ServiceLifecycle<IClusterClientLifecycle>>();
            services.AddFromExisting<ILifecycleParticipant<IClusterClientLifecycle>, ServiceLifecycle<IClusterClientLifecycle>>();

            // 统计统计统计
            services.AddSingleton<IEnvironmentStatisticsProvider, EnvironmentStatisticsProvider>();
#pragma warning disable 618
            services.AddSingleton<OldEnvironmentStatistics>();
            services.AddFromExisting<IAppEnvironmentStatistics, OldEnvironmentStatistics>();
            services.AddFromExisting<IHostEnvironmentStatistics, OldEnvironmentStatistics>();
#pragma warning restore 618

            services.TryAddSingleton<GrainBindingsResolver>();
            services.TryAddSingleton<LocalClientDetails>();
            services.TryAddSingleton<OutsideRuntimeClient>();
            services.TryAddSingleton<InterfaceToImplementationMappingCache>();
            services.TryAddSingleton<ClientGrainContext>();
            services.AddSingleton<IClusterConnectionStatusObserver, ClusterConnectionStatusObserverAdaptor>();
            services.AddFromExisting<IGrainContextAccessor, ClientGrainContext>();
            services.TryAddFromExisting<IRuntimeClient, OutsideRuntimeClient>();
            services.TryAddFromExisting<IClusterConnectionStatusListener, OutsideRuntimeClient>();
            services.TryAddSingleton<GrainFactory>();
            services.TryAddSingleton<GrainInterfaceTypeToGrainTypeResolver>();
            services.TryAddSingleton<GrainReferenceActivator>();
            services.AddSingleton<IGrainReferenceActivatorProvider, GrainReferenceActivatorProvider>();
            services.AddSingleton<IGrainReferenceActivatorProvider, UntypedGrainReferenceActivatorProvider>();
            services.TryAddSingleton<RpcProvider>();
            services.TryAddSingleton<IGrainReferenceRuntime, GrainReferenceRuntime>();
            services.TryAddSingleton<GrainPropertiesResolver>();
            services.TryAddSingleton<IGrainCancellationTokenRuntime, GrainCancellationTokenRuntime>();
            services.TryAddFromExisting<IGrainFactory, GrainFactory>();
            services.TryAddFromExisting<IInternalGrainFactory, GrainFactory>();
            services.TryAddSingleton<ClientProviderRuntime>();
            services.TryAddSingleton<MessageFactory>();
            services.TryAddFromExisting<IProviderRuntime, ClientProviderRuntime>();
            services.TryAddSingleton<ClusterClient>();
            services.TryAddFromExisting<IClusterClient, ClusterClient>();
            services.TryAddFromExisting<IInternalClusterClient, ClusterClient>();
            services.AddFromExisting<IHostedService, ClusterClient>();
            services.AddTransient<IOptions<MessagingOptions>>(static sp => sp.GetRequiredService<IOptions<ClientMessagingOptions>>());
            services.TryAddSingleton<IClientConnectionRetryFilter, LinearBackoffClientConnectionRetryFilter>();

            services.AddSingleton<IConfigureOptions<GrainTypeOptions>, DefaultGrainTypeOptionsProvider>();

            // 添加默认选项, 用于未配置的选项, 以配置需要配置的选项
            services.ConfigureFormatter<ClusterOptions>();
            services.ConfigureFormatter<ClientMessagingOptions>();
            services.ConfigureFormatter<ConnectionOptions>();

            services.AddTransient<IConfigurationValidator, GrainTypeOptionsValidator>();
            services.AddTransient<IConfigurationValidator, ClusterOptionsValidator>();
            services.AddTransient<IConfigurationValidator, ClientClusteringValidator>();
            services.AddTransient<IConfigurationValidator, SerializerConfigurationValidator>();

            // 建立网络
            services.AddSingleton<MessageHandlerShared>();
            services.TryAddSingleton<IMessageStatisticsSink, NoOpMessageStatisticsSink>();
            services.TryAddSingleton<NetworkingInstruments>();
            services.TryAddSingleton<ConnectionCommon>();
            services.TryAddSingleton<ConnectionManager>();
            services.TryAddSingleton<ConnectionPreambleHelper>();
            services.AddSingleton<ILifecycleParticipant<IClusterClientLifecycle>>(sp =>
                new ConnectionManagerLifecycleAdapter<IClusterClientLifecycle>(
                    sp.GetRequiredService<ConnectionManager>(),
                    ct => sp.GetRequiredService<OutsideRuntimeClient>().StopObserverInvocationsAsync().WaitAsync(ct)));

            services.AddSerializer();
            services.AddSingleton<ITypeNameFilter, AllowOrleansTypes>();
            services.AddSingleton<ISpecializableCodec, GrainReferenceCodecProvider>();
            services.AddSingleton<ISpecializableCopier, GrainReferenceCopierProvider>();
            services.AddSingleton<OnDeserializedCallbacks>();
            services.AddSingleton<IPostConfigureOptions<OrleansJsonSerializerOptions>, ConfigureOrleansJsonSerializerOptions>();
            services.AddSingleton<OrleansJsonSerializer>();

            services.TryAddSingleton<MessageSerializerFactory>(sp =>
            {
                var sessionPool = sp.GetRequiredService<SerializerSessionPool>();
                var options = sp.GetRequiredService<IOptions<ClientMessagingOptions>>();
                return () => new MessageSerializer(sessionPool, options.Value);
            });
            services.TryAddSingleton<ConnectionFactory, ClientOutboundConnectionFactory>();
            services.AddSingleton<ClientMessageCenter>(sp => sp.GetRequiredService<OutsideRuntimeClient>().MessageCenter!);
            services.TryAddFromExisting<IMessageCenter, ClientMessageCenter>();
            services.AddSingleton<GatewayManager>();
            services.AddSingleton<ConnectionTrace>();
            services.AddSingleton<MessagingTrace>();

            // 元元数据类型
            services.AddSingleton<ClientClusterManifestProvider>();
            services.AddFromExisting<IClusterManifestProvider, ClientClusterManifestProvider>();
            services.AddSingleton<ClientManifestProvider>();
            services.AddSingleton<IGrainInterfaceTypeProvider, AttributeGrainInterfaceTypeProvider>();
            services.AddSingleton<GrainTypeResolver>();
            services.AddSingleton<IGrainTypeProvider, AttributeGrainTypeProvider>();
            services.AddSingleton<GrainPropertiesResolver>();
            services.AddSingleton<GrainVersionManifest>();
            services.AddSingleton<GrainInterfaceTypeResolver>();
            services.AddSingleton<IGrainInterfacePropertiesProvider, AttributeGrainInterfacePropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, AttributeGrainPropertiesProvider>();
            services.AddSingleton<IGrainInterfacePropertiesProvider, TypeNameGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, TypeNameGrainPropertiesProvider>();
            services.AddSingleton<IGrainPropertiesProvider, ImplementedInterfaceProvider>();

            services.AddSingleton<IGrainCallCancellationManager, ExternalClientGrainCallCancellationManager>();
            services.AddSingleton<ILocalActivationStatusChecker, ClientLocalActivationStatusChecker>();

            services.AddSingleton<MessageTransportConnector, TcpMessageTransportConnector>();
            ApplyConfiguration(builder);
        }

        private static void ApplyConfiguration(IClientBuilder builder)
        {
            var services = builder.Services;
            var cfg = builder.Configuration.GetSection("Orleans");
            var knownProviderTypes = GetRegisteredProviders();

            services.Configure<ClusterOptions>(cfg);
            services.Configure<ClientMessagingOptions>(cfg.GetSection("Messaging"));
            services.Configure<GatewayOptions>(cfg.GetSection("Gateway"));

            if (ConfigurationValueParser.TryParseValue<bool>(cfg, "EnableDistributedTracing", out var enableDistributedTracing) && enableDistributedTracing)
            {
                builder.AddActivityPropagation();
            }

            ApplySubsection(builder, cfg, knownProviderTypes, "Clustering");
            ApplySubsection(builder, cfg, knownProviderTypes, "Reminders");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "BroadcastChannel");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "Streaming");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "GrainStorage");
            ApplyNamedSubsections(builder, cfg, knownProviderTypes, "GrainDirectory");

            static void ConfigureProvider(
                IClientBuilder builder,
                Dictionary<(string Kind, string Name), Type> knownProviderTypes,
                string kind,
                string? name,
                IConfigurationSection configurationSection)
            {
                var providerType = configurationSection["ProviderType"] ?? "Default";
                var provider = GetRequiredProvider(knownProviderTypes, kind, providerType);
                provider.Configure(builder, name, configurationSection);
            }

            static IProviderBuilder<IClientBuilder> GetRequiredProvider(Dictionary<(string Kind, string Name), Type> knownProviderTypes, string kind, string name)
            {
                if (ProviderRegistrationResolver.Default.TryGetRegisteredProvider(knownProviderTypes, "Client", kind, name, out var type))
                {
                    var instance = Activator.CreateInstance(type);
                    return instance as IProviderBuilder<IClientBuilder>
                        ?? throw new InvalidOperationException($"{kind} provider, '{name}', of type {type}, does not implement {typeof(IProviderBuilder<IClientBuilder>)}.");
                }

                var knownProvidersOfKind = knownProviderTypes
                    .Where(kvp => string.Equals(kvp.Key.Kind, kind, StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key.Name)
                    .OrderBy(n => n)
                    .ToList();

                var knownProvidersMessage = knownProvidersOfKind.Count > 0
                    ? $" Known {kind} providers: {string.Join(", ", knownProvidersOfKind)}."
                    : string.Empty;

                throw new InvalidOperationException($"Could not find {kind} provider named '{name}'. This can indicate that either the 'Microsoft.Orleans.Sdk' or the provider's package are not referenced by your application.{knownProvidersMessage}");
            }

            static Dictionary<(string Kind, string Name), Type> GetRegisteredProviders()
                => ProviderRegistrationResolver.Default.GetRegisteredProviders("Client");

            static void ApplySubsection(IClientBuilder builder, IConfigurationSection cfg, Dictionary<(string Kind, string Name), Type> knownProviderTypes, string sectionName)
            {
                if (cfg.GetSection(sectionName) is { } section && section.Exists())
                {
                    ConfigureProvider(builder, knownProviderTypes, sectionName, name: null, section);
                }
            }

            static void ApplyNamedSubsections(IClientBuilder builder, IConfigurationSection cfg, Dictionary<(string Kind, string Name), Type> knownProviderTypes, string sectionName)
            {
                if (cfg.GetSection(sectionName) is { } section && section.Exists())
                {
                    foreach (var child in section.GetChildren())
                    {
                        ConfigureProvider(builder, knownProviderTypes, sectionName, name: child.Key, child);
                    }
                }
            }
        }

        internal partial class RootConfiguration
        {
            public IConfigurationSection? Clustering { get; set; }
        }

        /// <summary>
        /// A <see cref="ITypeNameFilter"/> 允许从装配中含有任何类型的“Orleans" 允许为序列化和去转性目的使用其名称。
        /// </summary>
        private class AllowOrleansTypes : ITypeNameFilter
        {
            /// <inheritdoc />
            public bool? IsTypeNameAllowed(string typeName, string assemblyName)
            {
                if (assemblyName is { Length: > 0 } && assemblyName.Contains("Orleans", StringComparison.Ordinal))
                {
                    return true;
                }

                return null;
            }
        }

        /// <summary>
        /// 用于确定是否添加默认服务的标记类型。
        /// </summary>
        private class ServicesAdded { }
    }
}
