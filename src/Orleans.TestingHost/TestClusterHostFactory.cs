using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.Hosting;
using Orleans.Messaging;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;
using Orleans.Runtime.TestHooks;
using Orleans.Statistics;
using Orleans.TestingHost.Logging;
using Orleans.TestingHost.Utils;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 给定配置源的名称和收藏, 用于创建 silos 的工具 。
    /// </summary>
    public class TestClusterHostFactory
    {
        /// <summary>
        /// 创建返回新 silo。
        /// </summary>
        /// <param name="hostName">如果配置中没有指定, 则名称为 silo 。</param>
        /// <param name="configuration">配置。</param>
        /// <param name="postConfigureHostBuilder">一个可选的代表, 可用于在主机正在构建之前配置主机构建器。</param>
        /// <returns>一个新的 silo。</returns>
        public static IHost CreateSiloHost(string hostName, IConfiguration configuration, Action<IHostBuilder>? postConfigureHostBuilder = null)
        {
            string siloName = configuration["Name"] ?? hostName;

            var hostBuilder = new HostBuilder();
            hostBuilder.UseEnvironment(Environments.Development);
            hostBuilder.Properties["Configuration"] = configuration;
            hostBuilder.ConfigureHostConfiguration(cb => cb.AddConfiguration(configuration));

            hostBuilder.UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder.Services
                    .Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
                TestClusterFatalErrorHandler.Configure(siloBuilder.Services);
            });

            ConfigureAppServices(configuration, hostBuilder);

            hostBuilder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<TestHooksEnvironmentStatisticsProvider>();
                services.AddFromExisting<IEnvironmentStatisticsProvider, TestHooksEnvironmentStatisticsProvider>();
                services.AddSingleton<TestHooksSystemTarget>();

                TryConfigureFileLogging(configuration, services, siloName);

                if (Debugger.IsAttached)
                {
                    // 测试正在内部调试器内运行 - 让超时 *\\ 无限
                    services.Configure<SiloMessagingOptions>(op => op.ResponseTimeout = TimeSpan.FromMilliseconds(1000000));
                }
            });

            postConfigureHostBuilder?.Invoke(hostBuilder);
            var host = hostBuilder.Build();
            TestClusterFatalErrorHandler.Attach(host);
            InitializeTestHooksSystemTarget(host);
            return host;
        }

        /// <summary>
        /// 创建分组客户端。
        /// </summary>
        /// <param name="hostName">主机的名称。</param>
        /// <param name="configuration">配置。</param>
        /// <param name="postConfigureHostBuilder">一个可选的代表, 可用于在主机正在构建之前配置主机构建器。</param>
        /// <returns>群集客户端主机。</returns>
        public static IHost CreateClusterClient(string hostName, IConfiguration configuration, Action<IHostBuilder>? postConfigureHostBuilder = null)
        {
            var hostBuilder = new HostBuilder();
            hostBuilder.UseEnvironment(Environments.Development);
            hostBuilder.Properties["Configuration"] = configuration;
            hostBuilder.ConfigureHostConfiguration(cb => cb.AddConfiguration(configuration));

            hostBuilder.UseOrleansClient();

            ConfigureClientAppServices(configuration, hostBuilder);

            hostBuilder
                .ConfigureServices(services =>
                {
                    //TryConfigureClientMembership(configuration, services);
                    TryConfigureFileLogging(configuration, services, hostName);
                });

            postConfigureHostBuilder?.Invoke(hostBuilder);
            var host = hostBuilder.Build();

            return host;
        }

        /// <summary>
        /// 将配置分级为字符串。
        /// </summary>
        /// <param name="configuration">配置。</param>
        /// <returns>序列配置。</returns>
        public static string SerializeConfiguration(IConfiguration configuration)
        {
            var settings = new JsonSerializerSettings();

            KeyValuePair<string, string?>[] enumerated = configuration.AsEnumerable().ToArray();
            return JsonConvert.SerializeObject(enumerated, settings);
        }

        /// <summary>
        /// 使配置字符串降序。
        /// </summary>
        /// <param name="serializedSources">序列资料</param>
        /// <returns>解密配置。</returns>
        public static IConfiguration DeserializeConfiguration(string serializedSources)
        {
            var settings = new JsonSerializerSettings();

            var builder = new ConfigurationBuilder();
            var enumerated = JsonConvert.DeserializeObject<KeyValuePair<string, string?>[]>(serializedSources, settings);
            builder.AddInMemoryCollection(enumerated);
            return builder.Build();
        }

        private static void ConfigureAppServices(IConfiguration configuration, IHostBuilder hostBuilder)
        {
            var builderConfiguratorTypes = configuration.GetSection(nameof(TestClusterOptions.SiloBuilderConfiguratorTypes))?.Get<string[]>();
            if (builderConfiguratorTypes == null) return;

            foreach (var builderConfiguratorType in builderConfiguratorTypes)
            {
                if (!string.IsNullOrWhiteSpace(builderConfiguratorType))
                {
                    var configurator = Activator.CreateInstance(Type.GetType(builderConfiguratorType, true)!);

                    (configurator as IHostConfigurator)?.Configure(hostBuilder);
                    hostBuilder.UseOrleans((ctx, siloBuilder) => (configurator as ISiloConfigurator)?.Configure(siloBuilder));
                }
            }
        }

        private static void ConfigureClientAppServices(IConfiguration configuration, IHostBuilder hostBuilder)
        {
            var builderConfiguratorTypes = configuration.GetSection(nameof(TestClusterOptions.ClientBuilderConfiguratorTypes))?.Get<string[]>();
            if (builderConfiguratorTypes == null) return;

            foreach (var builderConfiguratorType in builderConfiguratorTypes)
            {
                if (!string.IsNullOrWhiteSpace(builderConfiguratorType))
                {
                    var builderConfigurator = Activator.CreateInstance(Type.GetType(builderConfiguratorType, true)!);

                    (builderConfigurator as IHostConfigurator)?.Configure(hostBuilder);

                    if (builderConfigurator is IClientBuilderConfigurator clientBuilderConfigurator)
                    {
                        hostBuilder.UseOrleansClient(clientBuilder => clientBuilderConfigurator.Configure(configuration, clientBuilder));
                    }
                }
            }
        }

        private static void TryConfigureFileLogging(IConfiguration configuration, IServiceCollection services, string name)
        {
            bool.TryParse(configuration[nameof(TestClusterOptions.ConfigureFileLogging)], out bool configureFileLogging);
            if (configureFileLogging)
            {
                var fileName = TestingUtils.CreateTraceFileName(name, configuration["Orleans:ClusterId"]!);
                services.AddLogging(loggingBuilder => loggingBuilder.AddFile(fileName));
            }
        }

        private static void InitializeTestHooksSystemTarget(IHost host)
        {
            _ = host.Services.GetRequiredService<TestHooksSystemTarget>();
        }
    }
}
