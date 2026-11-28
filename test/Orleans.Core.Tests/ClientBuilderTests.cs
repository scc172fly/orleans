using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Messaging;
using Orleans.Runtime;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// 用于测试客户端配置的 IGatewayListProvider 空操作实现，
    /// 无需实际连接到网关。
    /// </summary>
    public class NoOpGatewaylistProvider : IGatewayListProvider
    {
        public TimeSpan MaxStaleness => throw new NotImplementedException();

        public bool IsUpdatable => throw new NotImplementedException();

        public Task<IList<Uri>> GetGateways()
        {
            throw new NotImplementedException();
        }

        public Task InitializeGatewayListProvider()
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 测试 Orleans ClientBuilder，它负责配置并构建 Orleans 客户端实例。
    /// 这些测试验证客户端配置校验、服务注册及客户端组件正确初始化，
    /// 无需完整的 Orleans 集群。
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("ClientBuilder")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ClientBuilderTests
    {
        /// <summary>
        /// 测试未指定 ClusterId 和 ServiceId 时无法创建客户端。
        /// </summary>
        [Fact]
        public void ClientBuilder_ClusterOptionsTest()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                var host = new HostBuilder()
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.Configure<ClusterOptions>(options =>
                        {
                            options.ClusterId = null!;
                            options.ServiceId = null!;
                        });

                        clientBuilder.ConfigureServices(services =>
                            services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                    })
                    .Build();

                _ = host.Services.GetRequiredService<IClusterClient>();
            });

            Assert.Throws<OrleansConfigurationException>(() =>
            {
                var host = new HostBuilder()
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.Configure<ClusterOptions>(options =>
                        {
                            options.ClusterId = "someClusterId";
                            options.ServiceId = null!;
                        });

                        clientBuilder.ConfigureServices(services =>
                            services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                    })
                    .Build();

                _ = host.Services.GetRequiredService<IClusterClient>();
            });

            Assert.Throws<OrleansConfigurationException>(() =>
            {
                var host = new HostBuilder()
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.Configure<ClusterOptions>(options =>
                        {
                            options.ClusterId = null!;
                            options.ServiceId = "someServiceId";
                        });

                        clientBuilder.ConfigureServices(services =>
                            services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                    })
                    .Build();

                _ = host.Services.GetRequiredService<IClusterClient>();
            });

            var host = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder.Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = "someClusterId";
                        options.ServiceId = "someServiceId";
                    });

                    clientBuilder.ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                })
                .Build();

            var client = host.Services.GetRequiredService<IClusterClient>();
            Assert.NotNull(client);
        }

        /// <summary>
        /// 测试不指定配置也能创建客户端。
        /// </summary>
        [Fact]
        public void ClientBuilder_NoSpecifiedConfigurationTest()
        {
            var hostBuilder = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder.ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                })
                .ConfigureServices(RemoveConfigValidators);

            var host = hostBuilder.Build();

            var client = host.Services.GetRequiredService<IClusterClient>();
            Assert.NotNull(client);
        }

        /// <summary>
        /// 验证未注册 Grain 接口时，客户端在启动期间抛出异常。
        /// 这确保客户端至少具有一个可用于通信的 Grain 接口。
        /// </summary>
        [Fact]
        public void ClientBuilder_ThrowsDuringStartupIfNoGrainInterfacesAdded()
        {
            // 仅添加包含生成序列化器但不含 Grain 接口的程序集。
            var hostBuilder = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder
                        .UseLocalhostClustering()
                        .Configure<GrainTypeOptions>(options =>
                        {
                            options.Interfaces.Clear();
                        });
                })
                .ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());

            var host = hostBuilder.Build();

            Assert.Throws<OrleansConfigurationException>(() => _ = host.Services.GetRequiredService<IClusterClient>());
        }

        /// <summary>
        /// 测试 <see cref="IClientBuilder.ConfigureServices"/> 委托能否按预期工作。
        /// </summary>
        [Fact]
        public void ClientBuilder_ServiceProviderTest()
        {
            var hostBuilder = new HostBuilder()
                .UseOrleansClient((ctx, clientBuilder) =>
                {
                    clientBuilder.ConfigureServices(services => services.AddSingleton<IGatewayListProvider, NoOpGatewaylistProvider>());
                })
                .ConfigureServices(RemoveConfigValidators);

            Assert.Throws<ArgumentNullException>(() => hostBuilder.ConfigureServices(null!));

            var registeredFirst = new int[1];

            var one = new MyService { Id = 1 };
            hostBuilder.ConfigureServices(
                services =>
                {
                    Interlocked.CompareExchange(ref registeredFirst[0], 1, 0);
                    services.AddSingleton(one);
                });

            var two = new MyService { Id = 2 };
            hostBuilder.ConfigureServices(
                services =>
                {
                    Interlocked.CompareExchange(ref registeredFirst[0], 2, 0);
                    services.AddSingleton(two);
                });

            var host = hostBuilder.Build();

            var client = host.Services.GetRequiredService<IClusterClient>();
            var services = client.ServiceProvider.GetServices<MyService>()?.ToList();
            Assert.NotNull(services);

            // 两个服务都应已注册。
            Assert.Equal(2, services.Count);
            Assert.NotNull(services.Find(svc => svc.Id == 1));
            Assert.NotNull(services.Find(svc => svc.Id == 2));

            // 服务 1 应先注册，必须保留管线顺序。
            Assert.Equal(1, registeredFirst[0]);

            // 默认应提供最后注册的服务。
            Assert.Equal(2, client.ServiceProvider.GetRequiredService<MyService>().Id);
        }

        /// <summary>
        /// 测试尝试在同一主机中同时配置 Silo 和客户端时抛出异常。
        /// Orleans 要求 Silo 和客户端使用独立的主机。
        /// </summary>
        [Fact]
        public void ClientBuilderThrowsDuringStartupIfSiloBuildersAdded()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                _ = new HostBuilder()
                    .UseOrleans((ctx, siloBuilder) =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    })
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.UseLocalhostClustering();
                    });
            });
        }

        /// <summary>
        /// 测试使用 Host.CreateApplicationBuilder API 同时配置 Silo 和客户端时抛出异常。
        /// 这验证相同的限制也适用于现代托管 API。
        /// </summary>
        [Fact]
        public void ClientBuilderWithHotApplicationBuilderThrowsDuringStartupIfSiloBuildersAdded()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                _ = Host.CreateApplicationBuilder()
                    .UseOrleans(siloBuilder =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    })
                    .UseOrleansClient(clientBuilder =>
                    {
                        clientBuilder.UseLocalhostClustering();
                    });
            });
        }

        private static void RemoveConfigValidators(IServiceCollection services)
        {
            var validators = services.Where(descriptor => descriptor.ServiceType == typeof(IConfigurationValidator)).ToList();
            foreach (var validator in validators) services.Remove(validator);
        }

        private class MyService
        {
            public int Id { get; set; }
        }
    }
}
