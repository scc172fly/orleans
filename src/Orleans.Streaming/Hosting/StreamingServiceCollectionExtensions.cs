using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration.Internal;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Runtime.Providers;
using Orleans.Serialization;
using Orleans.Streaming.JsonConverters;
using Orleans.Streams;
using Orleans.Streams.Core;
using Orleans.Streams.Filtering;

namespace Orleans.Hosting
{
    /// <summary>
    /// silos上的配置流的扩展方法。
    /// </summary>
    public static class StreamingServiceCollectionExtensions
    {
        /// <summary>
        /// 添加此流的支援 silo。
        /// </summary>
        /// <param name="services">服务。</param>
        public static void AddSiloStreaming(this IServiceCollection services)
        {
            if (services.Any(service => service.ServiceType.Equals(typeof(SiloStreamProviderRuntime))))
            {
                return;
            }

            services.TryAddSingleton<StreamInstruments>();
            services.AddSingleton<PubSubGrainStateStorageFactory>();
            services.AddSingleton<SiloStreamProviderRuntime>();
            services.AddFromExisting<IStreamProviderRuntime, SiloStreamProviderRuntime>();
            services.AddSingleton<ImplicitStreamSubscriberTable>();
            services.AddSingleton<IConfigureGrainContext, StreamConsumerGrainContextAction>();
            services.AddSingleton<IStreamNamespacePredicateProvider, DefaultStreamNamespacePredicateProvider>();
            services.AddSingleton<IStreamNamespacePredicateProvider, ConstructorStreamNamespacePredicateProvider>();
            services.AddKeyedSingleton<IStreamIdMapper, DefaultStreamIdMapper>(DefaultStreamIdMapper.Name);
            services.AddKeyedTransient<IGrainExtension>(typeof(IStreamConsumerExtension), (sp, _) =>
            {
                var runtime = sp.GetRequiredService<IStreamProviderRuntime>();
                var grainContext = sp.GetRequiredService<IGrainContextAccessor>().GrainContext;
                return new StreamConsumerExtension(runtime, grainContext);
            });
            services.AddSingleton<IStreamSubscriptionManagerAdmin>(sp =>
                new StreamSubscriptionManagerAdmin(sp.GetRequiredService<IStreamProviderRuntime>()));
            services.AddTransient<IStreamQueueBalancer, ConsistentRingQueueBalancer>();
            services.AddSingleton<IPostConfigureOptions<OrleansJsonSerializerOptions>, StreamingConverterConfigurator>();
            services.AddSingleton<IPostConfigureOptions<SystemTextJsonGrainStorageSerializerOptions>, SystemTextJsonStreamConverterConfigurator>();

            // 每次激活一个流目录
            services.AddScoped<StreamDirectory>();
        }

        /// <summary>
        /// 添加对此客户端流的支持 。
        /// </summary>
        /// <param name="services">服务。</param>
        public static void AddClientStreaming(this IServiceCollection services)
        {
            if (services.Any(service => service.ServiceType.Equals(typeof(ClientStreamingProviderRuntime))))
            {
                return;
            }

            services.AddSingleton<ClientStreamingProviderRuntime>();
            services.AddFromExisting<IStreamProviderRuntime, ClientStreamingProviderRuntime>();
            services.AddSingleton<IStreamSubscriptionManagerAdmin, StreamSubscriptionManagerAdmin>();
            services.AddSingleton<ImplicitStreamSubscriberTable>();
            services.AddSingleton<IStreamNamespacePredicateProvider, DefaultStreamNamespacePredicateProvider>();
            services.AddSingleton<IStreamNamespacePredicateProvider, ConstructorStreamNamespacePredicateProvider>();
            services.AddKeyedSingleton<IStreamIdMapper, DefaultStreamIdMapper>(DefaultStreamIdMapper.Name);
            services.AddFromExisting<ILifecycleParticipant<IClusterClientLifecycle>, ClientStreamingProviderRuntime>();
            services.AddSingleton<IPostConfigureOptions<OrleansJsonSerializerOptions>, StreamingConverterConfigurator>();
        }

        /// <summary>
        /// 添加流过滤器。 
        /// </summary>
        /// <typeparam name="T">流筛选类型。</typeparam>
        /// <param name="services">服务收藏。</param>
        /// <param name="name">流体过滤器名称。</param>
        /// <returns>服务收藏。</returns>
        public static IServiceCollection AddStreamFilter<T>(this IServiceCollection services, string name) where T : class, IStreamFilter
        {
            return services.AddKeyedSingleton<IStreamFilter, T>(name);
        }
    }
}
