using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.Configuration.Validators;
using Orleans.GrainDirectory;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Runtime.Configuration;
using Orleans.Runtime.Hosting;
using Orleans.Statistics;
using UnitTests.Grains;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// IMembershipTable 的空操作实现，用于测试 Silo 配置，
    /// 无需实际成员资格表基础设施。
    /// </summary>
    public class NoOpMembershipTable : IMembershipTable
    {
        [Obsolete("Use CleanupDefunctSiloEntriesAsync instead.")]
        public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) => CleanupDefunctSiloEntriesAsync(beforeDate, CancellationToken.None);

        public Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        [Obsolete("Use DeleteMembershipTableEntriesAsync instead.")]
        public Task DeleteMembershipTableEntries(string clusterId) => DeleteMembershipTableEntriesAsync(clusterId, CancellationToken.None);

        public Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        public Task InitializeMembershipTable(bool tryInitTableVersion) => InitializeMembershipTableAsync(tryInitTableVersion, CancellationToken.None);

        public Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        [Obsolete("Use InsertRowAsync instead.")]
        public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) => InsertRowAsync(entry, tableVersion, CancellationToken.None);

        public Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        [Obsolete("Use ReadAllAsync instead.")]
        public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

        public Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotImplementedException();
        }

        [Obsolete("Use ReadRowAsync instead.")]
        public Task<MembershipTableData> ReadRow(SiloAddress key) => ReadRowAsync(key, CancellationToken.None);

        public Task<MembershipTableData> ReadRowAsync(SiloAddress key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotImplementedException();
        }

        [Obsolete("Use UpdateIAmAliveAsync instead.")]
        public Task UpdateIAmAlive(MembershipEntry entry) => UpdateIAmAliveAsync(entry, CancellationToken.None);

        public Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        [Obsolete("Use UpdateRowAsync instead.")]
        public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion) => UpdateRowAsync(entry, etag, tableVersion, CancellationToken.None);

        public Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Orleans SiloBuilder 的测试，它负责配置和构建 Orleans Silo 实例。
    /// 这些测试验证配置校验、服务注册以及 Silo 组件的正确初始化，
    /// 无需完整的 Orleans 集群。Silo 是 Orleans 中托管 Grain 的主要单元。
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("Hosting")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class SiloBuilderTests
    {
        [Fact]
        public void ConfigureServicesRejectsNullBuilderBeforeCallingDelegate()
        {
            var invoked = false;

            var exception = Assert.Throws<ArgumentNullException>(
                () => ((ISiloBuilder)null!).ConfigureServices(_ => invoked = true));

            Assert.Equal("builder", exception.ParamName);
            Assert.False(invoked);
        }

        [Fact]
        public void ConfigureServicesAppliesRegistrationsToBuilderCollection()
        {
            var marker = new object();
            using var host = new HostBuilder().UseOrleans(builder =>
            {
                builder.UseLocalhostClustering();

                var result = builder.ConfigureServices(services => services.AddSingleton(marker));

                Assert.Same(builder, result);
            }).Build();

            Assert.Same(marker, host.Services.GetRequiredService<object>());
        }

        [Fact]
        public void ActivityPropagationRejectsNullSiloBuilder()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => CoreHostingExtensions.AddActivityPropagation(null!));

            Assert.Equal("builder", exception.ParamName);
        }

        [Fact]
        public void LegacyGrainDirectoryRejectsNullSiloBuilder()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => CoreHostingExtensions.UseLegacyGrainDirectory(null!));

            Assert.Equal("siloBuilder", exception.ParamName);
        }

        [Fact]
        public void DistributedGrainDirectoryRejectsNullSiloBuilder()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => CoreHostingExtensions.AddDistributedGrainDirectory(null!));

            Assert.Equal("siloBuilder", exception.ParamName);
        }

        [Fact]
        public void ActivityPropagationPreservesBuilderAndRegistersCallFilters()
        {
            using var host = new HostBuilder().UseOrleans(builder =>
            {
                builder.UseLocalhostClustering();

                Assert.Same(builder, builder.AddActivityPropagation());
            }).Build();

            Assert.Same(
                System.Diagnostics.DistributedContextPropagator.Current,
                host.Services.GetRequiredService<System.Diagnostics.DistributedContextPropagator>());
            Assert.Single(host.Services.GetServices<IIncomingGrainCallFilter>(),
                filter => filter is ActivityPropagationIncomingGrainCallFilter);
            Assert.Single(host.Services.GetServices<IOutgoingGrainCallFilter>(),
                filter => filter is ActivityPropagationOutgoingGrainCallFilter);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GrainDirectorySelectionPreservesBuilderAndDefaultRegistration(bool useLegacy)
        {
            using var host = new HostBuilder().UseOrleans(builder =>
            {
                builder.UseLocalhostClustering();

                var result = useLegacy ? builder.UseLegacyGrainDirectory() : builder.AddDistributedGrainDirectory();

                Assert.Same(builder, result);
                var registrations = builder.Services
                    .Where(descriptor => !descriptor.IsKeyedService)
                    .Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<NamedService<IGrainDirectory>>()
                    .Where(registration => registration.Name == GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY);
                if (useLegacy)
                {
                    Assert.Empty(registrations);
                }
                else
                {
                    Assert.Equal(typeof(Orleans.Runtime.GrainDirectory.DistributedGrainDirectory),
                        Assert.Single(registrations).ImplementationType);
                }
            }).Build();
        }

        /// <summary>
        /// 测试 Silo 生成器的基本配置，验证是否能够使用
        /// 本地主机集群配置及必需的配置选项成功构建 Silo。
        /// </summary>
        [Fact]
        public void SiloBuilderTest()
        {
            var host = new HostBuilder()
                .UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterOptions>(options => options.ClusterId = "someClusterId")
                        .Configure<EndpointOptions>(options => options.AdvertisedIPAddress = IPAddress.Loopback);
                })
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = true;
                    options.ValidateOnBuild = true;
                })
                .Build();

            var clusterClient = host.Services.GetRequiredService<IClusterClient>();
        }

        /// <summary>
        /// Grain 的 CollectionAgeLimit 必须大于 0 分钟。
        /// </summary>
        [Fact]
        public async Task SiloBuilder_GrainCollectionOptionsForZeroSecondsAgeLimitTest()
        {
            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .Configure<ClusterOptions>(options => { options.ClusterId = "GrainCollectionClusterId"; options.ServiceId = "GrainCollectionServiceId"; })
                        .Configure<EndpointOptions>(options => options.AdvertisedIPAddress = IPAddress.Loopback)
                        .ConfigureServices(services => services.AddSingleton<IMembershipTable, NoOpMembershipTable>())
                        .Configure<GrainCollectionOptions>(options => options
                                    .ClassSpecificCollectionAge
                                    .Add(typeof(CollectionSpecificAgeLimitForZeroSecondsActivationGcTestGrain).FullName!, TimeSpan.Zero));
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });
        }

        /// <summary>
        /// ClusterMembershipOptions.NumProbedSilos 必须大于 ClusterMembershipOptions.NumVotesForDeathDeclaration。
        /// </summary>
        [Fact]
        public async Task SiloBuilder_ClusterMembershipOptionsValidators()
        {
            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options => { options.NumVotesForDeathDeclaration = 10; options.NumProbedSilos = 1; });
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options => { options.NumVotesForDeathDeclaration = 0; });
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options => options.MaxDefunctSiloEntries = -1);
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options => options.DefunctSiloMinimumRetention = TimeSpan.FromTicks(-1));
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options => options.ProbeTimeout = TimeSpan.Zero);
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options => options.MinProbeTimeout = TimeSpan.FromSeconds(6));
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options =>
                        {
                            options.MinProbeTimeout = TimeSpan.FromSeconds(1);
                            options.MaxProbeTimeout = TimeSpan.FromSeconds(4);
                        });
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options => options.MaxProbeTimeout = TimeSpan.FromDays(50));
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options =>
                        {
                            options.ProbeTimeout = TimeSpan.FromDays(1);
                            options.MaxProbeTimeout = TimeSpan.FromDays(49);
                            options.NumMissedProbesLimit = int.MaxValue;
                        });
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });

            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterMembershipOptions>(options =>
                        {
                            options.ProbeTimeout = TimeSpan.FromTicks(4_611_686_018);
                            options.MaxProbeTimeout = options.ProbeTimeout;
                            options.NumMissedProbesLimit = 2_000_000_000;
                        });
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public void FailureDetectionTimeout_PreservesRepresentableTickBoundary()
        {
            var options = new ClusterMembershipOptions
            {
                MaxProbeTimeout = TimeSpan.FromTicks(long.MaxValue / int.MaxValue),
                NumMissedProbesLimit = int.MaxValue
            };

            Assert.Equal(TimeSpan.FromTicks(long.MaxValue - 1), options.GetFailureDetectionTimeout());
        }

        [Fact]
        public void FailureDetectionTimeout_ReportsOverflowAsConfigurationError()
        {
            var options = new ClusterMembershipOptions
            {
                MaxProbeTimeout = TimeSpan.FromTicks(long.MaxValue / int.MaxValue + 1),
                NumMissedProbesLimit = int.MaxValue
            };

            var exception = Assert.Throws<OrleansConfigurationException>(() => options.GetFailureDetectionTimeout());
            Assert.IsType<OverflowException>(exception.InnerException);
        }

        [Theory]
        [InlineData(0L, true)]
        [InlineData(-1L, true)]
        [InlineData(-TimeSpan.TicksPerMillisecond, true)]
        [InlineData(-TimeSpan.TicksPerMillisecond, false)]
        [InlineData(-TimeSpan.TicksPerMillisecond - 1, true)]
        [InlineData(long.MinValue, true)]
        public async Task SiloBuilder_ClusterMembershipOptionsRejectsInvalidTableRefreshTimeout(long ticks, bool useLivenessGossip)
        {
            using var host = new HostBuilder()
                .UseOrleans(siloBuilder => siloBuilder
                    .UseLocalhostClustering()
                    .Configure<ClusterMembershipOptions>(options =>
                    {
                        options.TableRefreshTimeout = TimeSpan.FromTicks(ticks);
                        options.UseLivenessGossip = useLivenessGossip;
                    }))
                .Build();

            var exception = await Assert.ThrowsAsync<OrleansConfigurationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains("ClusterMembershipOptions.TableRefreshTimeout", exception.Message);
            Assert.Contains("must be greater than 0.", exception.Message);
        }

        [Theory]
        [InlineData(1L)]
        [InlineData(TimeSpan.TicksPerMillisecond / 2)]
        [InlineData(60L * TimeSpan.TicksPerDay)]
        public void SiloBuilder_ClusterMembershipOptionsAcceptsValidTableRefreshTimeout(long ticks)
        {
            using var services = new ServiceCollection()
                .AddSingleton<IMembershipTable, NoOpMembershipTable>()
                .Configure<ClusterMembershipOptions>(options => options.TableRefreshTimeout = TimeSpan.FromTicks(ticks))
                .BuildServiceProvider();

            new SiloClusteringValidator(services).ValidateConfiguration();
        }

        /// <summary>
        /// 确保 LoadSheddingLimit 大于 100 时 <see cref="LoadSheddingValidator"/> 校验失败。
        /// </summary>
        [Fact]
        public async Task SiloBuilder_LoadSheddingValidatorAbove100ShouldFail()
        {
            await Assert.ThrowsAsync<OrleansConfigurationException>(async () =>
            {
                await new HostBuilder().UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .UseLocalhostClustering()
                        .Configure<ClusterOptions>(options => options.ClusterId = "someClusterId")
                        .Configure<EndpointOptions>(options => options.AdvertisedIPAddress = IPAddress.Loopback)
                        .ConfigureServices(services => services.AddSingleton<IMembershipTable, NoOpMembershipTable>())
                        .Configure<LoadSheddingOptions>(options =>
                        {
                            options.LoadSheddingEnabled = true;
                            options.CpuThreshold = 101;
                        });
                }).RunConsoleAsync(TestContext.Current.CancellationToken);
            });
        }

        /// <summary>
        /// 测试在未注册任何 Grain 类或接口时，Silo 无法启动。
        /// 这确保 Silo 在启动之前有实际工作可执行。
        /// </summary>
        [Fact]
        public async Task SiloBuilderThrowsDuringStartupIfNoGrainsAdded()
        {
            using var host = new HostBuilder()
                .UseOrleans((ctx, siloBuilder) =>
                {
                    // 仅添加一个包含生成的序列化器、但不包含 Grain 接口或 Grain 类的程序集
                    siloBuilder.UseLocalhostClustering()
                    .Configure<GrainTypeOptions>(options =>
                    {
                        options.Classes.Clear();
                        options.Interfaces.Clear();
                    });
                }).Build();

            await Assert.ThrowsAsync<OrleansConfigurationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// 测试尝试在同一宿主中同时配置客户端和 Silo 时是否引发异常。
        /// Orleans 要求 Silo 和客户端使用独立的宿主。
        /// </summary>
        [Fact]
        public void SiloBuilderThrowsDuringStartupIfClientBuildersAdded()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                _ = new HostBuilder()
                    .UseOrleansClient((ctx, clientBuilder) =>
                    {
                        clientBuilder.UseLocalhostClustering();
                    })
                    .UseOrleans((ctx, siloBuilder) =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    });
            });
        }

        /// <summary>
        /// 测试使用 Host.CreateApplicationBuilder API 尝试同时配置客户端和 Silo 时是否引发异常。
        /// 这验证相同的限制也适用于现代宿主 API。
        /// </summary>
        [Fact]
        public void SiloBuilderWithHotApplicationBuilderThrowsDuringStartupIfClientBuildersAdded()
        {
            Assert.Throws<OrleansConfigurationException>(() =>
            {
                _ = Host.CreateApplicationBuilder()
                    .UseOrleansClient(clientBuilder =>
                    {
                        clientBuilder.UseLocalhostClustering();
                    })
                    .UseOrleans(siloBuilder =>
                    {
                        siloBuilder.UseLocalhostClustering();
                    });
            });
        }

        private class MyService
        {
            public int Id { get; set; }
        }
    }
}
