using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Orleans.Core.Internal;
using Orleans.Diagnostics;
using Orleans.Placement;
using Orleans.Storage;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 测试各种 Grain 停用场景中，是否正确创建
    /// OnDeactivateAsync 跟踪跨度。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [Collection("ActivationTracing")]
    public class DeactivationTracingTests : OrleansTestingBase, IClassFixture<ActivationTracingTests.Fixture>
    {
        private static readonly ConcurrentBag<Activity> Started = new();
        private static readonly ActivityListener Listener;

        static DeactivationTracingTests()
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

        private readonly ActivationTracingTests.Fixture _fixture;
        private readonly ITestOutputHelper _output;

        public DeactivationTracingTests(ActivationTracingTests.Fixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanIsCreatedOnDeactivateOnIdle()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-deactivate");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IDeactivationTracingTestGrain>(Random.Shared.Next());

                _ = await grain.GetActivityId();

                var testParentTraceId = parent!.TraceId.ToString();
                Started.Clear();

                await _fixture.HostedCluster.DeactivateAsync(grain);

                _ = await grain.GetActivityId();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span to be created during deactivation");

                var onDeactivateSpan = onDeactivateSpans.First();

                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.type").Value);
                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.silo.id").Value);
                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.activation.id").Value);

                var deactivationReasonTag = onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.deactivation.reason").Value;
                Assert.NotNull(deactivationReasonTag);
                Assert.Contains("ApplicationRequested", deactivationReasonTag);

                Assert.Equal(testParentTraceId, onDeactivateSpan.TraceId.ToString());
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanIncludesStorageWriteDuringDeactivation()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-deactivate-storage");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IDeactivationWithWorkTracingTestGrain>(Random.Shared.Next());

                _ = await grain.GetActivityId();
                Started.Clear();

                await _fixture.HostedCluster.DeactivateAsync(grain);

                var wasDeactivated = await grain.WasDeactivated();
                Assert.True(wasDeactivated, "Expected grain to have been deactivated");

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span to be created during deactivation");

                var storageWriteSpans = Started.Where(a => a.OperationName == ActivityNames.StorageWrite).ToList();
                Assert.True(storageWriteSpans.Count > 0, "Expected at least one storage write span to be created during OnDeactivateAsync");

                var storageWriteSpan = storageWriteSpans.First();
                Assert.Equal("MemoryGrainStorage", storageWriteSpan.Tags.FirstOrDefault(t => t.Key == "orleans.storage.provider").Value);
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanCapturesExceptionDuringDeactivation()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-deactivate-exception");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IDeactivationWithExceptionTracingTestGrain>(Random.Shared.Next());

                _ = await grain.GetActivityId();
                Started.Clear();

                await _fixture.HostedCluster.DeactivateAsync(grain);

                _ = await grain.GetActivityId();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span to be created during deactivation");

                var onDeactivateSpan = onDeactivateSpans.First();
                Assert.Equal(ActivityStatusCode.Error, onDeactivateSpan.Status);
                Assert.Equal("on-deactivate-failed", onDeactivateSpan.StatusDescription);
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanPrecedesDehydrateDuringMigration()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-deactivate-migrate");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IDeactivationMigrationTracingTestGrain>(Random.Shared.Next());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                Started.Clear();

                await _fixture.HostedCluster.MigrateAsync(grain, targetHost);

                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);

                var testParentTraceId = parent!.TraceId.ToString();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span during migration");

                var dehydrateSpans = Started.Where(a => a.OperationName == ActivityNames.ActivationDehydrate).ToList();
                Assert.True(dehydrateSpans.Count > 0, "Expected at least one dehydrate span during migration");

                var onDeactivateSpan = onDeactivateSpans.First();
                var dehydrateSpan = dehydrateSpans.First();

                Assert.True(onDeactivateSpan.StartTimeUtc <= dehydrateSpan.StartTimeUtc,
                    "OnDeactivateAsync should start before or at the same time as Dehydrate");

                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.Contains("Migrating", onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.deactivation.reason").Value);

                Assert.NotNull(dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.Equal(targetHost.ToString(), dehydrateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.migration.target.silo").Value);
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanIsNotCreatedForNonGrainBaseGrain()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-no-deactivate");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IActivityGrain>(Random.Shared.Next());

                _ = await grain.GetActivityId();
                Started.Clear();

                await grain.Cast<IGrainManagementExtension>().DeactivateOnIdle(TestContext.Current.CancellationToken);

                _ = await grain.GetActivityId();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();

                Assert.True(onDeactivateSpans.Count == 0,
                    $"Expected no OnDeactivate spans for grain not implementing IGrainBase, but found {onDeactivateSpans.Count}");
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanInheritsTraceContextFromTriggeringCall()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-trace-context");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IDeactivationTracingTestGrain>(Random.Shared.Next());

                _ = await grain.GetActivityId();

                var testParentTraceId = parent!.TraceId.ToString();

                await _fixture.HostedCluster.DeactivateAsync(grain);

                _ = await grain.GetActivityId();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span");

                var onDeactivateSpan = onDeactivateSpans.First();
                Assert.Equal(testParentTraceId, onDeactivateSpan.TraceId.ToString());
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanIsParentedToAsyncEnumerableMethodCall()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-async-enum-deactivate");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IAsyncEnumerableDeactivationGrain>(Random.Shared.Next());
                var testParentTraceId = parent!.TraceId.ToString();
                const int elementCount = 3;

                var deactivated = _fixture.HostedCluster.WaitForDeactivationAsync(grain);
                var values = new List<int>();
                await foreach (var value in grain.GetValuesAndDeactivate(elementCount).WithBatchSize(1))
                {
                    values.Add(value);
                }

                Assert.Equal(elementCount, values.Count);

                await deactivated;

                _ = await grain.GetActivityId();

                var sessionSpans = Started
                    .Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                               && a.OperationName.Contains("GetValuesAndDeactivate"))
                    .ToList();
                Assert.True(sessionSpans.Count >= 1, "Expected at least one session span with GetValuesAndDeactivate operation name");

                var sessionSpan = sessionSpans.First();
                Assert.Equal(testParentTraceId, sessionSpan.TraceId.ToString());

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span to be created during enumeration");

                var onDeactivateSpan = onDeactivateSpans.First();
                Assert.Equal(testParentTraceId, onDeactivateSpan.TraceId.ToString());
                Assert.Equal(sessionSpan.TraceId, onDeactivateSpan.TraceId);

                var deactivationReasonTag = onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.deactivation.reason").Value;
                Assert.NotNull(deactivationReasonTag);
                Assert.Contains("ApplicationRequested", deactivationReasonTag);
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanHasCorrectReasonTagForMigration()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-reason-migration");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IDeactivationMigrationTracingTestGrain>(Random.Shared.Next());
                var testParentTraceId = parent!.TraceId.ToString();
                await grain.SetState(42);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;

                var targetHost = _fixture.HostedCluster.GetActiveSilos()
                    .Select(s => s.SiloAddress)
                    .First(address => address != originalHost);

                Started.Clear();

                await _fixture.HostedCluster.MigrateAsync(grain, targetHost);

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span during migration");

                var onDeactivateSpan = onDeactivateSpans.First();

                var deactivationReasonTag = onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.deactivation.reason").Value;
                Assert.NotNull(deactivationReasonTag);
                Assert.Contains("Migrating", deactivationReasonTag);

                Assert.Equal(testParentTraceId, onDeactivateSpan.TraceId.ToString());
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanIsCreatedForInconsistentStateException()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-inconsistent-state");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IInconsistentStateDeactivationGrain>(Random.Shared.Next());

                _ = await grain.GetActivityId();

                var deactivated = _fixture.HostedCluster.WaitForDeactivationAsync(grain);
                try
                {
                    await grain.ThrowInconsistentStateException();
                }
                catch (InconsistentStateException)
                {
                    // 符合预期
                }

                await deactivated;

                _ = await grain.GetActivityId();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span to be created during deactivation");

                var onDeactivateSpan = onDeactivateSpans.First();

                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.type").Value);

                var deactivationReasonTag = onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.deactivation.reason").Value;
                Assert.NotNull(deactivationReasonTag);
                Assert.Contains("ApplicationError", deactivationReasonTag);

                Assert.NotEqual(default(ActivityTraceId).ToString(), onDeactivateSpan.TraceId.ToString());
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanIsNotCreatedForActivationFailure()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-activation-failure");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IActivationFailureDeactivationGrain>(Random.Shared.Next());

                Started.Clear();

                try
                {
                    await grain.GetActivityId();
                }
                catch
                {
                    // 符合预期：激活失败
                }

                // 短暂等待，让可能的停用操作完成
                await Task.Delay(500, TestContext.Current.CancellationToken);

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count == 0,
                    $"Expected no OnDeactivate spans for grain that failed during activation, but found {onDeactivateSpans.Count}");

                var activationSpans = Started.Where(a => a.OperationName == ActivityNames.ActivateGrain).ToList();
                Assert.True(activationSpans.Count > 0, "Expected at least one activation span");
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanIsCreatedForGrainContextDeactivate()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-grain-context-deactivate");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IGrainContextDeactivationGrain>(Random.Shared.Next());
                var testParentTraceId = parent!.TraceId.ToString();

                _ = await grain.GetActivityId();
                Started.Clear();

                await grain.DeactivateWithCustomReason("Custom deactivation reason for testing");
                await _fixture.HostedCluster.WaitForDeactivationAsync(grain);

                _ = await grain.GetActivityId();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span to be created during deactivation");

                var onDeactivateSpan = onDeactivateSpans.First();

                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.id").Value);
                Assert.NotNull(onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.grain.type").Value);

                var deactivationReasonTag = onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.deactivation.reason").Value;
                Assert.NotNull(deactivationReasonTag);
                Assert.Contains("ApplicationRequested", deactivationReasonTag);
                Assert.Contains("Custom deactivation reason for testing", deactivationReasonTag);

                Assert.Equal(testParentTraceId, onDeactivateSpan.TraceId.ToString());
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task OnDeactivateSpanHasCorrectParentWhenTriggeredExternally()
        {
            Started.Clear();

            using var parent = ActivitySources.ApplicationGrainSource.StartActivity("test-parent-external-deactivate");
            parent?.Start();
            try
            {
                var grain = _fixture.GrainFactory.GetGrain<IDeactivationTracingTestGrain>(Random.Shared.Next());
                var testParentTraceId = parent!.TraceId.ToString();

                _ = await grain.GetActivityId();
                Started.Clear();

                await _fixture.HostedCluster.DeactivateAsync(grain);

                _ = await grain.GetActivityId();

                var onDeactivateSpans = Started.Where(a => a.OperationName == ActivityNames.OnDeactivate).ToList();
                Assert.True(onDeactivateSpans.Count > 0, "Expected at least one OnDeactivate span to be created during deactivation");

                var onDeactivateSpan = onDeactivateSpans.First();

                Assert.Equal(testParentTraceId, onDeactivateSpan.TraceId.ToString());

                var deactivationReasonTag = onDeactivateSpan.Tags.FirstOrDefault(t => t.Key == "orleans.deactivation.reason").Value;
                Assert.NotNull(deactivationReasonTag);
                Assert.Contains("ApplicationRequested", deactivationReasonTag);
            }
            finally
            {
                parent?.Stop();
                PrintActivityDiagnostics();
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
            sb.AppendLine("=== CAPTURED ACTIVITIES ===");
            sb.AppendLine($"Total: {activities.Count}");
            sb.AppendLine();

            foreach (var activity in activities.OrderBy(a => a.StartTimeUtc))
            {
                sb.AppendLine($"[{activity.Source.Name}] {activity.OperationName}");
                sb.AppendLine($"  ID: {activity.Id}");
                sb.AppendLine($"  TraceId: {activity.TraceId}");
                sb.AppendLine($"  ParentId: {activity.ParentId}");
                sb.AppendLine($"  Duration: {activity.Duration.TotalMilliseconds:F2}ms");
                sb.AppendLine($"  Status: {activity.Status}");
                sb.AppendLine();
            }

            _output.WriteLine(sb.ToString());
        }
    }

    /// <summary>
    /// 用于基本停用跟踪测试的 Grain 接口。
    /// </summary>
    public interface IDeactivationTracingTestGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
        Task TriggerDeactivation();
    }

    /// <summary>
    /// 用于基本停用跟踪测试的 Grain 实现。
    /// 实现简单的 OnDeactivateAsync 以验证是否创建跟踪跨度。
    /// </summary>
    public class DeactivationTracingTestGrain : Grain, IDeactivationTracingTestGrain
    {
        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            return Task.FromResult<ActivityData?>(new ActivityData
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            });
        }

        public Task TriggerDeactivation()
        {
            this.DeactivateOnIdle();
            return Task.CompletedTask;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 用于测试在 OnDeactivateAsync 内执行操作时的停用跟踪的 Grain 接口。
    /// </summary>
    public interface IDeactivationWithWorkTracingTestGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
        Task TriggerDeactivation();
        Task<bool> WasDeactivated();
    }

    /// <summary>
    /// 用于跟踪停用的测试 Grain 状态。
    /// </summary>
    [GenerateSerializer]
    public class DeactivationWorkState
    {
        [Id(0)]
        public bool WasDeactivated { get; set; }

        [Id(1)]
        public string? DeactivationReason { get; set; }
    }

    /// <summary>
    /// 在 OnDeactivateAsync 期间执行操作的测试 Grain 实现。
    /// 使用持久化状态来跟踪停用是否发生。
    /// </summary>
    public class DeactivationWithWorkTracingTestGrain : Grain, IDeactivationWithWorkTracingTestGrain
    {
        private readonly IPersistentState<DeactivationWorkState> _state;

        public DeactivationWithWorkTracingTestGrain(
            [PersistentState("deactivationState")] IPersistentState<DeactivationWorkState> state)
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

            return Task.FromResult<ActivityData?>(new ActivityData
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            });
        }

        public Task TriggerDeactivation()
        {
            this.DeactivateOnIdle();
            return Task.CompletedTask;
        }

        public Task<bool> WasDeactivated() => Task.FromResult(_state.State!.WasDeactivated);

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _state.State!.WasDeactivated = true;
            _state.State.DeactivationReason = reason.ToString();
            await _state.WriteStateAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 用于测试 OnDeactivateAsync 抛出异常时的停用跟踪的 Grain 接口。
    /// </summary>
    public interface IDeactivationWithExceptionTracingTestGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
        Task TriggerDeactivation();
    }

    /// <summary>
    /// 在 OnDeactivateAsync 期间抛出异常的测试 Grain 实现。
    /// 用于验证 OnDeactivate 跟踪跨度是否正确记录错误。
    /// </summary>
    public class DeactivationWithExceptionTracingTestGrain : Grain, IDeactivationWithExceptionTracingTestGrain
    {
        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            return Task.FromResult<ActivityData?>(new ActivityData
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            });
        }

        public Task TriggerDeactivation()
        {
            this.DeactivateOnIdle();
            return Task.CompletedTask;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated error during deactivation");
        }
    }

    /// <summary>
    /// 用于测试具有迁移参与者时的停用跟踪的 Grain 接口。
    /// </summary>
    public interface IDeactivationMigrationTracingTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
        ValueTask TriggerDeactivation();
    }

    /// <summary>
    /// 实现 IGrainMigrationParticipant 以测试停用跟踪的 Grain 实现。
    /// 用于验证迁移期间在提取状态之前已创建 OnDeactivate 跟踪跨度。
    /// </summary>
    [RandomPlacement]
    public class DeactivationMigrationTracingTestGrain : Grain, IDeactivationMigrationTracingTestGrain, IGrainMigrationParticipant
    {
        private int _state;
        private bool _onDeactivateCalled;

        public ValueTask<int> GetState() => new(_state);

        public ValueTask SetState(int state)
        {
            _state = state;
            return default;
        }

        public ValueTask TriggerDeactivation()
        {
            this.DeactivateOnIdle();
            return default;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _onDeactivateCalled = true;
            return Task.CompletedTask;
        }

        public void OnDehydrate(IDehydrationContext migrationContext)
        {
            migrationContext.TryAddValue("state", _state);
            migrationContext.TryAddValue("onDeactivateCalled", _onDeactivateCalled);
        }

        public void OnRehydrate(IRehydrationContext migrationContext)
        {
            migrationContext.TryGetValue("state", out _state);
            migrationContext.TryGetValue("onDeactivateCalled", out _onDeactivateCalled);
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);
    }

    /// <summary>
    /// 用于 InconsistentStateException 停用跟踪测试的 Grain 接口。
    /// </summary>
    public interface IInconsistentStateDeactivationGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
        Task ThrowInconsistentStateException();
    }

    /// <summary>
    /// 抛出 InconsistentStateException 的测试 Grain 实现。
    /// 用于验证创建的 OnDeactivate 跟踪跨度具有 ApplicationError 原因。
    /// </summary>
    public class InconsistentStateDeactivationGrain : Grain, IInconsistentStateDeactivationGrain
    {
        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            return Task.FromResult<ActivityData?>(new ActivityData
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            });
        }

        public Task ThrowInconsistentStateException()
        {
            throw new InconsistentStateException("Simulated inconsistent state for testing deactivation tracing")
            {
                IsSourceActivation = true
            };
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 用于激活失败时的停用跟踪测试的 Grain 接口。
    /// </summary>
    public interface IActivationFailureDeactivationGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
    }

    /// <summary>
    /// 在激活期间失败的测试 Grain 实现。
    /// 用于验证 PreviousState != Valid 时不会创建 OnDeactivate 跟踪跨度。
    /// </summary>
    public class ActivationFailureDeactivationGrain : Grain, IActivationFailureDeactivationGrain
    {
        public ActivationFailureDeactivationGrain()
        {
            // 在构造函数中抛出异常，使激活失败
            throw new InvalidOperationException("Simulated activation failure for testing deactivation tracing");
        }

        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            return Task.FromResult<ActivityData?>(new ActivityData
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            });
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            // 由于激活失败，不应调用此方法
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 用于 GrainContext.Deactivate 停用跟踪测试的 Grain 接口。
    /// </summary>
    public interface IGrainContextDeactivationGrain : IGrainWithIntegerKey
    {
        Task<ActivityData?> GetActivityId();
        Task DeactivateWithCustomReason(string reason);
    }

    /// <summary>
    /// 使用带自定义原因的 GrainContext.Deactivate 的测试 Grain 实现。
    /// 用于验证创建的 OnDeactivate 跟踪跨度具有自定义原因。
    /// </summary>
    public class GrainContextDeactivationGrain : Grain, IGrainContextDeactivationGrain
    {
        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            return Task.FromResult<ActivityData?>(new ActivityData
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            });
        }

        public Task DeactivateWithCustomReason(string reason)
        {
            GrainContext.Deactivate(new DeactivationReason(DeactivationReasonCode.ApplicationRequested, reason));
            return Task.CompletedTask;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }



    /// <summary>
    /// 用于 IAsyncEnumerable 停用跟踪测试的 Grain 接口。
    /// </summary>
    public interface IAsyncEnumerableDeactivationGrain : IGrainWithIntegerKey
    {
        IAsyncEnumerable<int> GetValuesAndDeactivate(int count);
        Task<ActivityData?> GetActivityId();
    }


    /// <summary>
    /// 通过 IAsyncEnumerable 返回值并在 DisposeAsync 后停用的测试 Grain 实现。
    /// 使用 Grain 调用筛选器，在异步可枚举序列释放后触发停用。
    /// </summary>
    public class AsyncEnumerableDeactivationGrain : Grain, IAsyncEnumerableDeactivationGrain
    {
        public async IAsyncEnumerable<int> GetValuesAndDeactivate(int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Delay(10);
                yield return i;
            }
        }

        public Task<ActivityData?> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity is null)
            {
                return Task.FromResult(default(ActivityData));
            }

            return Task.FromResult<ActivityData?>(new ActivityData
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)).ToList(),
            });
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

}
