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
    /// 用于测试客户配置的 IGatewayListProvider 不执行操作
    /// 不需要实际网关连接。
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
    /// 测试 Orleans ClientBuilder负责配置和建设 Orleans 客户端实例。
    /// 这些测试核查客户配置验证、服务登记和客户构成部分的适当启动
    /// 不需要完整的 Orleans 组。
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("ClientBuilder")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ClientBuilderTests
    {
        /// <summary>
        /// 测试客户端在未指定 ClusterId 和 ServiceId的情况下无法创建 。
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
        /// 测试客户端是否无需指定配置即可创建。
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
        /// 证明客户在启动时没有注册 grain 接口,会丢弃例外。
        /// 这确保客户至少有一个 grain 接口可以与用户沟通。
        /// </summary>
        [Fact]
        public void ClientBuilder_ThrowsDuringStartupIfNoGrainInterfacesAdded()
        {
            // 只添加带有生成序列器的组件, 但没有 grain 接口
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
        /// 测试 <see cref="IClientBuilder.ConfigureServices"/> 代表的工作是否如预期的那样有效。
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

            // 两种服务都应登记。
            Assert.Equal(2, services.Count);
            Assert.NotNull(services.Find(svc => svc.Id == 1));
            Assert.NotNull(services.Find(svc => svc.Id == 2));

            // 第1服务处应首先登记,管道订单应予保留。
            Assert.Equal(1, registeredFirst[0]);

            // 最后登记的服务应默认提供。
            Assert.Equal(2, client.ServiceProvider.GetRequiredService<MyService>().Id);
        }

        /// <summary>
        /// 试图在同一主机中配置 silo 和客户端的测试会排除一个例外。
        /// Orleans 要求 silos 和客户分别提供主机。
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
        /// 试图将 silo 客户使用 Host.CreateApplicationBuilder API 抛出一个例外。
        /// 由此可以核实同一限制是否适用于现代主机 API。
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
