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
    /// 测试失败, 显示缺少启动跟踪的间隔时间 。
    /// 期望在第一次 grain 激活时启动活动。
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
                builder.Options.InitialSilosCount = 2; // 移民测试需要2 silos
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
                // 第一次呼叫应该启动
                _ = await grain.GetActivityId();

                // 期望至少开展一项与激活有关的活动
                var activationActivities = Started.Where(a => a.Source.Name == ActivitySources.LifecycleActivitySourceName).ToList();
                Assert.True(activationActivities.Count > 0, "Expected activation tracing activity to be created, but none were observed.");

                // 测试对象中应核查所有预期的间隔时间,并适当定位于测试对象
                var testParentTraceId = parent!.TraceId.ToString();

                // 查找定位的放置跨度 - 应用定位到 grain 呼叫, 并挂给测试亲
                var placementSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.PlaceGrain);
                Assert.NotNull(placementSpan);
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找放置过滤器的宽度 - 与测试亲一样共享跟踪身份
                var placementFilterSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.FilterPlacementCandidates);
                Assert.Null(placementFilterSpan);

                // 查找激活的跨度 - 应该嵌入到 grain 呼叫中, 以测试父
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);
                Assert.NotNull(activationSpan);
                Assert.Equal(testParentTraceId, activationSpan.TraceId.ToString());

                // 查找 OnActivateAsync 的间隔 - 应与激活间隔相侧
                var onActivateSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.OnActivate);
                Assert.Null(onActivateSpan);

                // 查找目录登记器的横跨 - 应该在边上定位以激活
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
                // 第一次呼叫应该启动
                _ = await grain.GetActivityId();

                // 期望至少开展一项与激活有关的活动
                var activationActivities = Started.Where(a => a.Source.Name == ActivitySources.LifecycleActivitySourceName).ToList();
                Assert.True(activationActivities.Count > 0, "Expected activation tracing activity to be created, but none were observed.");

                // 测试对象中应核查所有预期的间隔时间,并适当定位于测试对象
                var testParentTraceId = parent!.TraceId.ToString();

                // 查找定位的放置跨度 - 应用定位到 grain 呼叫, 并挂给测试亲
                var placementSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.PlaceGrain);
                Assert.NotNull(placementSpan);
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找放置过滤器的宽度 - 与测试亲一样共享跟踪身份
                var placementFilterSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.FilterPlacementCandidates);
                Assert.NotNull(placementFilterSpan);
                Assert.Equal(testParentTraceId, placementFilterSpan.TraceId.ToString());
                Assert.Equal("TracingTestPlacementFilterStrategy", placementFilterSpan.Tags.FirstOrDefault(t => t.Key == "orleans.placement.filter.type").Value);

                // 查找激活的跨度 - 应该嵌入到 grain 呼叫中, 以测试父
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);
                Assert.NotNull(activationSpan);
                Assert.Equal(testParentTraceId, activationSpan.TraceId.ToString());

                // 查找 OnActivateAsync 的间隔 - 应与激活间隔相侧
                var onActivateSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.OnActivate);
                Assert.NotNull(onActivateSpan);
                Assert.Equal(testParentTraceId, onActivateSpan.TraceId.ToString());
                Assert.Equal(activationSpan.SpanId.ToString(), onActivateSpan.ParentSpanId.ToString());

                // 查找目录登记器的横跨 - 应该在边上定位以激活
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
                // 第一次呼叫应该启动
                _ = await grain.GetActivityId();

                // 测试对象中应核查所有预期的间隔时间,并适当定位于测试对象
                var testParentTraceId = parent!.TraceId.ToString();
                var testParentSpanId = parent.SpanId.ToString();

                // 查找放置范围
                var placementSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.PlaceGrain);
                Assert.NotNull(placementSpan);
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找所有放置位置过滤宽度 - 应该为 2( 每个筛选一个)
                var placementFilterSpans = Started
                    .Where(a => a.OperationName == ActivityNames.FilterPlacementCandidates)
                    .OrderBy(a => a.StartTimeUtc)
                    .ToList();
                Assert.Equal(2, placementFilterSpans.Count);

                // 两个过滤宽度应和测试对象共享相同的跟踪标识符
                foreach (var filterSpan in placementFilterSpans)
                {
                    Assert.Equal(testParentTraceId, filterSpan.TraceId.ToString());
                    // 每个过滤宽度应直接嵌入到 PlaceGrain 宽度
                    Assert.Equal(placementSpan.SpanId.ToString(), filterSpan.ParentSpanId.ToString());
                }

                // 验证两个过滤器是否都执行
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
                // 第一次呼叫后, 启动触发应被改为
                _ = await grain.GetActivityId();

                // 期望至少开展一项与激活有关的活动
                var activationActivities = Started.Where(a => a.Source.Name == ActivitySources.LifecycleActivitySourceName).ToList();
                Assert.True(activationActivities.Count > 0, "Expected activation tracing activity to be created, but none were observed.");

                // 测试对象中应核查所有预期的间隔时间,并适当定位于测试对象
                var testParentTraceId = parent!.TraceId.ToString();

                // 查找激活的跨度 - 应该嵌入到 grain 呼叫中, 以测试父
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain && a.Tags.First(kv => kv.Key == "orleans.grain.type").Value == "persistentstateactivity");
                Assert.NotNull(activationSpan);
                Assert.Equal(testParentTraceId, activationSpan.TraceId.ToString());

                // 查找存储的读跨 - 应与测试对象共享相同的跟踪身份
                var storageReadSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.StorageRead);
                Assert.NotNull(storageReadSpan);
                Assert.Equal(testParentTraceId, storageReadSpan.TraceId.ToString());

                // 校验存储已读过宽度, 需要标记
                Assert.Equal("MemoryGrainStorage", storageReadSpan.Tags.FirstOrDefault(t => t.Key == "orleans.storage.provider").Value);
                Assert.Equal("state", storageReadSpan.Tags.FirstOrDefault(t => t.Key == "orleans.storage.state.name").Value);
                Assert.Equal("PersistentStateActivityGrainState", storageReadSpan.Tags.FirstOrDefault(t => t.Key == "orleans.storage.state.type").Value);

                // 验证 grain ID标记存在
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
        /// 测试在以下试验期间产生脱水和再水合物间隙 grain 移徙。
        /// 证明移徙过程为两者建立了适当的追踪范围
        /// 脱水(关于源 silo)和补液(关于目标 silo)。
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
                // 创建 grain 并设置某种状态
                var grain = _fixture.GrainFactory.GetGrain<IMigrationTracingTestGrain>(Random.Shared.Next());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找不同的 silo 迁移到
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 触发移民,暗示要强迫安置主任使用目标 silo
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 校验国家是否被保存(这也等待移民完成)
                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);

                // 给所有活动一些时间完成
                await Task.Delay(500, TestContext.Current.CancellationToken);

                var testParentTraceId = parent!.TraceId.ToString();

                // 创建了检测脱水的宽度
                var dehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationDehydrate).ToList();
                Assert.True(dehydrateSpans.Count > 0, "Expected at least one dehydrate span to be created during migration");

                var dehydrateSpan = dehydrateSpans.First();
                Assert.NotNull(dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.NotNull(dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.silo.id").Value);
                Assert.NotNull(dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.activation.id").Value);
                // 目标 silo 标签存在
                Assert.Equal(targetHost.ToString(), dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.migration.target.silo").Value);
                // 检测脱水孔的宽度与移民申请的跟踪相匹配
                Assert.Equal(testParentTraceId, dehydrateSpan.TraceId.ToString());

                // 目标上创建了检测再水合的宽度 silo
                var rehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationRehydrate).ToList();
                Assert.True(rehydrateSpans.Count > 0, "Expected at least one rehydrate span to be created during migration");

                var rehydrateSpan = rehydrateSpans.First();
                Assert.NotNull(rehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.NotNull(rehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.silo.id").Value);
                Assert.NotNull(rehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.activation.id").Value);
                // 验证水合度的宽度是否具有上一个注册标记
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
        /// 测试 FilterPlacementCandidates 空格的宽度在 PlaceGrain 宽
        /// 当移徙通过 PlaceGrainAsync引发安置时。
        /// 此选项覆盖 PlaceGrainAsync 的代码路径( 不是 PlacementWorker 消息路)
        /// 调用设置主任,该主任用过滤器呼叫 GetCompatibleSilos 。
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
                // 创建有布置过滤器和迁移支持的 grain
                var grain = _fixture.GrainFactory.GetGrain<IMigrationFilterTracingTestGrain>(Random.Shared.Next());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找不同的 silo 迁移到
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 明确活动,着重移徙安置
                Started.Clear();

                // 带有安插提示的触发移徙
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 校验国家是否被保存(这也等待移民完成)
                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);

                // 给所有活动一些时间完成
                await Task.Delay(500, TestContext.Current.CancellationToken);

                var testParentTraceId = parent!.TraceId.ToString();

                // 查找 PlaceGrain 移民过程中创建的 PlaceGrainAsync 调
                var placementSpans = Started.Where(a => a.OperationName == ActivityNames.PlaceGrain).ToList();
                Assert.True(placementSpans.Count > 0, "Expected at least one PlaceGrain span during migration");

                var placementSpan = placementSpans.First();
                Assert.Equal(testParentTraceId, placementSpan.TraceId.ToString());

                // 查找 FilterPlacementCandidates 的横跨 - 应该共享相同的跟踪标识符
                var filterSpans = Started.Where(a => a.OperationName == ActivityNames.FilterPlacementCandidates).ToList();
                Assert.True(filterSpans.Count > 0, "Expected at least one FilterPlacementCandidates span during migration with filter");

                var filterSpan = filterSpans.First();
                Assert.Equal(testParentTraceId, filterSpan.TraceId.ToString());
                Assert.Equal("TracingTestPlacementFilterStrategy", filterSpan.Tags.FirstOrDefault(t => t.Key == "orleans.placement.filter.type").Value);

                // 过滤范围应为 PlaceGrain 跨度的子(子)
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
        /// 测试在 grain 具有持久性状态的迁移过程中产生脱水和补水间隙。
        /// 证明 IPersistentState 参与移徙,并创造适当的追踪间隔。
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
                // 创建有持久性状态的 grain 并设定一定状态
                var grain = _fixture.GrainFactory.GetGrain<IMigrationPersistentStateTracingTestGrain>(Random.Shared.Next());
                var expectedStateA = Random.Shared.Next();
                var expectedStateB = Random.Shared.Next();
                await grain.SetState(expectedStateA, expectedStateB);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找不同的 silo 迁移到
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 带有安插提示的触发移徙
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 等待移民完成
                GrainAddress newAddress;
                do
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                    newAddress = await grain.GetGrainAddress();
                } while (newAddress.ActivationId == originalAddress.ActivationId);

                // 核查向目标迁移的 grain silo
                Assert.Equal(targetHost, newAddress.SiloAddress);

                // 核查国家是否被保存
                var (actualA, actualB) = await grain.GetState();
                Assert.Equal(expectedStateA, actualA);
                Assert.Equal(expectedStateB, actualB);

                // 给所有活动一些时间完成
                await Task.Delay(500, TestContext.Current.CancellationToken);

                // 检测脱水宽度未创建(grain 不执行 IGrainMigrationParticipant)
                var dehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationDehydrate).ToList();
                Assert.True(dehydrateSpans.Count == 0, $"Expected no dehydrate spans for grain without IGrainMigrationParticipant, but found {dehydrateSpans.Count}");

                // 检测再水合度没有创建
                var rehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationRehydrate).ToList();
                Assert.True(rehydrateSpans.Count == 0, $"Expected no rehydrate spans for grain without IGrainMigrationParticipant, but found {rehydrateSpans.Count}");

                // 体液补充期间(通过迁移转移状态)没有创建核查存储的读宽度
                // 注:储存读作不应在移徙期间发生----国家暂时转移
                var storageReadSpansAfterMigration = Started.Where(a => a.OperationName == ActivityNames.StorageRead).ToList();
                // 在移徙期间,不应阅读储存,因为国家是通过脱水方式转移的
                // 储存读取只见于新激活,而非补水

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
        /// 测试脱水和再水合的跨度不是在 grain迁移过程中产生的
        /// 不符合 IGrainMigrationParticipant要求。
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
                // 创建 grain 执行不执行 IGrainMigrationParticipant
                var grain = _fixture.GrainFactory.GetGrain<ISimpleMigrationTracingTestGrain>(Random.Shared.Next());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                // 查找不同的 silo 迁移到
                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                // 触发移民,暗示要强迫安置主任使用目标 silo
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                // 打电话确保 grain 目标启动 silo
                // 注:国家不会被保存,因为 grain 不参与移徙
                _ = await grain.GetState();

                // 给所有活动一些时间完成
                await Task.Delay(500, TestContext.Current.CancellationToken);

                // 检测脱水宽度未创建(grain 不执行 IGrainMigrationParticipant)
                var dehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationDehydrate).ToList();
                Assert.True(dehydrateSpans.Count == 0, $"Expected no dehydrate spans for grain without IGrainMigrationParticipant, but found {dehydrateSpans.Count}");

                // 检测再水合度没有创建
                var rehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationRehydrate).ToList();
                Assert.True(rehydrateSpans.Count == 0, $"Expected no rehydrate spans for grain without IGrainMigrationParticipant, but found {rehydrateSpans.Count}");

                // 验证是否创建了激活周期( grain 在新的 silo上仍然被激活)
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
        /// 测试为 IAsyncEnumerable grain 电话设置了具有多个元素的适当跟踪间隔。
        /// 证明:
        /// 1. 使用原方法名称(GetActivityDataStream)创建一个横跨度
        /// 2. StartEnumeration、 MoveNext和 DisposeAsync 的宽度在届会宽度下嵌套
        /// 3. 所有跨度都具有相同的跟踪背景
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

                // 校验我们收到所有元素
                Assert.Equal(elementCount, values.Count);

                // 测试对象中应核查所有预期的间隔时间,并适当定位于测试对象
                var testParentTraceId = parent!.TraceId.ToString();
                var testParentSpanId = parent.SpanId.ToString();

                // 查找 ApplicationGrainActivitySourceName的所有活动
                var applicationSpans = Started.Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName).ToList();

                // 查找会话范围(逻辑方法调用区域)
                // 这应该包含来自 grain 接口(e.g. "IAsyncEnumerableActivityGrain/GetActivityDataStream" )的方法名称。
                var sessionSpans = applicationSpans.Where(a => a.OperationName.Contains("GetActivityDataStream")).ToList();
                Assert.True(sessionSpans.Count >= 1, "Expected at least one session span with GetActivityDataStream operation name");

                var sessionSpan = sessionSpans.First();
                Assert.Equal(testParentTraceId, sessionSpan.TraceId.ToString());
                Assert.Equal(testParentSpanId, sessionSpan.ParentSpanId.ToString());

                // 校验会的跨度是否有请求的ID标记
                var requestIdTag = sessionSpan.Tags.FirstOrDefault(t => t.Key == "orleans.async_enumerable.request_id").Value;
                Assert.NotNull(requestIdTag);

                var sessionSpanId = sessionSpan.SpanId.ToString();

                // 查找所有用于校验亲子关系的跨度(包括运行时间间隔)
                var allSpans = Started.ToList();

                // 查找 StartEnumeration 范围 - 应该嵌套在会话区域下(在 RuntimeActivitySourceName)
                // 过滤到仅客户端侧跨度( 直接嵌入会话横距的)
                var startEnumerationSpans = allSpans
                    .Where(a => a.OperationName.Contains("StartEnumeration") && a.ParentSpanId.ToString() == sessionSpanId)
                    .ToList();
                Assert.True(startEnumerationSpans.Count >= 1, "Expected at least one StartEnumeration span parented to session span");

                var startEnumerationSpan = startEnumerationSpans.First();
                Assert.Equal(testParentTraceId, startEnumerationSpan.TraceId.ToString());

                // 查找 MoveNext 跨度 - 应该嵌套在会话间隔下( 在 RuntimeActivitySourceName)
                // 过滤到仅客户端侧跨度( 直接嵌入会话横距的)
                var moveNextSpans = allSpans
                    .Where(a => a.OperationName.Contains("MoveNext") && a.ParentSpanId.ToString() == sessionSpanId)
                    .ToList();
                Assert.True(moveNextSpans.Count >= 1, $"Expected at least one MoveNext span parented to session span, found {moveNextSpans.Count}");

                // 所有客户端 MoveNext 宽度应共享相同的跟踪标识符
                foreach (var moveNextSpan in moveNextSpans)
                {
                    Assert.Equal(testParentTraceId, moveNextSpan.TraceId.ToString());
                }

                // 查找 DisposeAsync 跨度 - 应该嵌套在会话间隔下( 在 RuntimeActivitySourceName)
                // 过滤到仅客户端侧跨度( 直接嵌入会话横距的)
                var disposeSpans = allSpans
                    .Where(a => a.OperationName.Contains("DisposeAsync") && a.ParentSpanId.ToString() == sessionSpanId)
                    .ToList();
                Assert.True(disposeSpans.Count >= 1, "Expected at least one DisposeAsync span parented to session span");

                var disposeSpan = disposeSpans.First();
                Assert.Equal(testParentTraceId, disposeSpan.TraceId.ToString());

                // 核查每收到 ActivityData 一次有活动信息
                // (在查点时,将核实的跟踪背景传播到 grain )
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
        /// 与 ApplicationGrainActivitySourceName 没有间隔的发言者有来自 RuntimeActivitySourceName的父母。
        /// 这确保了如果只添加 ApplicationGrainActivitySourceName (不增加 RuntimeActivitySourceName),
        /// 将不会有任何悬吊痕迹因为缺失 RuntimeActivitySourceName 的宽度而根部被挂起
        /// 否则,它会传播跟踪背景。
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

            // 按来源组
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

            // 打印层结构视图
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
    /// 用于跟踪测试的测试放置过滤属性 。
    /// </summary>
    public class TracingTestPlacementFilterAttribute() : PlacementFilterAttribute(new TracingTestPlacementFilterStrategy());

    /// <summary>
    /// 追踪测试的测试布置过滤策略。
    /// </summary>
    public class TracingTestPlacementFilterStrategy() : PlacementFilterStrategy(order: 1)
    {
    }

    /// <summary>
    /// 测试定位过滤器主任,只要通过所有 silos即可。
    /// </summary>
    public class TracingTestPlacementFilterDirector : IPlacementFilterDirector
    {
        public IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos)
        {
            return silos;
        }
    }

    /// <summary>
    /// 使用多个过滤器进行追踪测试的第二个测试定位过滤属性 。
    /// </summary>
    public class SecondTracingTestPlacementFilterAttribute() : PlacementFilterAttribute(new SecondTracingTestPlacementFilterStrategy());

    /// <summary>
    /// 使用多个过滤器进行追踪测试的第二次测试定位过滤策略。
    /// </summary>
    public class SecondTracingTestPlacementFilterStrategy() : PlacementFilterStrategy(order: 2)
    {
    }

    /// <summary>
    /// 第二测试设置过滤器主任,只需通过所有 silos。
    /// </summary>
    public class SecondTracingTestPlacementFilterDirector : IPlacementFilterDirector
    {
        public IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos)
        {
            return silos;
        }
    }

    /// <summary>
    /// 测试 grain 接口与用于跟踪检测的定位过滤器接口。
    /// </summary>
    public interface IFilteredActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
    }

    /// <summary>
    /// 测试 grain 执行,并配有跟踪检测的定位过滤器。
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
    /// 测试 grain 接口与多个定位过滤器的界面,用于跟踪检测。
    /// </summary>
    public interface IMultiFilteredActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
    }

    /// <summary>
    /// 测试 grain ,并配有多个定位过滤器进行追踪检测。
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
    /// 测试 grain 与持久性状态接口,用于追踪检测。
    /// </summary>
    public interface IPersistentStateActivityGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
        Task<int> GetStateValue();
    }

    /// <summary>
    /// 持久性国家追踪试验的 grain 状态。
    /// </summary>
    [GenerateSerializer]
    public class PersistentStateActivityGrainState
    {
        [Id(0)]
        public int Value { get; set; }
    }

    /// <summary>
    /// 具有持久性的追踪测试状态的试验 grain 实施。
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
    /// 移民追踪测试的测试 grain 接口。
    /// </summary>
    public interface IMigrationTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
    }

    /// <summary>
    /// 移民追踪试验 grain 状态。
    /// </summary>
    [GenerateSerializer]
    public class MigrationTracingTestGrainState
    {
        [Id(0)]
        public int Value { get; set; }
    }

    /// <summary>
    /// 移民追踪试验的测试 grain 实施。
    /// 实施 IGrainMigrationParticipant 参与移徙。
    /// 使用 RandomPlacement 允许向不同的 silos迁移。
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
    /// 使用放置过滤器测试移民追踪检测的 grain 接口。
    /// </summary>
    public interface IMigrationFilterTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
    }

    /// <summary>
    /// 使用安置过滤器测试移民追踪检测的 grain 实施情况。
    /// 组合 IGrainMigrationParticipant 和一个布置过滤器,以核实
    /// FilterPlacementCandidates 跨度在移徙期间根据 PlaceGrain 适当定位。
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
    /// 测试测试 grain 用于进行移徙追踪测试的界面接口 IGrainMigrationParticipant.
    /// </summary>
    public interface ISimpleMigrationTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
    }

    /// <summary>
    /// 测试 grain 是否执行不执行 IGrainMigrationParticipant的移民追踪试验。
    /// 使用 RandomPlacement 允许向不同的 silos迁移。
    /// 这 grain 在移民过程中将失去状态,因为它不参与脱水/体液补充。
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
    /// 与持续状态的测试 grain 接口,用于移民追踪检测。
    /// </summary>
    public interface IMigrationPersistentStateTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask SetState(int a, int b);
        ValueTask<(int A, int B)> GetState();
        ValueTask<GrainAddress> GetGrainAddress();
    }

    /// <summary>
    /// 测试测试 grain 执行和 IPersistentState 进行移徙追踪试验。
    /// 使用 RandomPlacement 允许向不同的 silos迁移。
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
    /// Grain 呼叫过滤器, 即 DisposeAsync 被调用后触发停用, 可以同数方式进行。
    /// 这样可以确保查点完成后停止使用。
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
