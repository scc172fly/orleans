using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Core.Internal;
using Orleans.Diagnostics;
using Orleans.Placement;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 演示缺少激活跟踪跨度的失败测试。
    /// 预期 Grain 首次激活时创建激活 Activity。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [Collection("ActivationTracing")]
    public class ActivationTracingTests : OrleansTestingBase, IClassFixture<ActivationTracingTests.Fixture>
    {
        private static readonly ConcurrentBag<Activity> Started = new();
        private static readonly ActivityListener Listener;

        static ActivationTracingTests()
        {
            Listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        || src.Name == ActivitySources.LifecycleActivitySourceName
                                        || src.Name == ActivitySources.StorageActivitySourceName,
                Sample = (ref _) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity => Started.Add(activity),
            };
            ActivitySource.AddActivityListener(Listener);
        }

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 2; // 迁移测试需要两个 Silo
                builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
                builder.AddSiloBuilderConfigurator<SiloCfg>();
                builder.AddClientBuilderConfigurator<ClientCfg>();
            }

            private class SiloCfg : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder
                        .AddActivityPropagation()
                        .AddDistributedGrainDirectory()
                        .AddMemoryGrainStorageAsDefault()
                        .AddMemoryGrainStorage("PubSubStore")
                        .AddIncomingGrainCallFilter<DeactivateAfterDisposeAsyncFilter>();
                    hostBuilder.Services.AddPlacementFilter<TracingTestPlacementFilterStrategy, TracingTestPlacementFilterDirector>(ServiceLifetime.Singleton);
                    hostBuilder.Services.AddPlacementFilter<SecondTracingTestPlacementFilterStrategy, SecondTracingTestPlacementFilterDirector>(ServiceLifetime.Singleton);
                }
            }

            private class ClientCfg : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
                {
                    clientBuilder.AddActivityPropagation();
                }
            }
        }

        private readonly Fixture _fixture;
        private readonly ITestOutputHelper _output;

        public ActivationTracingTests(Fixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationSpanIsCreatedOnFirstCall()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IActivityGrain>(Random.Shared.Next());
                // 首次调用应强制触发激活
                _ = await grain.GetActivityId();

                // 预期至少有一个与激活相关的 Activity
                var activationActivities = Started.Where(a => a.Source.Name == ActivitySources.LifecycleActivitySourceName).ToList();
                Assert.True(activationActivities.Count > 0, "Expected activation tracing activity to be created, but none were observed.");

                // 验证所有预期 Span 均存在，且正确地以 test-parent 为父级。
                var testParentTraceId = parent!.TraceId.ToString();

                // 查找放置 跟踪跨度：其父级应为 Grain 调用，而该调用的父级为 test-parent
                var placementSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.PlaceGrain);
                Assert.NotNull(placementSpan);
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找放置筛选器 跟踪跨度：其跟踪 ID 应与 test-parent 相同
                var placementFilterSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.FilterPlacementCandidates);
                Assert.Null(placementFilterSpan);

                // 查找激活 跟踪跨度：其父级应为 Grain 调用，而该调用的父级为 test-parent
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);
                Assert.NotNull(activationSpan);
                Assert.Equal(testParentTraceId, activationSpan.TraceId.ToString());

                // 查找 OnActivateAsync 跟踪跨度：其父级应为激活 跟踪跨度
                var onActivateSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.OnActivate);
                Assert.Null(onActivateSpan);

                // 查找目录注册 跟踪跨度：其父级应为激活 跟踪跨度
                var directoryRegisterSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.RegisterDirectoryEntry);
                Assert.NotNull(directoryRegisterSpan);
                Assert.Equal(testParentTraceId, directoryRegisterSpan.TraceId.ToString());
                Assert.Equal(activationSpan.SpanId.ToString(), directoryRegisterSpan.ParentSpanId.ToString());
            }
            finally
            {
                parent!.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationSpanIncludesFilter()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-filter");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IFilteredActivityGrain>(Random.Shared.Next());
                // 首次调用应强制触发激活
                _ = await grain.GetActivityId();

                // 预期至少有一个与激活相关的 Activity
                var activationActivities = Started.Where(a => a.Source.Name == ActivitySources.LifecycleActivitySourceName).ToList();
                Assert.True(activationActivities.Count > 0, "Expected activation tracing activity to be created, but none were observed.");

                // 验证所有预期 Span 均存在，且正确地以 test-parent 为父级。
                var testParentTraceId = parent!.TraceId.ToString();

                // 查找放置 跟踪跨度：其父级应为 Grain 调用，而该调用的父级为 test-parent
                var placementSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.PlaceGrain);
                Assert.NotNull(placementSpan);
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找放置筛选器 跟踪跨度：其跟踪 ID 应与 test-parent 相同
                var placementFilterSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.FilterPlacementCandidates);
                Assert.NotNull(placementFilterSpan);
                Assert.Equal(testParentTraceId, placementFilterSpan.TraceId.ToString());
                Assert.Equal("TracingTestPlacementFilterStrategy", placementFilterSpan.Tags.FirstOrDefault(t => t.Key == "orleans.placement.filter.type").Value);

                // 查找激活 跟踪跨度：其父级应为 Grain 调用，而该调用的父级为 test-parent
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);
                Assert.NotNull(activationSpan);
                Assert.Equal(testParentTraceId, activationSpan.TraceId.ToString());

                // 查找 OnActivateAsync 跟踪跨度：其父级应为激活 跟踪跨度
                var onActivateSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.OnActivate);
                Assert.NotNull(onActivateSpan);
                Assert.Equal(testParentTraceId, onActivateSpan.TraceId.ToString());
                Assert.Equal(activationSpan.SpanId.ToString(), onActivateSpan.ParentSpanId.ToString());

                // 查找目录注册 跟踪跨度：其父级应为激活 跟踪跨度
                var directoryRegisterSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.RegisterDirectoryEntry);
                Assert.NotNull(directoryRegisterSpan);
                Assert.Equal(testParentTraceId, directoryRegisterSpan.TraceId.ToString());
                Assert.Equal(activationSpan.SpanId.ToString(), directoryRegisterSpan.ParentSpanId.ToString());
            }
            finally
            {
                parent!.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationSpanIncludesMultipleFilters()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-multi-filter");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IMultiFilteredActivityGrain>(Random.Shared.Next());
                // 首次调用应强制触发激活
                _ = await grain.GetActivityId();

                // 验证所有预期 Span 均存在，且正确地以 test-parent 为父级。
                var testParentTraceId = parent!.TraceId.ToString();
                var testParentSpanId = parent.SpanId.ToString();

                // 查找放置 跟踪跨度
                var placementSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.PlaceGrain);
                Assert.NotNull(placementSpan);
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找全部放置筛选器 跟踪跨度：应该有两个（每个筛选器一个）
                var placementFilterSpans = Started
                    .Where(a => a.OperationName == ActivityNames.FilterPlacementCandidates)
                    .OrderBy(a => a.StartTimeUtc)
                    .ToList();
                Assert.Equal(2, placementFilterSpans.Count);

                // 两个筛选器 跟踪跨度 的跟踪 ID 均应与 test-parent 相同
                foreach (var filterSpan in placementFilterSpans)
                {
                    Assert.Equal(testParentTraceId, filterSpan.TraceId.ToString());
                    // 每个筛选器 跟踪跨度 的直接父级都应为 PlaceGrain 跟踪跨度
                    Assert.Equal(placementSpan.SpanId.ToString(), filterSpan.ParentSpanId.ToString());
                }

                // 验证两个筛选器均已执行
                var filterTypes = placementFilterSpans
                    .Select(span => span.Tags.FirstOrDefault(t => t.Key == "orleans.placement.filter.type").Value)
                    .ToHashSet();
                Assert.Contains("TracingTestPlacementFilterStrategy", filterTypes);
                Assert.Contains("SecondTracingTestPlacementFilterStrategy", filterTypes);
            }
            finally
            {
                parent!.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task PersistentStateReadSpanIsCreatedDuringActivation()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-storage");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IPersistentStateActivityGrain>(Random.Shared.Next());
                // 首次调用应强制触发激活，进而触发状态读取
                _ = await grain.GetActivityId();

                // 预期至少有一个与激活相关的 Activity
                var activationActivities = Started.Where(a => a.Source.Name == ActivitySources.LifecycleActivitySourceName).ToList();
                Assert.True(activationActivities.Count > 0, "Expected activation tracing activity to be created, but none were observed.");

                // 验证所有预期 Span 均存在，且正确地以 test-parent 为父级。
                var testParentTraceId = parent!.TraceId.ToString();

                // 查找激活 跟踪跨度：其父级应为 Grain 调用，而该调用的父级为 test-parent
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain && a.Tags.First(kv => kv.Key == "orleans.grain.type").Value == "persistentstateactivity");
                Assert.NotNull(activationSpan);
                Assert.Equal(testParentTraceId, activationSpan.TraceId.ToString());

                // 查找存储读取 跟踪跨度：其跟踪 ID 应与 test-parent 相同
                var storageReadSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.StorageRead);
                Assert.NotNull(storageReadSpan);
                Assert.Equal(testParentTraceId, storageReadSpan.TraceId.ToString());

                // 验证存储读取 跟踪跨度 包含预期标记
                Assert.Equal("MemoryGrainStorage", storageReadSpan.Tags.FirstOrDefault(t => t.Key == "orleans.storage.provider").Value);
                Assert.Equal("state", storageReadSpan.Tags.FirstOrDefault(t => t.Key == "orleans.storage.state.name").Value);
                Assert.Equal("PersistentStateActivityGrainState", storageReadSpan.Tags.FirstOrDefault(t => t.Key == "orleans.storage.state.type").Value);

                // 验证 Grain ID 标记存在
                var grainIdTag = storageReadSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value;
                Assert.NotNull(grainIdTag);
            }
            finally
            {
                parent!.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试 Grain 迁移期间是否创建提取状态与恢复状态 跟踪跨度。
        /// 验证迁移过程是否正确创建跟踪跨度，分别用于
        /// 提取状态（在源 Silo 上）及恢复状态（在目标 Silo 上）。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task MigrationSpansAreCreatedDuringGrainMigration()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-migration");
            parent?.Start();
            try
            {
                // 创建 Grain 并设置部分状态
                var grain = _fixture.GrainFactory.GetGrain<IMigrationTracingTestGrain>(Random.Shared.Next());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找另一个可迁移到的 Silo。
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 通过放置提示触发迁移，促使放置决策器使用目标 Silo
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 验证状态已保留（此操作也会等待迁移完成）
                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);

                // 等待一段时间，使所有活动完成。
                await Task.Delay(500, TestContext.Current.CancellationToken);

                var testParentTraceId = parent!.TraceId.ToString();

                // 验证提取状态 跟踪跨度 已创建
                var dehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationDehydrate).ToList();
                Assert.True(dehydrateSpans.Count > 0, "Expected at least one dehydrate span to be created during migration");

                var dehydrateSpan = dehydrateSpans.First();
                Assert.NotNull(dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.NotNull(dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.silo.id").Value);
                Assert.NotNull(dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.activation.id").Value);
                // 验证目标 Silo 标记存在
                Assert.Equal(targetHost.ToString(), dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.migration.target.silo").Value);
                // 验证提取状态 跟踪跨度 的父级为迁移请求跟踪
                Assert.Equal(testParentTraceId, dehydrateSpan.TraceId.ToString());

                // 验证目标 Silo 上已创建恢复状态 跟踪跨度
                var rehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationRehydrate).ToList();
                Assert.True(rehydrateSpans.Count > 0, "Expected at least one rehydrate span to be created during migration");

                var rehydrateSpan = rehydrateSpans.First();
                Assert.NotNull(rehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.NotNull(rehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.silo.id").Value);
                Assert.NotNull(rehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.activation.id").Value);
                // 验证恢复状态 跟踪跨度 包含先前注册信息的标记
                Assert.NotNull(rehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.rehydrate.previousRegistration").Value);
            }
            finally
            {
                parent?.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试 FilterPlacementCandidates 跟踪跨度 是否正确以 PlaceGrain 跟踪跨度 为父级，
        /// 即迁移通过 PlaceGrainAsync 触发放置的情况。
        /// 此测试覆盖 PlaceGrainAsync（而非 PlacementWorker 消息路径）
        /// 调用放置决策器，继而调用带筛选器的 GetCompatibleSilos 的代码路径。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task MigrationPlacementFilterSpanIsParentedUnderPlaceGrainSpan()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-migration-filter");
            parent?.Start();
            try
            {
                // 创建同时具有放置筛选器和迁移支持的 Grain
                var grain = _fixture.GrainFactory.GetGrain<IMigrationFilterTracingTestGrain>(Random.Shared.Next());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找另一个可迁移到的 Silo。
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 清除 Activity，以便集中检查迁移放置
                Started.Clear();

                // 使用放置提示触发迁移
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 验证状态已保留（此操作也会等待迁移完成）
                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);

                // 等待一段时间，使所有活动完成。
                await Task.Delay(500, TestContext.Current.CancellationToken);

                var testParentTraceId = parent!.TraceId.ToString();

                // 查找迁移期间调用 PlaceGrainAsync 所创建的 PlaceGrain 跟踪跨度
                var placementSpans = Started.Where(a => a.OperationName == ActivityNames.PlaceGrain).ToList();
                Assert.True(placementSpans.Count > 0, "Expected at least one PlaceGrain span during migration");

                var placementSpan = placementSpans.First();
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找 FilterPlacementCandidates 跟踪跨度：跟踪 ID 应相同
                var filterSpans = Started.Where(a => a.OperationName == ActivityNames.FilterPlacementCandidates).ToList();
                Assert.True(filterSpans.Count > 0, "Expected at least one FilterPlacementCandidates span during migration with filter");

                var filterSpan = filterSpans.First();
                Assert.Equal(testParentTraceId, filterSpan.TraceId.ToString());
                Assert.Equal("TracingTestPlacementFilterStrategy", filterSpan.Tags.FirstOrDefault(t => t.Key == "orleans.placement.filter.type").Value);

                // 筛选器 跟踪跨度 应为 PlaceGrain 跟踪跨度 的子级
                Assert.Equal(placementSpan.SpanId.ToString(), filterSpan.ParentSpanId.ToString());
            }
            finally
            {
                parent?.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试具有持久化状态的 Grain 迁移时是否创建提取状态及恢复状态 跟踪跨度。
        /// 验证 IPersistentState 参与迁移，并创建正确的跟踪跨度。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task MigrationSpansAreCreatedForGrainWithPersistentState()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-migration-persistent");
            parent?.Start();
            try
            {
                // 创建具有持久化状态的 Grain，并设置部分状态
                var grain = _fixture.GrainFactory.GetGrain<IMigrationPersistentStateTracingTestGrain>(Random.Shared.Next());
                var expectedStateA = Random.Shared.Next();
                var expectedStateB = Random.Shared.Next();
                await grain.SetState(expectedStateA, expectedStateB);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找另一个可迁移到的 Silo。
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 使用放置提示触发迁移
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 等待迁移完成
                GrainAddress newAddress;
                do
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                    newAddress = await grain.GetGrainAddress();
                } while (newAddress.ActivationId == originalAddress.ActivationId);

                // 验证 Grain 已迁移至目标 Silo
                Assert.Equal(targetHost, newAddress.SiloAddress);

                // 验证状态已保留
                var (actualA, actualB) = await grain.GetState();
                Assert.Equal(expectedStateA, actualA);
                Assert.Equal(expectedStateB, actualB);

                // 等待一段时间，使所有活动完成。
                await Task.Delay(500, TestContext.Current.CancellationToken);

                // 验证未创建提取状态 跟踪跨度（Grain 未实现 IGrainMigrationParticipant）
                var dehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationDehydrate).ToList();
                Assert.True(dehydrateSpans.Count == 0, $"Expected no dehydrate spans for grain without IGrainMigrationParticipant, but found {dehydrateSpans.Count}");

                // 验证未创建恢复状态 跟踪跨度
                var rehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationRehydrate).ToList();
                Assert.True(rehydrateSpans.Count == 0, $"Expected no rehydrate spans for grain without IGrainMigrationParticipant, but found {rehydrateSpans.Count}");

                // 验证恢复状态期间未创建存储读取 跟踪跨度（状态通过迁移上下文传输）
                // 注意：迁移期间不应读取存储，状态通过内存传输
                var storageReadSpansAfterMigration = Started.Where(a => a.OperationName == ActivityNames.StorageRead).ToList();
                // 迁移期间不应读取存储，因为状态通过提取状态上下文传输
                // 只有全新激活时才会读取存储，恢复状态时不会读取

                Assert.Equal(2, storageReadSpansAfterMigration.Count);
            }
            finally
            {
                parent!.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试以下 Grain 迁移时不创建提取状态及恢复状态 跟踪跨度：
        /// 未实现 IGrainMigrationParticipant 的 Grain。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task DehydrateAndRehydrateSpansAreNotCreatedForGrainWithoutMigrationParticipant()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-no-migration-participant");
            parent?.Start();
            try
            {
                // 创建未实现 IGrainMigrationParticipant 的 Grain
                var grain = _fixture.GrainFactory.GetGrain<ISimpleMigrationTracingTestGrain>(Random.Shared.Next());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找另一个可迁移到的 Silo。
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 通过放置提示触发迁移，促使放置决策器使用目标 Silo
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 发起一次调用，确保 Grain 在目标 Silo 上激活
                // 注意：由于 Grain 未参与迁移，状态不会保留
                _ = await grain.GetState();

                // 等待一段时间，使所有活动完成。
                await Task.Delay(500, TestContext.Current.CancellationToken);

                // 验证未创建提取状态 跟踪跨度（Grain 未实现 IGrainMigrationParticipant）
                var dehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationDehydrate).ToList();
                Assert.True(dehydrateSpans.Count == 0, $"Expected no dehydrate spans for grain without IGrainMigrationParticipant, but found {dehydrateSpans.Count}");

                // 验证未创建恢复状态 跟踪跨度
                var rehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationRehydrate).ToList();
                Assert.True(rehydrateSpans.Count == 0, $"Expected no rehydrate spans for grain without IGrainMigrationParticipant, but found {rehydrateSpans.Count}");

                // 验证已创建激活 跟踪跨度（Grain 仍在新 Silo 上激活）
                var activationSpans = Started.Where(a => a.OperationName == ActivityNames.ActivateGrain).ToList();
                Assert.True(activationSpans.Count > 0, "Expected at least one activation span for the migrated grain");
            }
            finally
            {
                parent?.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试包含多个元素的 IAsyncEnumerable Grain 调用是否创建正确的跟踪跨度。
        /// 验证以下情况：
        /// 1. 使用原方法名（GetActivityDataStream）创建会话 跟踪跨度
        /// 2. StartEnumeration、MoveNext 和 DisposeAsync 跟踪跨度 均嵌套在会话 跟踪跨度 下
        /// 3. 所有 跟踪跨度 共享同一跟踪上下文
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task AsyncEnumerableSpansAreCreatedForMultipleElements()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-async-enumerable");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IAsyncEnumerableActivityGrain>(Random.Shared.Next());
                const int elementCount = 5;

                var values = new List<ActivityData>();
                await foreach (var entry in grain.GetActivityDataStream(
                    elementCount,
                    TestContext.Current.CancellationToken).WithBatchSize(1))
                {
                    values.Add(entry);
                }

                // 验证已收到全部元素
                Assert.Equal(elementCount, values.Count);

                // 验证所有预期 Span 均存在，且正确地以 test-parent 为父级。
                var testParentTraceId = parent!.TraceId.ToString();
                var testParentSpanId = parent.SpanId.ToString();

                // 查找 ApplicationGrainActivitySourceName 下的所有 Activity
                var applicationSpans = Started.Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName).ToList();

                // 查找会话 跟踪跨度（逻辑方法调用的 跟踪跨度）
                // 应使用 Grain 接口中的方法名（例如 "IAsyncEnumerableActivityGrain/GetActivityDataStream"）
                var sessionSpans = applicationSpans.Where(a => a.OperationName.Contains("GetActivityDataStream")).ToList();
                Assert.True(sessionSpans.Count >= 1, "Expected at least one session span with GetActivityDataStream operation name");

                var sessionSpan = sessionSpans.First();
                Assert.Equal(testParentTraceId, sessionSpan.TraceId.ToString());
                Assert.Equal(testParentSpanId, sessionSpan.ParentSpanId.ToString());

                // 验证会话 跟踪跨度 包含请求 ID 标记
                var requestIdTag = sessionSpan.Tags.FirstOrDefault(t => t.Key == "orleans.async_enumerable.request_id").Value;
                Assert.NotNull(requestIdTag);

                var sessionSpanId = sessionSpan.SpanId.ToString();

                // 找到所有跟踪跨度（包括运行时跟踪跨度）以验证父子关系。
                var allSpans = Started.ToList();

                // 查找 StartEnumeration 跟踪跨度：应嵌套在会话 跟踪跨度 下（位于 RuntimeActivitySourceName 中）
                // 筛选出客户端 跟踪跨度（直接以会话 跟踪跨度 为父级的 跟踪跨度）
                var startEnumerationSpans = allSpans
                    .Where(a => a.OperationName.Contains("StartEnumeration") && a.ParentSpanId.ToString() == sessionSpanId)
                    .ToList();
                Assert.True(startEnumerationSpans.Count >= 1, "Expected at least one StartEnumeration span parented to session span");

                var startEnumerationSpan = startEnumerationSpans.First();
                Assert.Equal(testParentTraceId, startEnumerationSpan.TraceId.ToString());

                // 查找 MoveNext 跟踪跨度：应嵌套在会话 跟踪跨度 下（位于 RuntimeActivitySourceName 中）
                // 筛选出客户端 跟踪跨度（直接以会话 跟踪跨度 为父级的 跟踪跨度）
                var moveNextSpans = allSpans
                    .Where(a => a.OperationName.Contains("MoveNext") && a.ParentSpanId.ToString() == sessionSpanId)
                    .ToList();
                Assert.True(moveNextSpans.Count >= 1, $"Expected at least one MoveNext span parented to session span, found {moveNextSpans.Count}");

                // 所有客户端 MoveNext 跟踪跨度 应共享同一跟踪 ID
                foreach (var moveNextSpan in moveNextSpans)
                {
                    Assert.Equal(testParentTraceId, moveNextSpan.TraceId.ToString());
                }

                // 查找 DisposeAsync 跟踪跨度：应嵌套在会话 跟踪跨度 下（位于 RuntimeActivitySourceName 中）
                // 筛选出客户端 跟踪跨度（直接以会话 跟踪跨度 为父级的 跟踪跨度）
                var disposeSpans = allSpans
                    .Where(a => a.OperationName.Contains("DisposeAsync") && a.ParentSpanId.ToString() == sessionSpanId)
                    .ToList();
                Assert.True(disposeSpans.Count >= 1, "Expected at least one DisposeAsync span parented to session span");

                var disposeSpan = disposeSpans.First();
                Assert.Equal(testParentTraceId, disposeSpan.TraceId.ToString());

                // 验证收到的每个 ActivityData 都包含活动信息
                // （验证枚举期间跟踪上下文已传播至 Grain 内部）
                foreach (var activityData in values)
                {
                    Assert.NotNull(activityData);
                    Assert.NotNull(activityData.Id);
                }
            }
            finally
            {
                parent?.Stop();
                AssertNoApplicationSpansParentedByRuntimeSpans();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 断言 ApplicationGrainActivitySourceName 的 跟踪跨度 均不以 RuntimeActivitySourceName 的 跟踪跨度 为父级。
        /// 这确保仅添加 ApplicationGrainActivitySourceName（未添加 RuntimeActivitySourceName）时，
        /// 不会因缺少 RuntimeActivitySourceName 中原本用于
        /// 传播跟踪上下文的 跟踪跨度，而将孤立的跟踪放在根级别。
        /// </summary>
        private void AssertNoApplicationSpansParentedByRuntimeSpans()
        {
            var activities = Started.ToList();
            var activityById = activities
                .Where(a => a.Id is not null)
                .ToDictionary(a => a.Id!);

            var applicationSpans = activities
                .Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName)
                .ToList();

            var violations = new List<(Activity Child, Activity Parent)>();

            foreach (var appSpan in applicationSpans)
            {
                if (appSpan.ParentId is not null && activityById.TryGetValue(appSpan.ParentId, out var parentActivity))
                {
                    if (parentActivity.Source.Name == ActivitySources.RuntimeActivitySourceName)
                    {
                        violations.Add((appSpan, parentActivity));
                    }
                }
            }

            if (violations.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Found {violations.Count} ApplicationGrainActivitySourceName span(s) with RuntimeActivitySourceName parent(s):");
                foreach (var (child, violationParent) in violations)
                {
                    sb.AppendLine($"  - Application span '{child.OperationName}' (Id: {child.Id}) has Runtime parent '{violationParent.OperationName}' (Id: {violationParent.Id})");
                }
                Assert.Fail(sb.ToString());
            }
        }

        private void PrintActivityDiagnostics()
        {
            var activities = Started.ToList();
            if (activities.Count == 0)
            {
                _output.WriteLine("No activities captured.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║                         CAPTURED ACTIVITIES DIAGNOSTIC                       ║");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");
            sb.AppendLine($"║ Total Activities: {activities.Count,-59}║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // 按来源分组
            var bySource = activities.GroupBy(a => a.Source.Name).OrderBy(g => g.Key);

            foreach (var sourceGroup in bySource)
            {
                sb.AppendLine($"┌─ Source: {sourceGroup.Key}");
                sb.AppendLine("│");

                var sourceActivities = sourceGroup.OrderBy(a => a.StartTimeUtc).ToList();
                for (int i = 0; i < sourceActivities.Count; i++)
                {
                    var activity = sourceActivities[i];
                    var isLast = i == sourceActivities.Count - 1;
                    var prefix = isLast ? "└──" : "├──";
                    var continuePrefix = isLast ? "   " : "│  ";

                    sb.AppendLine($"│ {prefix} [{activity.OperationName}]");
                    sb.AppendLine($"│ {continuePrefix}   ID: {activity.Id ?? "(null)"}");

                    if (activity.ParentId is not null)
                    {
                        sb.AppendLine($"│ {continuePrefix}   Parent: {activity.ParentId}");
                    }
                    else
                    {
                        sb.AppendLine($"│ {continuePrefix}   Parent: (root)");
                    }

                    sb.AppendLine($"│ {continuePrefix}   Duration: {activity.Duration.TotalMilliseconds:F2}ms");
                    sb.AppendLine($"│ {continuePrefix}   Status: {activity.Status}");

                    var tags = activity.Tags.ToList();
                    if (tags.Count > 0)
                    {
                        sb.AppendLine($"│ {continuePrefix}   Tags:");
                        foreach (var tag in tags)
                        {
                            sb.AppendLine($"│ {continuePrefix}     • {tag.Key}: {tag.Value}");
                        }
                    }

                    sb.AppendLine("│");
                }

                sb.AppendLine();
            }

            // 输出层级视图
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("                              ACTIVITY HIERARCHY                               ");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();

            var activityById = activities.Where(a => a.Id is not null).ToDictionary(a => a.Id!);
            var roots = activities.Where(a => a.ParentId is null || !activityById.ContainsKey(a.ParentId)).ToList();

            foreach (var root in roots.OrderBy(a => a.StartTimeUtc))
            {
                PrintActivityTree(sb, root, activityById, activities, "", true);
            }

            _output.WriteLine(sb.ToString());
        }

        private static void PrintActivityTree(
            StringBuilder sb,
            Activity activity,
            Dictionary<string, Activity> activityById,
            List<Activity> allActivities,
            string indent,
            bool isLast)
        {
            var marker = isLast ? "└── " : "├── ";
            var durationStr = activity.Duration.TotalMilliseconds > 0
                ? $" ({activity.Duration.TotalMilliseconds:F2}ms)"
                : "";

            sb.AppendLine($"{indent}{marker}[{activity.Source.Name}] {activity.OperationName}{durationStr}");

            var children = allActivities
                .Where(a => a.ParentId == activity.Id)
                .OrderBy(a => a.StartTimeUtc)
                .ToList();

            var childIndent = indent + (isLast ? "    " : "│   ");

            for (int i = 0; i < children.Count; i++)
            {
                PrintActivityTree(sb, children[i], activityById, allActivities, childIndent, i == children.Count - 1);
            }
        }
    }


    /// <summary>
    /// 用于跟踪测试的测试放置筛选器特性。
    /// </summary>
    public class TracingTestPlacementFilterAttribute() : PlacementFilterAttribute(new TracingTestPlacementFilterStrategy());

    /// <summary>
    /// 用于跟踪测试的测试放置筛选器策略。
    /// </summary>
    public class TracingTestPlacementFilterStrategy() : PlacementFilterStrategy(order: 1)
    {
    }

    /// <summary>
    /// 测试放置筛选器决策器，直接放行所有 Silo。
    /// </summary>
    public class TracingTestPlacementFilterDirector : IPlacementFilterDirector
    {
        public IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos)
        {
            return silos;
        }
    }

    /// <summary>
    /// 用于多筛选器跟踪测试的第二个测试放置筛选器特性。
    /// </summary>
    public class SecondTracingTestPlacementFilterAttribute() : PlacementFilterAttribute(new SecondTracingTestPlacementFilterStrategy());

    /// <summary>
    /// 用于多筛选器跟踪测试的第二个测试放置筛选器策略。
    /// </summary>
    public class SecondTracingTestPlacementFilterStrategy() : PlacementFilterStrategy(order: 2)
    {
    }

    /// <summary>
    /// 第二个测试放置筛选器决策器，直接放行所有 Silo。
    /// </summary>
    public class SecondTracingTestPlacementFilterDirector : IPlacementFilterDirector
    {
        public IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos)
        {
            return silos;
        }
    }

    /// <summary>
    /// 用于跟踪测试、带有放置筛选器的测试 Grain 接口。
    /// </summary>
    public interface IFilteredActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
    }

    /// <summary>
    /// 用于跟踪测试、带有放置筛选器的测试 Grain 实现。
    /// </summary>
    [TracingTestPlacementFilter]
    public class FilteredActivityGrain : Grain, IFilteredActivityGrain
    {
        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            var result = new ActivityData()
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            };

            return Task.FromResult<ActivityData?>(result);
        }
    }

    /// <summary>
    /// 用于跟踪测试、带有多个放置筛选器的测试 Grain 接口。
    /// </summary>
    public interface IMultiFilteredActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
    }

    /// <summary>
    /// 用于跟踪测试、带有多个放置筛选器的测试 Grain 实现。
    /// </summary>
    [TracingTestPlacementFilter]
    [SecondTracingTestPlacementFilter]
    public class MultiFilteredActivityGrain : Grain, IMultiFilteredActivityGrain
    {
        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            var result = new ActivityData()
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            };

            return Task.FromResult<ActivityData?>(result);
        }
    }



    /// <summary>
    /// 用于跟踪测试、具有持久化状态的测试 Grain 接口。
    /// </summary>
    public interface IPersistentStateActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
        Task<int> GetStateValue();
    }

    /// <summary>
    /// 用于持久化状态跟踪测试的测试 Grain 状态。
    /// </summary>
    [GenerateSerializer]
    public class PersistentStateActivityGrainState
    {
        [Id(0)]
        public int Value { get; set; }
    }

    /// <summary>
    /// 用于跟踪测试、具有持久化状态的测试 Grain 实现。
    /// </summary>
    [TracingTestPlacementFilter]
    public class PersistentStateActivityGrain : Grain, IPersistentStateActivityGrain
    {
        private readonly IPersistentState<PersistentStateActivityGrainState> _state;

        public PersistentStateActivityGrain(
            [PersistentState("state")] IPersistentState<PersistentStateActivityGrainState> state)
        {
            _state = state;
        }

        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            var result = new ActivityData()
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            };

            return Task.FromResult<ActivityData?>(result);
        }

        public Task<int> GetStateValue()
        {
            return Task.FromResult(_state.State!.Value);
        }
    }



    /// <summary>
    /// 用于迁移跟踪测试的测试 Grain 接口。
    /// </summary>
    public interface IMigrationTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
    }

    /// <summary>
    /// 用于迁移跟踪测试的测试 Grain 状态。
    /// </summary>
    [GenerateSerializer]
    public class MigrationTracingTestGrainState
    {
        [Id(0)]
        public int Value { get; set; }
    }

    /// <summary>
    /// 用于迁移跟踪测试的测试 Grain 实现。
    /// 实现 IGrainMigrationParticipant 以参与迁移。
    /// 使用 RandomPlacement，允许迁移到不同 Silo。
    /// </summary>
    [RandomPlacement]
    public class MigrationTracingTestGrain : Grain, IMigrationTracingTestGrain, IGrainMigrationParticipant
    {
        private int _state;

        public ValueTask<int> GetState() => new(_state);

        public ValueTask SetState(int state)
        {
            _state = state;
            return default;
        }

        public void OnDehydrate(IDehydrationContext migrationContext)
        {
            migrationContext.TryAddValue("state", _state);
        }

        public void OnRehydrate(IRehydrationContext migrationContext)
        {
            migrationContext.TryGetValue("state", out _state);
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);
    }

    /// <summary>
    /// 用于带放置筛选器的迁移跟踪测试的测试 Grain 接口。
    /// </summary>
    public interface IMigrationFilterTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
    }

    /// <summary>
    /// 用于带放置筛选器的迁移跟踪测试的测试 Grain 实现。
    /// 结合 IGrainMigrationParticipant 与放置筛选器，验证
    /// 迁移期间 FilterPlacementCandidates 跟踪跨度 正确以 PlaceGrain 跟踪跨度 为父级。
    /// </summary>
    [RandomPlacement]
    [TracingTestPlacementFilter]
    public class MigrationFilterTracingTestGrain : Grain, IMigrationFilterTracingTestGrain, IGrainMigrationParticipant
    {
        private int _state;

        public ValueTask<int> GetState() => new(_state);

        public ValueTask SetState(int state)
        {
            _state = state;
            return default;
        }

        public void OnDehydrate(IDehydrationContext migrationContext)
        {
            migrationContext.TryAddValue("state", _state);
        }

        public void OnRehydrate(IRehydrationContext migrationContext)
        {
            migrationContext.TryGetValue("state", out _state);
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);
    }

    /// <summary>
    /// 用于不使用 IGrainMigrationParticipant 的迁移跟踪测试的 Grain 接口。
    /// </summary>
    public interface ISimpleMigrationTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
    }

    /// <summary>
    /// 用于迁移跟踪测试、未实现 IGrainMigrationParticipant 的测试 Grain 实现。
    /// 使用 RandomPlacement，允许迁移到不同 Silo。
    /// 此 Grain 不参与提取状态或恢复状态，因此会在迁移期间丢失状态。
    /// </summary>
    [RandomPlacement]
    public class SimpleMigrationTracingTestGrain : Grain, ISimpleMigrationTracingTestGrain
    {
        private int _state;

        public ValueTask<int> GetState() => new(_state);

        public ValueTask SetState(int state)
        {
            _state = state;
            return default;
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);
    }

    /// <summary>
    /// 用于迁移跟踪测试、具有持久化状态的测试 Grain 接口。
    /// </summary>
    public interface IMigrationPersistentStateTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask SetState(int a, int b);
        ValueTask<(int A, int B)> GetState();
        ValueTask<GrainAddress> GetGrainAddress();
    }

    /// <summary>
    /// 用于迁移跟踪测试、带 IPersistentState 的测试 Grain 实现。
    /// 使用 RandomPlacement，允许迁移到不同 Silo。
    /// </summary>
    [RandomPlacement]
    public class MigrationPersistentStateTracingTestGrain : Grain, IMigrationPersistentStateTracingTestGrain
    {
        private readonly IPersistentState<MigrationTracingTestGrainState> _stateA;
        private readonly IPersistentState<MigrationTracingTestGrainState> _stateB;

        public MigrationPersistentStateTracingTestGrain(
            [PersistentState("a")] IPersistentState<MigrationTracingTestGrainState> stateA,
            [PersistentState("b")] IPersistentState<MigrationTracingTestGrainState> stateB)
        {
            _stateA = stateA;
            _stateB = stateB;
        }

        public ValueTask<(int A, int B)> GetState() => new((_stateA.State!.Value, _stateB.State!.Value));

        public ValueTask SetState(int a, int b)
        {
            _stateA.State!.Value = a;
            _stateB.State!.Value = b;
            return default;
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);
    }

    /// <summary>
    /// 在异步可枚举序列上调用 DisposeAsync 之后触发停用的 Grain 调用筛选器。
    /// 确保完全结束枚举后才停用。
    /// </summary>
    public class DeactivateAfterDisposeAsyncFilter : IIncomingGrainCallFilter
    {
        public async Task Invoke(IIncomingGrainCallContext context)
        {
            await context.Invoke();

            if (context.InterfaceMethod?.Name == "DisposeAsync" &&
                context.InterfaceMethod.DeclaringType?.FullName == "Orleans.Runtime.IAsyncEnumerableGrainExtension")
            {
                if (context.Grain is Grain grain)
                {
                    grain.DeactivateOnIdle();
                }
            }
        }
    }

}
