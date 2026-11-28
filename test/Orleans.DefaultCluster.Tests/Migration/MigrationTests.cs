using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.Core.Internal;
using Orleans.Placement;
using Orleans.Runtime;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace DefaultCluster.Tests.General
{
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class MigrationTests : HostedTestClusterEnsureDefaultStarted
    {
        public MigrationTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试直接实现 <see cref="IGrainMigrationParticipant"/> 的简单 Grain 能否正确迁移。
        /// 测试不指定 Grain 的迁移备选位置，而是反复尝试，直到 Grain 自行选择另一个位置。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task BasicGrainMigrationTest()
        {
            for (var i = 1; i < 100; ++i)
            {
                var grain = GrainFactory.GetGrain<IMigrationTestGrain>(GetRandomGrainId());
                var expectedState = Random.Shared.Next();
                var originalAddress = await grain.GetGrainAddress();
                GrainAddress newAddress;
                do
                {
                    // 不设置放置提示而触发迁移，因此 Grain 放置提供程序
                    // 可自由选择任意位置，包括当前位置。
                    await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);
                    await grain.SetState(expectedState);
                    newAddress = await grain.GetGrainAddress();
                } while (originalAddress == newAddress);

                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);
            }
        }

        /// <summary>
        /// 测试是否能从 Grain 的 <see cref="IGrainBase.OnDeactivateAsync(DeactivationReason, CancellationToken)"/> 实现内部发起迁移。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task InitiateMigrationFromOnDeactivateAsyncTest()
        {
            for (var i = 1; i < 100; ++i)
            {
                var grain = GrainFactory.GetGrain<IMigrationTestGrain>(GetRandomGrainId());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;
                var targetHost = Fixture.HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => address != originalHost);

                // 触发停用，并告知 Grain 在停用时应迁移到目标宿主。
                await grain.MigrateDuringDeactivation(targetHost);

                GrainAddress newAddress;
                do
                {
                    newAddress = await grain.GetGrainAddress();
                } while (newAddress.ActivationId == originalAddress.ActivationId);

                var newHost = newAddress.SiloAddress;
                Assert.Equal(targetHost, newHost);

                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);
            }
        }

        /// <summary>
        /// 测试直接实现 <see cref="IGrainMigrationParticipant"/> 的简单 Grain 能否正确迁移。
        /// 测试指定 Grain 迁移的备选位置，并断言它迁移到该位置。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DirectedGrainMigrationTest()
        {
            for (var i = 1; i < 100; ++i)
            {
                var grain = GrainFactory.GetGrain<IMigrationTestGrain>(GetRandomGrainId());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;
                var targetHost = Fixture.HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => address != originalHost);

                // 触发迁移，并设置放置提示以促使放置决策器使用目标 Silo。
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                GrainAddress newAddress;
                do
                {
                    newAddress = await grain.GetGrainAddress();
                } while (newAddress.ActivationId == originalAddress.ActivationId);

                var newHost = newAddress.SiloAddress;
                Assert.Equal(targetHost, newHost);

                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);
            }
        }

        /// <summary>
        /// 测试能否同时迁移多个 Grain。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MultiGrainDirectedMigrationTest()
        {
            var baseId = GetRandomGrainId();
            for (var i = 1; i < 100; ++i)
            {
                var a = GrainFactory.GetGrain<IMigrationTestGrain>(baseId + 2 * i);
                var expectedState = Random.Shared.Next();
                await a.SetState(expectedState);
                var originalAddressA = await a.GetGrainAddress();
                var originalHostA = originalAddressA.SiloAddress!;

                RequestContext.Set(IPlacementDirector.PlacementHintKey, originalHostA);
                var b = GrainFactory.GetGrain<IMigrationTestGrain>(baseId + 1 + 2 * i);
                await b.SetState(expectedState);
                var originalAddressB = await b.GetGrainAddress();
                Assert.Equal(originalHostA, originalAddressB.SiloAddress);

                var targetHost = Fixture.HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => address != originalHostA);

                // 触发迁移，并设置放置提示以促使放置决策器使用目标 Silo。
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                var migrateA = a.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);
                var migrateB = b.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);
                await migrateA;
                await migrateB;

                while (true)
                {
                    var newAddress = await a.GetGrainAddress();
                    if (newAddress.ActivationId != originalAddressA.ActivationId)
                    {
                        Assert.Equal(targetHost, newAddress.SiloAddress);
                        break;
                    }
                }

                while (true)
                {
                    var newAddress = await b.GetGrainAddress();
                    if (newAddress.ActivationId != originalAddressB.ActivationId)
                    {
                        Assert.Equal(targetHost, newAddress.SiloAddress);
                        break;
                    }
                }

                Assert.Equal(expectedState, await a.GetState());
                Assert.Equal(expectedState, await b.GetState());
            }
        }

        /// <summary>
        /// 测试使用 <see cref="Grain{TGrainState}"/> 管理状态的简单 Grain 能否正确迁移。
        /// 测试指定 Grain 迁移的备选位置，并断言它迁移到该位置。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DirectedGrainMigrationTest_GrainOfT()
        {
            for (var i = 1; i < 100; ++i)
            {
                var grain = GrainFactory.GetGrain<IMigrationTestGrain_GrainOfT>(GetRandomGrainId());
                var expectedState = Random.Shared.Next();
                await grain.SetState(expectedState);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;
                var targetHost = Fixture.HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => address != originalHost);

                // 触发迁移，并设置放置提示以促使放置决策器使用目标 Silo。
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                GrainAddress newAddress;
                do
                {
                    newAddress = await grain.GetGrainAddress();
                } while (newAddress.ActivationId == originalAddress.ActivationId);

                var newHost = newAddress.SiloAddress;
                Assert.Equal(targetHost, newHost);

                var newState = await grain.GetState();
                Assert.Equal(expectedState, newState);
            }
        }

        /// <summary>
        /// 测试使用 <see cref="IPersistentState{TState}"/> 管理状态的简单 Grain 能否正确迁移。
        /// 测试指定 Grain 迁移的备选位置，并断言它迁移到该位置。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task DirectedGrainMigrationTest_IPersistentStateOfT()
        {
            for (var i = 1; i < 100; ++i)
            {
                var grain = GrainFactory.GetGrain<IMigrationTestGrain_IPersistentStateOfT>(GetRandomGrainId());
                var expectedStateA = Random.Shared.Next();
                var expectedStateB = Random.Shared.Next();
                await grain.SetState(expectedStateA, expectedStateB);
                var originalAddress = await grain.GetGrainAddress();
                var originalHost = originalAddress.SiloAddress;
                var targetHost = Fixture.HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => address != originalHost);

                // 触发迁移，并设置放置提示以促使放置决策器使用目标 Silo。
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

                GrainAddress newAddress;
                do
                {
                    newAddress = await grain.GetGrainAddress();
                } while (newAddress.ActivationId == originalAddress.ActivationId);

                var newHost = newAddress.SiloAddress;
                Assert.Equal(targetHost, newHost);

                var (actualA, actualB) = await grain.GetState();
                Assert.Equal(expectedStateA, actualA);
                Assert.Equal(expectedStateB, actualB);
            }
        }

        /// <summary>
        /// Grain 状态提取失败时，应停用 Grain，但不会保留迁移状态。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task FailDehydrationTest()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            var grain = GrainFactory.GetGrain<IMigrationTestGrain>(GetRandomGrainId());
            var expectedState = Random.Shared.Next();
            await grain.SetState(expectedState);
            var originalAddress = await grain.GetGrainAddress();
            var targetHost = Fixture.HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => address != originalAddress.SiloAddress);

            // 等待成员资格稳定，并等待集群清单传播。
            // 通过反复尝试在目标 Silo 上激活 Grain 来实现这一点。
            var primaryGrainFactory = ((InProcessSiloHandle)Fixture.HostedCluster.Primary!).ServiceProvider.GetRequiredService<IGrainFactory>();
            SiloAddress? grainOnOtherSiloAddr;
            do
            {
                cts.Token.ThrowIfCancellationRequested();
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
                var grainOnOtherSilo = primaryGrainFactory.GetGrain<IMigrationTestGrain>(GetRandomGrainId());
                var addr = await grainOnOtherSilo.GetGrainAddress();
                grainOnOtherSiloAddr = addr.SiloAddress;
                await Task.Delay(100, cts.Token);
            } while (!targetHost.Equals(grainOnOtherSiloAddr));

            // 触发迁移，并设置放置提示以促使放置决策器使用目标 Silo。
            // 同时指示 Grain 使状态提取失败（在请求上下文中放入一些数据，使其引发异常）
            RequestContext.Set("fail_dehydrate", true);
            RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
            await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

            // Grain 应已在失败的迁移期间丢失其状态。
            var newState = await grain.GetState();
            Assert.NotEqual(expectedState, newState);

            var newAddress = await grain.GetGrainAddress();
            Assert.Equal(targetHost, newAddress.SiloAddress);
        }

        /// <summary>
        /// Grain 状态恢复失败时，应停用 Grain，但不会保留迁移状态。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task FailRehydrationTest()
        {
            var grain = GrainFactory.GetGrain<IMigrationTestGrain>(GetRandomGrainId());
            var expectedState = Random.Shared.Next();
            await grain.SetState(expectedState);
            var originalAddress = await grain.GetGrainAddress();
            var targetHost = Fixture.HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => address != originalAddress.SiloAddress);

            // 触发迁移，并设置放置提示以促使放置决策器使用目标 Silo。
            // 同时指示 Grain 使状态恢复失败（在恢复上下文中放入一些数据，使其引发异常）
            RequestContext.Set("fail_rehydrate", true);
            RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
            await grain.Cast<IGrainManagementExtension>().MigrateOnIdle(TestContext.Current.CancellationToken);

            var newAddress = await grain.GetGrainAddress();
            Assert.Equal(targetHost, newAddress.SiloAddress);

            // Grain 应已在失败的迁移期间丢失其状态。
            var newState = await grain.GetState();
            Assert.NotEqual(expectedState, newState);
        }
    }

    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class StuckDeactivationRecoveryTests : TestClusterPerTest
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.AddSiloBuilderConfigurator<Configurator>();
        }

        [Fact, TestCategory("BVT")]
        public async Task StuckDeactivatingActivationIsAbandoned()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = GrainFactory.GetGrain<IStuckDeactivationTestGrain>(GetRandomGrainId());
            var grainId = grain.GetGrainId();
            var originalAddress = await grain.GetGrainAddress();
            var targetHost = HostedCluster.GetActiveSilos().Select(s => s.SiloAddress).First(address => !address.Equals(originalAddress.SiloAddress));
            var blockingCall = grain.BlockUntilReleased();

            try
            {
                await grain.WaitUntilBlocked().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                await grain.StartMigrationTo(targetHost).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                var newAddress = await grain.GetGrainAddress().AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                Assert.NotEqual(originalAddress.ActivationId, newAddress.ActivationId);
            }
            finally
            {
                StuckDeactivationTestGrain.Release(grainId);
            }

            var completed = await Task.WhenAny(blockingCall, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(blockingCall, completed);
            await blockingCall;
        }

        private sealed class Configurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder
                    .Configure<SiloMessagingOptions>(options =>
                    {
                        options.MaxRequestProcessingTime = TimeSpan.FromMilliseconds(250);
                        options.ResponseTimeout = TimeSpan.FromSeconds(20);
                    })
                    .Configure<GrainCollectionOptions>(options => options.DeactivationTimeout = TimeSpan.FromMilliseconds(250));
            }
        }
    }

    public interface IStuckDeactivationTestGrain : IGrainWithIntegerKey
    {
        Task BlockUntilReleased();

        [AlwaysInterleave]
        Task WaitUntilBlocked();

        [AlwaysInterleave]
        ValueTask StartMigrationTo(SiloAddress targetHost);

        ValueTask<GrainAddress> GetGrainAddress();
    }

    [RandomPlacement]
    public class StuckDeactivationTestGrain : Grain, IStuckDeactivationTestGrain
    {
        private static readonly ConcurrentDictionary<GrainId, BlockState> BlockStates = new();

        public Task BlockUntilReleased()
        {
            var state = GetBlockState();
            state.Started.TrySetResult();
            return state.Released.Task;
        }

        public Task WaitUntilBlocked() => GetBlockState().Started.Task;

        public ValueTask StartMigrationTo(SiloAddress targetHost)
        {
            RequestContext.Set(IPlacementDirector.PlacementHintKey, targetHost);
            MigrateOnIdle();
            return default;
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);

        public static void Release(GrainId grainId)
        {
            if (BlockStates.TryRemove(grainId, out var state))
            {
                state.Released.TrySetResult();
            }
        }

        private BlockState GetBlockState() => BlockStates.GetOrAdd(GrainContext.GrainId, static _ => new());

        private sealed class BlockState
        {
            public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public interface IMigrationTestGrain : IGrainWithIntegerKey
    {
        ValueTask<GrainAddress> GetGrainAddress();
        ValueTask SetState(int state);
        ValueTask<int> GetState();
        ValueTask MigrateDuringDeactivation(SiloAddress targetHost);
    }

    // 采用随机放置，因为此处用于测试未指定目标的迁移。
    [RandomPlacement]
    public class MigrationTestGrain : Grain, IMigrationTestGrain, IGrainMigrationParticipant
    {
        private int _state;
        private SiloAddress? _migrateDuringDeactivationTargetHost;
        public ValueTask<int> GetState() => new(_state);

        public ValueTask SetState(int state)
        {
            _state = state;
            return default;
        }

        public void OnDehydrate(IDehydrationContext migrationContext)
        {
            if (RequestContext.Get("fail_rehydrate") is true)
            {
                migrationContext.TryAddValue("fail_rehydrate", true);
            }

            if (RequestContext.Get("fail_dehydrate") is true)
            {
                throw new InvalidOperationException("Failing to dehydrate on-command");
            }

            migrationContext.TryAddValue("state", _state);

        }

        public void OnRehydrate(IRehydrationContext migrationContext)
        {
            if (migrationContext.TryGetValue("fail_rehydrate", out bool fail) && fail)
            {
                throw new InvalidOperationException("Failing to rehydrate on-command");
            }

            migrationContext.TryGetValue("state", out _state);
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);

        public ValueTask MigrateDuringDeactivation(SiloAddress targetHost)
        {
            _migrateDuringDeactivationTargetHost = targetHost;
            this.DeactivateOnIdle();
            return ValueTask.CompletedTask;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            if (reason.ReasonCode is DeactivationReasonCode.ApplicationRequested && _migrateDuringDeactivationTargetHost is not null)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, _migrateDuringDeactivationTargetHost);
                this.MigrateOnIdle();
            }

            return base.OnDeactivateAsync(reason, cancellationToken);
        }
    }

    public interface IMigrationTestGrain_GrainOfT : IGrainWithIntegerKey
    {
        ValueTask SetState(int state);
        ValueTask<int> GetState();
        ValueTask<GrainAddress> GetGrainAddress();
    }

    public class MigrationTestGrainWithMemoryStorage : Grain<MyMigrationStateClass>, IMigrationTestGrain_GrainOfT
    {
        public ValueTask<int> GetState() => new(State.Value);

        public ValueTask SetState(int state)
        {
            State.Value = state;
            return default;
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);
    }

    [GenerateSerializer]
    public class MyMigrationStateClass
    {
        [Id(0)]
        public int Value { get; set; }
    }

    public interface IMigrationTestGrain_IPersistentStateOfT : IGrainWithIntegerKey
    {
        ValueTask SetState(int a, int b);
        ValueTask<(int A, int B)> GetState();
        ValueTask<GrainAddress> GetGrainAddress();
    }

    public class MigrationTestGrainWithInjectedMemoryStorage : Grain, IMigrationTestGrain_IPersistentStateOfT
    {
        private readonly IPersistentState<MyMigrationStateClass> _stateA;
        private readonly IPersistentState<MyMigrationStateClass> _stateB;

        public MigrationTestGrainWithInjectedMemoryStorage(
            [PersistentState("a")] IPersistentState<MyMigrationStateClass> stateA,
            [PersistentState("b")] IPersistentState<MyMigrationStateClass> stateB)
        {
            _stateA = stateA;
            _stateB = stateB;
        }

        public ValueTask<(int A, int B)> GetState() => new((_stateA.State.Value, _stateB.State.Value));

        public ValueTask SetState(int a, int b)
        {
            _stateA.State.Value = a;
            _stateB.State.Value = b;
            return default;
        }

        public ValueTask<GrainAddress> GetGrainAddress() => new(GrainContext.Address);
    }
}
