using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NonSilo.Tests.Utilities;
using NSubstitute;
using Orleans;
using Orleans.Configuration;
using Orleans.Core.Diagnostics;
using Orleans.Runtime;
using Orleans.Runtime.ConsistentRing;
using Orleans.Runtime.MembershipService;
using Orleans.TestingHost.Diagnostics;
using TestExtensions;
using Xunit;

namespace NonSilo.Tests.Membership
{
    /// <summary>
    /// 测试 <see cref="MembershipTableManager"/>。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Membership")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class MembershipTableManagerTests
    {
        private readonly ITestOutputHelper output;
        private readonly LoggerFactory loggerFactory;
        private readonly ILocalSiloDetails localSiloDetails;
        private readonly SiloAddress localSilo;
        private readonly IFatalErrorHandler fatalErrorHandler;
        private readonly IMembershipGossiper membershipGossiper;
        private readonly SiloLifecycleSubject lifecycle;
        private MembershipTableManager? membershipTableManager;

        public MembershipTableManagerTests(ITestOutputHelper output)
        {
            this.output = output;
            this.loggerFactory = new LoggerFactory(new[] { new XunitLoggerProvider(this.output) });

            this.localSiloDetails = Substitute.For<ILocalSiloDetails>();
            this.localSilo = Silo("127.0.0.1:100@100");
            this.localSiloDetails.SiloAddress.Returns(this.localSilo);
            this.localSiloDetails.DnsHostName.Returns("MyServer11");
            this.localSiloDetails.Name.Returns(Guid.NewGuid().ToString("N"));

            this.fatalErrorHandler = Substitute.For<IFatalErrorHandler>();
            this.fatalErrorHandler.IsUnexpected(default!).ReturnsForAnyArgs(true);
            this.membershipGossiper = Substitute.For<IMembershipGossiper>();
            this.lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
        }

        /// <summary>
        /// 测试新集群中 Silo 启动时 <see cref="MembershipTableManager"/> 的行为。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_NewCluster()
        {
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"));
            await this.BasicScenarioTest(
                membershipTable,
                gracefulShutdown: true,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试存在以下情况时 Silo 启动阶段 <see cref="MembershipTableManager"/> 的行为：已有
        /// 集群。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_ExistingCluster()
        {
            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:300@100"), SiloStatus.ShuttingDown, now),
                Entry(Silo("127.0.0.1:400@100"), SiloStatus.Joining, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            await this.BasicScenarioTest(
                membershipTable,
                gracefulShutdown: false,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        private async Task BasicScenarioTest(
            InMemoryMembershipTable membershipTable,
            bool gracefulShutdown,
            CancellationToken cancellationToken)
        {
            var timers = new List<DelegateAsyncTimer>();
            var timerCalls = new BlockingCollection<(TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion)>();

            var timerFactory = new DelegateAsyncTimerFactory(
                (period, name) =>
                {
                    var timer = new DelegateAsyncTimer(
                        overridePeriod =>
                        {
                            var task = new TaskCompletionSource<bool>();
                            timerCalls.Add((overridePeriod, task));
                            return task.Task;
                        });
                    timers.Add(timer);
                    return timer;
                });

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: timerFactory,
                this.lifecycle,
                timeProvider: TimeProvider.System);

            // 本地启动状态独立于尚未初始化的成员资格视图。
            var initialSnapshot = manager.MembershipTableSnapshot;
            Assert.Equal(MembershipVersion.MinValue, initialSnapshot.Version);
            Assert.Empty(initialSnapshot.Entries);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);

            Assert.NotNull(manager.MembershipTableUpdates);
            var changes = manager.MembershipTableUpdates;
            var currentEnumerator = changes.GetAsyncEnumerator(cancellationToken);
            Assert.True(currentEnumerator.MoveNextAsync().Result);
            Assert.Equal(currentEnumerator.Current.Version, manager.MembershipTableSnapshot.Version);
            Assert.Empty(membershipTable.Calls);

            // 所有这些检查都在任何生命周期方法有机会运行之前执行。
            // 这是为了验证在构造函数中访问成员资格的服务
            // 无论初始化顺序如何，均会看到正确结果。
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);

            await this.lifecycle.OnStart(cancellationToken);

            var calls = membershipTable.Calls;
            Assert.NotEmpty(calls);
            Assert.True(calls.Count >= 2);
            Assert.Equal(nameof(IMembershipTable.InitializeMembershipTableAsync), calls[0].Method);
            Assert.Equal(nameof(IMembershipTable.ReadAllAsync), calls[1].Method);

            // 初始化期间会首次读取该表，使
            // 成员资格转换至有效版本。currentEnumerator = changes.GetAsyncEnumerator();
            currentEnumerator = changes.GetAsyncEnumerator(cancellationToken);
            Assert.True(currentEnumerator.MoveNextAsync().Result);
            var update1 = currentEnumerator.Current;

            // 转换至 Joining 状态。
            this.membershipGossiper.ClearReceivedCalls();
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            await this.membershipGossiper.ReceivedWithAnyArgs().GossipToRemoteSilos(default!, default!, default!, default, default);
            Assert.Equal(SiloStatus.Joining, manager.CurrentStatus);
            var localSiloEntry = manager.MembershipTableSnapshot.Entries[this.localSilo];
            Assert.Equal(SiloStatus.Joining, localSiloEntry.Status);
            Assert.Equal(this.localSiloDetails.Name, localSiloEntry.SiloName);
            Assert.Equal(this.localSiloDetails.DnsHostName, localSiloEntry.HostName);

            // 应已发出更新。
            currentEnumerator = changes.GetAsyncEnumerator(cancellationToken);
            Assert.True(currentEnumerator.MoveNextAsync().Result);
            Assert.NotEqual(update1.Version, manager.MembershipTableSnapshot.Version);

            var update2 = currentEnumerator.Current;
            Assert.Equal(update2.Version, manager.MembershipTableSnapshot.Version);
            var entry = Assert.Single(update2.Entries, e => e.Key.Equals(this.localSilo));
            Assert.Equal(this.localSilo, entry.Key);
            Assert.Equal(this.localSilo, entry.Value.SiloAddress);
            Assert.Equal(SiloStatus.Joining, entry.Value.Status);

            calls = membershipTable.Calls.Skip(2).ToList();
            Assert.NotEmpty(calls);
            Assert.Contains(calls, call => call.Method.Equals(nameof(IMembershipTable.InsertRowAsync), StringComparison.Ordinal));
            Assert.Contains(calls, call => call.Method.Equals(nameof(IMembershipTable.ReadAllAsync), StringComparison.Ordinal));

            {
                // 检查是否请求了计时器，并且计时器到期后会调用
                // 成员资格表刷新操作。
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var (_, completion) = timerCalls.Take(cts.Token);
                membershipTable.ClearCalls();
                completion.TrySetResult(true);
                while (membershipTable.Calls.Count == 0) await Task.Delay(10, cancellationToken);
                Assert.Contains(membershipTable.Calls, c => c.Method.Equals(nameof(IMembershipTable.ReadAllAsync), StringComparison.Ordinal));
            }

            using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!gracefulShutdown) shutdownCts.Cancel();
            Assert.Equal(0, timers.First().DisposedCounter);
            var stopped = this.lifecycle.OnStop(shutdownCts.Token);

            // 使所有正在等待的计时器完成。
            while (timerCalls.TryTake(out var t))
            {
                t.Completion.TrySetResult(false);
            }

            await stopped;
            Assert.Equal(1, timers.First().DisposedCounter);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        /// <summary>
        /// 测试存在以下情况时 Silo 启动阶段 <see cref="MembershipTableManager"/> 的行为：已有
        /// 集群，并且此 Silo 已重启（存在一个具有较旧
        /// 世代的现有条目）。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_Restarted()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;

            // 表中包含仍标记为活跃的前任实例。
            // 节点快速重启时可能出现这种情况。
            var predecessor = Entry(Silo("127.0.0.1:100@1"), SiloStatus.Active, now);

            var otherSilos = new[]
            {
                predecessor,
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:300@100"), SiloStatus.ShuttingDown, now),
                Entry(Silo("127.0.0.1:400@100"), SiloStatus.Joining, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                this.lifecycle,
                timeProvider: TimeProvider.System);

            // 本地启动状态独立于尚未初始化的成员资格视图。
            var snapshot = manager.MembershipTableSnapshot;
            Assert.Equal(MembershipVersion.MinValue, snapshot.Version);
            Assert.Empty(snapshot.Entries);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);

            Assert.NotNull(manager.MembershipTableUpdates);
            var membershipUpdates = manager.MembershipTableUpdates.GetAsyncEnumerator(cancellationToken);
            Assert.True(await membershipUpdates.MoveNextAsync());
            var firstSnapshot = membershipUpdates.Current;
            Assert.Equal(firstSnapshot.Version, manager.MembershipTableSnapshot.Version);
            Assert.Empty(membershipTable.Calls);

            // 所有这些检查都在任何生命周期方法有机会运行之前执行。
            // 这是为了验证在构造函数中访问成员资格的服务
            // 无论初始化顺序如何，均会看到正确结果。
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);

            await this.lifecycle.OnStart(cancellationToken);

            var calls = membershipTable.Calls;
            Assert.NotEmpty(calls);
            Assert.True(calls.Count >= 2);
            Assert.Equal(nameof(IMembershipTable.InitializeMembershipTableAsync), calls[0].Method);
            Assert.Contains(calls, call => call.Method.Equals(nameof(IMembershipTable.ReadAllAsync), StringComparison.Ordinal));

            // 初始化期间读取该表，并在将表快照发布给其他组件之前
            // 宣告前任实例的条目死亡。
            Assert.True(await membershipUpdates.MoveNextAsync());
            var update1 = membershipUpdates.Current;

            // 转换至 Joining 状态。
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            snapshot = manager.MembershipTableSnapshot;
            Assert.Equal(SiloStatus.Joining, manager.CurrentStatus);
            Assert.Equal(SiloStatus.Joining, snapshot.Entries[localSilo].Status);

            Assert.True(await membershipUpdates.MoveNextAsync());
            Assert.Equal(membershipUpdates.Current.Version, manager.MembershipTableSnapshot.Version);

            // 前任实例应已在启动期间标记为死亡，
            // 且早于第一个快照的发布。
            Assert.Equal(SiloStatus.Dead, update1.GetSiloStatus(predecessor.SiloAddress));
            var latest = membershipUpdates.Current;
            Assert.Equal(SiloStatus.Dead, latest.GetSiloStatus(predecessor.SiloAddress));

            var entry = Assert.Single(latest.Entries, e => e.Key.Equals(this.localSilo));
            Assert.Equal(this.localSilo, entry.Key);
            Assert.Equal(this.localSilo, entry.Value.SiloAddress);
            Assert.Equal(SiloStatus.Joining, entry.Value.Status);

            calls = membershipTable.Calls.Skip(2).ToList();
            Assert.NotEmpty(calls);
            Assert.Contains(calls, call => call.Method.Equals(nameof(IMembershipTable.InsertRowAsync), StringComparison.Ordinal));
            Assert.Contains(calls, call => call.Method.Equals(nameof(IMembershipTable.ReadAllAsync), StringComparison.Ordinal));

            await this.lifecycle.OnStop(cancellationToken);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        /// <summary>
        /// 测试存在以下情况时 Silo 启动阶段 <see cref="MembershipTableManager"/> 的行为：已有
        /// 集群，并且此 Silo 已被更新一代的实例取代。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_Superseded()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;

            // 表中包含此 Silo 的后继实例。
            var successor = Entry(Silo("127.0.0.1:100@200"), SiloStatus.Active, now);

            var otherSilos = new[]
            {
                successor,
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:300@100"), SiloStatus.ShuttingDown, now),
                Entry(Silo("127.0.0.1:400@100"), SiloStatus.Joining, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                this.lifecycle,
                timeProvider: TimeProvider.System);

            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);

            await this.lifecycle.OnStart(cancellationToken);

            // Silo 应在加入阶段自行终止。
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);

            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);

            await this.lifecycle.OnStop(cancellationToken);
        }

        /// <summary>
        /// 测试存在以下情况时 Silo 启动阶段 <see cref="MembershipTableManager"/> 的行为：已有
        /// 集群，并且此 Silo 已被宣告死亡。
        /// 注意：此处测试的具体情况绝不应发生，Silo 在启动之前
        /// 不应为其他 Silo 所知。不过，管理器仍覆盖此情况。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_AlreadyDeclaredDead()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(this.localSilo, SiloStatus.Dead, now),
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:300@100"), SiloStatus.ShuttingDown, now),
                Entry(Silo("127.0.0.1:400@100"), SiloStatus.Joining, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                this.lifecycle,
                timeProvider: TimeProvider.System);

            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);

            await this.lifecycle.OnStart(cancellationToken);

            // Silo 应在加入阶段自行终止。
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);

            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.Cancel();
            await this.lifecycle.OnStop(cts.Token);
        }

        /// <summary>
        /// 测试以下情况中 <see cref="MembershipTableManager"/> 的行为：已有
        /// 集群，且此 Silo 在将其状态更新为 Joining 后的某个时刻被宣告死亡。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_DeclaredDead_AfterJoining()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now)
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                siloLifecycle: this.lifecycle,
                timeProvider: TimeProvider.System);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);

            // Silo 应在加入阶段自行终止。（译注：此测试先确认进入 Joining 时未终止，随后将其标记为 Dead 并刷新后才触发终止。）
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);

            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            // 将 Silo 标记为死亡。
            while (true)
            {
                var table = await membershipTable.ReadAllAsync(cancellationToken);
                var row = table.Members.Single(e => e.Item1.SiloAddress.Equals(this.localSilo));
                var entry = row.Item1.WithStatus(SiloStatus.Dead);
                if (await membershipTable.UpdateRowAsync(entry, row.Item2, table.Version.Next(), cancellationToken)) break;
            }

            // 刷新 Silo 状态，并检查其能否判定自身已死亡。
            await manager.Refresh(cancellationToken: cancellationToken);
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);

            await this.lifecycle.OnStop(cancellationToken);
        }

        [Fact]
        public async Task MembershipTableManager_DeclaredDead_DuringShutdown_DoesNotTerminate()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"));
            var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            await this.lifecycle.OnStop(cancellationToken);

            while (true)
            {
                var table = await membershipTable.ReadAllAsync(cancellationToken);
                var row = table.Members.Single(e => e.Item1.SiloAddress.Equals(this.localSilo));
                if (await membershipTable.UpdateRowAsync(row.Item1.WithStatus(SiloStatus.Dead), row.Item2, table.Version.Next(), cancellationToken))
                {
                    break;
                }
            }

            await manager.Refresh(cancellationToken: cancellationToken);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        [Fact]
        public async Task MembershipTableManager_ExplicitDeadSnapshot_Terminates()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"));
            var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);

            var version = manager.MembershipTableSnapshot.Version;
            await manager.RefreshFromSnapshot(Snapshot(
                new MembershipVersion(version.Value - 1),
                Entry(this.localSilo, SiloStatus.Dead, DateTimeOffset.UtcNow)), cancellationToken);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            await manager.RefreshFromSnapshot(Snapshot(
                new MembershipVersion(version.Value + 1),
                Entry(this.localSilo, SiloStatus.Dead, DateTimeOffset.UtcNow)), cancellationToken);
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);

            await manager.RefreshFromSnapshot(Snapshot(
                new MembershipVersion(version.Value + 2),
                Entry(this.localSilo, SiloStatus.Joining, DateTimeOffset.UtcNow)), cancellationToken);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);

            await this.lifecycle.OnStop(cancellationToken);
        }

        [Fact]
        public async Task MembershipTableManager_DeadTransitionOutsideShutdown_Terminates()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"));
            var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);

            await manager.UpdateStatus(SiloStatus.Dead, cancellationToken);

            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);
            await this.lifecycle.OnStop(cancellationToken);
        }

        [Fact]
        public async Task MembershipTableManager_DeadTransitionDuringLaterStopStage_DoesNotTerminate()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"));
            var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            this.lifecycle.Subscribe(
                "CustomLaterStage",
                ServiceLifecycleStage.Active + 1,
                _ => Task.CompletedTask,
                ct => manager.UpdateStatus(SiloStatus.Dead, ct));
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);

            await this.lifecycle.OnStop(cancellationToken);

            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        [Fact]
        public async Task MembershipTableManager_StopBeforeStart_DoesNotMarkLifecycleStopping()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"));
            var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);

            await this.lifecycle.OnStop(cancellationToken);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Dead, cancellationToken);

            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);
            await this.lifecycle.OnStop(cancellationToken);
        }

        [Fact]
        public async Task MembershipTableManager_MissingLocalEntry_OnlyNewerSnapshotAfterJoiningTerminates()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var otherSilo = Silo("127.0.0.1:200@100");
            var membershipTable = new InMemoryMembershipTable(
                new TableVersion(123, "123"),
                Entry(otherSilo, SiloStatus.Active, now));
            var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);

            await manager.RefreshFromSnapshot(Snapshot(new MembershipVersion(1)), cancellationToken);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            var currentVersion = manager.MembershipTableSnapshot.Version;
            var joinedSnapshot = manager.MembershipTableSnapshot;

            await manager.RefreshFromSnapshot(Snapshot(new MembershipVersion(currentVersion.Value - 1)), cancellationToken);
            await manager.RefreshFromSnapshot(Snapshot(
                currentVersion,
                Entry(otherSilo, SiloStatus.Active, now.AddMinutes(1))), cancellationToken);
            Assert.Same(joinedSnapshot, manager.MembershipTableSnapshot);
            Assert.Equal(SiloStatus.Joining, manager.MembershipTableSnapshot.Entries[this.localSilo].Status);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            await using var membershipUpdates = manager.MembershipTableUpdates.GetAsyncEnumerator(cancellationToken);
            Assert.True(await membershipUpdates.MoveNextAsync());

            await manager.RefreshFromSnapshot(Snapshot(
                new MembershipVersion(currentVersion.Value + 1),
                Entry(otherSilo, SiloStatus.Active, now.AddMinutes(1))), cancellationToken);
            Assert.True(await membershipUpdates.MoveNextAsync());
            Assert.DoesNotContain(this.localSilo, membershipUpdates.Current.Entries.Keys);
            Assert.DoesNotContain(this.localSilo, manager.MembershipTableSnapshot.Entries.Keys);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            var change = Assert.Single(
                membershipUpdates.Current.CreateClusterMembershipSnapshot()
                    .CreateUpdate(joinedSnapshot.CreateClusterMembershipSnapshot()).Changes);
            Assert.Equal(this.localSilo, change.SiloAddress);
            Assert.Equal(SiloStatus.Dead, change.Status);
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);

            await this.lifecycle.OnStop(cancellationToken);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MembershipTableManager_DelayedJoinCompletion_ReconcilesCanonicalView(bool pruneLocalEntry)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var table = new InMemoryMembershipTable();
            var membershipTable = new DelegatingMembershipTable(table);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completeWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            membershipTable.InsertRowOverride = async (entry, version, token) =>
            {
                Assert.True(await table.InsertRowAsync(entry, version, token));
                committed.SetResult();
                await completeWrite.Task.WaitAsync(token);
                return true;
            };

            var joining = manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);
            var committedTable = await table.ReadAllAsync(cancellationToken);
            var row = Assert.Single(committedTable.Members);
            if (pruneLocalEntry)
            {
                Assert.True(await table.UpdateRowAsync(
                    row.Item1.WithStatus(SiloStatus.Dead), row.Item2, committedTable.Version.Next(), cancellationToken));
                await table.CleanupDefunctSiloEntriesAsync(DateTimeOffset.MaxValue, cancellationToken);
            }
            else
            {
                await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            }

            await manager.Refresh(cancellationToken: cancellationToken);
            var latest = manager.MembershipTableSnapshot;
            Assert.Equal(new MembershipVersion(2), latest.Version);
            Assert.Equal(pruneLocalEntry ? 0 : 1, latest.Entries.Count);
            Assert.Equal(pruneLocalEntry ? SiloStatus.Created : SiloStatus.Active, manager.CurrentStatus);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            completeWrite.SetResult();
            await joining.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            var reconciled = manager.MembershipTableSnapshot;
            Assert.Equal(latest.Version, reconciled.Version);
            Assert.Equal(latest.Entries, reconciled.Entries);
            Assert.Empty(latest.RecentlyDeadSilos);
            Assert.Equal(pruneLocalEntry ? SiloStatus.Dead : SiloStatus.Active, manager.CurrentStatus);
            if (pruneLocalEntry)
            {
                Assert.Equal(this.localSilo, Assert.Single(reconciled.RecentlyDeadSilos));
                Assert.Equal(SiloStatus.Dead, reconciled.GetSiloStatus(this.localSilo));
                await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
                Assert.Empty((await table.ReadAllAsync(cancellationToken)).Members);
                Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
                this.fatalErrorHandler.Received(1).OnFatalException(manager, Arg.Any<string>(), null);
            }
            else
            {
                Assert.Empty(reconciled.RecentlyDeadSilos);
                Assert.Equal(SiloStatus.Active, Assert.Single(latest.Entries).Value.Status);
                this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            }
        }

        [Fact]
        public async Task MembershipTableManager_DelayedDeadCompletion_ReconcilesSameVersionPruning()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var table = new InMemoryMembershipTable();
            var membershipTable = new DelegatingMembershipTable(table);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completeWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            membershipTable.InsertRowOverride = async (entry, version, token) =>
            {
                Assert.True(await table.InsertRowAsync(entry, version, token));
                committed.SetResult();
                await completeWrite.Task.WaitAsync(token);
                return true;
            };

            var terminating = manager.UpdateStatus(SiloStatus.Dead, cancellationToken);
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await table.CleanupDefunctSiloEntriesAsync(DateTimeOffset.MaxValue, cancellationToken);
            await manager.Refresh(cancellationToken: cancellationToken);
            var pruned = manager.MembershipTableSnapshot;
            Assert.Equal(new MembershipVersion(1), pruned.Version);
            Assert.Empty(pruned.Entries);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            completeWrite.SetResult();
            await terminating.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            var reconciled = manager.MembershipTableSnapshot;
            Assert.Equal(pruned.Version, reconciled.Version);
            Assert.Empty(reconciled.Entries);
            Assert.Equal(this.localSilo, Assert.Single(reconciled.RecentlyDeadSilos));
            Assert.Empty(pruned.RecentlyDeadSilos);
            Assert.Equal(SiloStatus.Dead, reconciled.GetSiloStatus(this.localSilo));
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            this.fatalErrorHandler.Received(1).OnFatalException(manager, Arg.Any<string>(), null);
        }

        [Theory]
        [InlineData(SiloStatus.Joining)]
        [InlineData(SiloStatus.Active)]
        [InlineData(SiloStatus.Dead)]
        public async Task MembershipTableManager_OlderRegistrationEvidence_TerminatesWithoutChangingView(SiloStatus oldStatus)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var table = new InMemoryMembershipTable(new TableVersion(2, "2"));
            using var manager = this.CreateMembershipTableManager(table);
            await manager.Refresh(cancellationToken: cancellationToken);
            var current = manager.MembershipTableSnapshot;

            await manager.RefreshFromSnapshot(Snapshot(
                new MembershipVersion(1), Entry(this.localSilo, oldStatus, DateTimeOffset.UnixEpoch)), cancellationToken);

            var reconciled = manager.MembershipTableSnapshot;
            Assert.Equal(current.Version, reconciled.Version);
            Assert.Empty(reconciled.Entries);
            Assert.Equal(this.localSilo, Assert.Single(reconciled.RecentlyDeadSilos));
            Assert.Empty(current.RecentlyDeadSilos);
            Assert.Equal(SiloStatus.Dead, reconciled.GetSiloStatus(this.localSilo));
            Assert.Empty(current.Entries);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);

            var otherSilo = Silo("127.0.0.1:200@100");
            Assert.True(await table.InsertRowAsync(
                Entry(otherSilo, SiloStatus.Active, DateTimeOffset.UtcNow), table.Version.Next(), cancellationToken));
            await manager.Refresh(cancellationToken: cancellationToken);
            Assert.Equal(new MembershipVersion(3), manager.MembershipTableSnapshot.Version);
            Assert.Equal(otherSilo, Assert.Single(manager.MembershipTableSnapshot.Entries).Key);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            this.fatalErrorHandler.Received(1).OnFatalException(manager, Arg.Any<string>(), null);
        }

        [Fact]
        public async Task MembershipTableManager_PreJoinCleanup_GossipsCanonicalViewAndAllowsHeartbeatRefresh()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var predecessor = Silo("127.0.0.1:100@99");
            var peer = Silo("127.0.0.1:200@100");
            var table = new InMemoryMembershipTable(
                new TableVersion(1, "1"),
                Entry(predecessor, SiloStatus.Active, now),
                Entry(peer, SiloStatus.Active, now));
            using var manager = this.CreateMembershipTableManager(table);
            var gossiped = new TaskCompletionSource<MembershipTableSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.membershipGossiper.GossipToRemoteSilos(
                Arg.Any<List<SiloAddress>>(), Arg.Any<MembershipTableSnapshot>(), predecessor, SiloStatus.Dead, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Assert.Equal(peer, Assert.Single(call.ArgAt<List<SiloAddress>>(0)));
                    gossiped.SetResult(call.ArgAt<MembershipTableSnapshot>(1));
                    return Task.CompletedTask;
                });

            await manager.Refresh(cancellationToken: cancellationToken);
            var snapshot = await gossiped.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.Equal(new MembershipVersion(2), snapshot.Version);
            Assert.Equal(2, snapshot.Entries.Count);
            Assert.Equal(SiloStatus.Dead, snapshot.Entries[predecessor].Status);
            Assert.Equal(SiloStatus.Active, snapshot.Entries[peer].Status);
            Assert.DoesNotContain(this.localSilo, snapshot.Entries.Keys);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);

            var peerDetails = Substitute.For<ILocalSiloDetails>();
            peerDetails.SiloAddress.Returns(peer);
            using var peerManager = new MembershipTableManager(
                peerDetails,
                Options.Create(CreateMembershipOptions()),
                table,
                this.fatalErrorHandler,
                Substitute.For<IMembershipGossiper>(),
                this.loggerFactory.CreateLogger<MembershipTableManager>(),
                new AsyncTimerFactory(this.loggerFactory),
                new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>()),
                TimeProvider.System);
            await peerManager.RefreshFromSnapshot(snapshot, cancellationToken);
            Assert.Equal(SiloStatus.Active, peerManager.CurrentStatus);

            var heartbeat = now.AddMinutes(1);
            await table.UpdateIAmAliveAsync(Entry(peer, SiloStatus.Active, heartbeat), cancellationToken);
            await peerManager.Refresh(cancellationToken: cancellationToken);

            var refreshed = peerManager.MembershipTableSnapshot;
            Assert.Equal(snapshot.Version, refreshed.Version);
            Assert.Equal(2, refreshed.Entries.Count);
            Assert.Equal(SiloStatus.Dead, refreshed.Entries[predecessor].Status);
            Assert.Equal(SiloStatus.Active, refreshed.Entries[peer].Status);
            Assert.Equal(heartbeat.UtcDateTime, refreshed.Entries[peer].IAmAliveTime);
            Assert.DoesNotContain(this.localSilo, refreshed.Entries.Keys);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        /// <summary>
        /// 成员资格通知跟踪已提交的条目，而本地启动状态在注册之前保持为 Created。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_NewCluster_PublishesCanonicalViewsAndPreservesLocalRouting()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable();
            using var manager = this.CreateMembershipTableManager(membershipTable);
            using var clusterMembership = new ClusterMembershipService(
                manager,
                this.loggerFactory.CreateLogger<ClusterMembershipService>(),
                this.fatalErrorHandler);
            var listenerManager = new SiloStatusListenerManager(
                manager,
                this.loggerFactory.CreateLogger<SiloStatusListenerManager>(),
                this.fatalErrorHandler);
            var oracle = new SiloStatusOracle(
                this.localSiloDetails, manager, this.loggerFactory.CreateLogger<SiloStatusOracle>(), listenerManager);
            using var ring = new ConsistentRingProvider(this.localSilo, this.loggerFactory, oracle);
            using var services = new ServiceCollection()
                .AddMetrics()
                .AddSingleton<OrleansInstruments>()
                .AddSingleton<ConsistentRingInstruments>()
                .BuildServiceProvider();
            using var virtualRing = new VirtualBucketsRingProvider(
                this.localSilo, this.loggerFactory, 30, oracle, services.GetRequiredService<ConsistentRingInstruments>());
            var listener = new RecordingSiloStatusListener();
            Assert.True(listenerManager.Subscribe(listener));
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            ((ILifecycleParticipant<ISiloLifecycle>)listenerManager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);

            Assert.Equal(MembershipVersion.MinValue, manager.MembershipTableSnapshot.Version);
            Assert.Empty(manager.MembershipTableSnapshot.Entries);
            Assert.Empty(clusterMembership.CurrentSnapshot.Members);
            Assert.Equal(SiloStatus.Created, oracle.CurrentStatus);
            Assert.Empty(oracle.GetApproximateSiloStatuses());
            Assert.False(listener.Notifications.TryRead(out _));

            var otherSilo = Silo("127.0.0.1:200@100");
            Assert.True(await membershipTable.InsertRowAsync(
                Entry(otherSilo, SiloStatus.Joining, DateTimeOffset.UtcNow),
                membershipTable.Version.Next(),
                cancellationToken));
            await manager.Refresh(cancellationToken: cancellationToken);
            await listenerManager.TestOnlyWaitForCurrentMembershipVersion(cancellationToken);

            Assert.True(listener.Notifications.TryRead(out var notification));
            Assert.Equal((otherSilo, SiloStatus.Joining), notification);
            Assert.False(listener.Notifications.TryRead(out notification), $"Unexpected notification: {notification}");
            Assert.Equal(new MembershipVersion(1), manager.MembershipTableSnapshot.Version);
            Assert.Equal(otherSilo, Assert.Single(manager.MembershipTableSnapshot.Entries).Key);
            var initialClusterView = clusterMembership.CurrentSnapshot;
            Assert.Equal(otherSilo, Assert.Single(initialClusterView.Members).Key);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);

            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            await listenerManager.TestOnlyWaitForCurrentMembershipVersion(cancellationToken);
            Assert.True(listener.Notifications.TryRead(out notification));
            Assert.Equal((this.localSilo, SiloStatus.Joining), notification);
            Assert.True(listener.Notifications.TryRead(out notification));
            Assert.Equal((this.localSilo, SiloStatus.Active), notification);
            Assert.False(listener.Notifications.TryRead(out notification), $"Unexpected notification: {notification}");
            Assert.Equal(SiloStatus.Active, manager.CurrentStatus);
            var clusterView = clusterMembership.CurrentSnapshot;
            Assert.Equal(new MembershipVersion(3), clusterView.Version);
            Assert.Equal(2, clusterView.Members.Count);
            var change = Assert.Single(clusterView.CreateUpdate(initialClusterView).Changes);
            Assert.Equal(this.localSilo, change.SiloAddress);
            Assert.Equal(SiloStatus.Active, change.Status);
            Assert.Equal(this.localSilo, ring.GetPrimaryTargetSilo(unchecked((uint)this.localSilo.GetConsistentHashCode())));
            Assert.Equal(this.localSilo, virtualRing.GetPrimaryTargetSilo(this.localSilo.GetUniformHashCodes(30)[0]));

            var table = await membershipTable.ReadAllAsync(cancellationToken);
            var otherRow = table.Members.Single(row => row.Item1.SiloAddress.Equals(otherSilo));
            Assert.True(await membershipTable.UpdateRowAsync(
                otherRow.Item1.WithStatus(SiloStatus.Active), otherRow.Item2, table.Version.Next(), cancellationToken));
            await manager.Refresh(cancellationToken: cancellationToken);
            await listenerManager.TestOnlyWaitForCurrentMembershipVersion(cancellationToken);
            Assert.True(listener.Notifications.TryRead(out notification));
            Assert.Equal((otherSilo, SiloStatus.Active), notification);
            Assert.False(listener.Notifications.TryRead(out _));
            Assert.Equal(this.localSilo, virtualRing.GetPrimaryTargetSilo(this.localSilo.GetUniformHashCodes(30)[0]));
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            await this.lifecycle.OnStop(cancellationToken);
        }

        [Fact]
        public async Task MembershipTableManager_DeclaredDeadThenPrunedBeforeRefresh_Terminates()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"));
            var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);

            while (true)
            {
                var table = await membershipTable.ReadAllAsync(cancellationToken);
                var row = table.Members.Single(e => e.Item1.SiloAddress.Equals(this.localSilo));
                if (await membershipTable.UpdateRowAsync(row.Item1.WithStatus(SiloStatus.Dead), row.Item2, table.Version.Next(), cancellationToken))
                {
                    break;
                }
            }

            await membershipTable.CleanupDefunctSiloEntriesAsync(DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
            var prunedTable = await membershipTable.ReadAllAsync(cancellationToken);
            Assert.DoesNotContain(prunedTable.Members, row => row.Item1.SiloAddress.Equals(this.localSilo));

            await manager.Refresh(cancellationToken: cancellationToken);
            Assert.Empty(manager.MembershipTableSnapshot.Entries);
            Assert.Equal(prunedTable.Version.Version, manager.MembershipTableSnapshot.Version.Value);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);

            await this.lifecycle.OnStop(cancellationToken);
        }

        /// <summary>
        /// 尝试怀疑另一个 Silo 发生故障，却发现本 Silo 已发生故障。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_TrySuspectOrKill_ButIAmKill()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                siloLifecycle: this.lifecycle,
                timeProvider: TimeProvider.System);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            using var membershipEvents = new DiagnosticEventCollector(MembershipEvents.ListenerName);

            // 将 Silo 标记为死亡。
            while (true)
            {
                var table = await membershipTable.ReadAllAsync(cancellationToken);
                var row = table.Members.Single(e => e.Item1.SiloAddress.Equals(this.localSilo));
                var entry = row.Item1.WithStatus(SiloStatus.Dead);
                if (await membershipTable.UpdateRowAsync(entry, row.Item2, table.Version.Next(), cancellationToken)) break;
            }

            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            var victim = otherSilos.First().SiloAddress;
            var completion = WaitForSuspectOrKillCompletion(membershipEvents, victim, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            Assert.True((await completion).Success);
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);
        }

        /// <summary>
        /// 尝试怀疑另一个 Silo 发生故障，却发现它已死亡。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_TrySuspectOrKill_AlreadyDead()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                siloLifecycle: this.lifecycle,
                timeProvider: TimeProvider.System);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            using var membershipEvents = new DiagnosticEventCollector(MembershipEvents.ListenerName);

            var victim = otherSilos.Last().SiloAddress;
            var completion = WaitForSuspectOrKillCompletion(membershipEvents, victim, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            Assert.True((await completion).Success);
            Assert.Equal(SiloStatus.Dead, manager.MembershipTableSnapshot.GetSiloStatus(victim));
        }

        /// <summary>
        /// 在由 2 个 Silo 组成的小型集群中宣告一个 Silo 死亡，需要一票（(2 + 1) / 2 = 1）。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_TrySuspectOrKill_DeclareDead_SmallCluster()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                siloLifecycle: this.lifecycle,
                timeProvider: TimeProvider.System);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            using var membershipEvents = new DiagnosticEventCollector(MembershipEvents.ListenerName);

            var victim = otherSilos.First().SiloAddress;
            var completion = WaitForSuspectOrKillCompletion(membershipEvents, victim, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            Assert.True((await completion).Success);
            Assert.Equal(SiloStatus.Dead, manager.MembershipTableSnapshot.GetSiloStatus(victim));
        }

        /// <summary>
        /// 在较大的集群中宣告一个 Silo 死亡，按配置需要 2 票，但我们的时钟与集群中其他时钟相差数分钟。
        /// 目的是检查即使时钟严重不同步，整个集群中的逻辑也能保持一致。
        /// 这在投票计数方面尤为重要。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_TrySuspectOrKill_ClocksNotSynchronized()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTime.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:300@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:400@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:600@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:700@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:800@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:900@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var clusterMembershipOptions = CreateMembershipOptions();
            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(clusterMembershipOptions),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                siloLifecycle: this.lifecycle,
                timeProvider: TimeProvider.System);

            // 人为调整本地时钟。
            manager.GetDateTimeUtcNow = () => now;

            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            using var membershipEvents = new DiagnosticEventCollector(MembershipEvents.ListenerName);

            // 添加一些怀疑时间。这些时间之间的差值大于有效期窗口（DeathVoteExpirationTimeout），
            // 因此只有一票会被视为仍有效，尽管从本地 Silo 的角度看，
            // 两票都位于未来。
            var victim = otherSilos.First().SiloAddress;
            while (true)
            {
                var table = await membershipTable.ReadAllAsync(cancellationToken);
                var row = table.Members.Single(e => e.Item1.SiloAddress.Equals(victim));
                var entry = row.Item1.Copy();
                entry.SuspectTimes?.Clear();

                // 从本地 Silo 的角度看，位于未来两个有效期窗口的时刻。以此为基准来比较
                // 其他投票。
                // 若统计有效票数的逻辑存在缺陷，则此票加上下方的一票本应导致逐出，
                // 因而本地 Silo 将崩溃，表明存在缺陷。
                entry.AddSuspector(otherSilos[2].SiloAddress, now.Add(clusterMembershipOptions.DeathVoteExpirationTimeout.Multiply(2)));

                // 从本地 Silo 的角度看，位于过去半个有效期窗口的时刻，因此从
                // 本地 Silo 的角度仍在有效期窗口内。
                // 若统计有效票数的逻辑存在缺陷，则此票加上本地 Silo 的投票应足以逐出目标 Silo。
                entry.AddSuspector(otherSilos[4].SiloAddress, now.Subtract(clusterMembershipOptions.DeathVoteExpirationTimeout.Divide(2)));
                if (await membershipTable.UpdateRowAsync(entry, row.Item2, table.Version.Next(), cancellationToken)) break;
            }

            // 检查以下情况：
            //   a）添加本地投票不产生任何变化，因为本地时钟落后太多。
            //   b）Silo 不会被错误宣告死亡，因为两次投票之间的差值大于 DeathVoteExpirationTimeout。
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            var completion = WaitForSuspectOrKillCompletion(membershipEvents, victim, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            Assert.True((await completion).Success);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            // 目标 Silo 应仍存活，因为没有第二票落在最新投票的有效期窗口内。
            await manager.Refresh(cancellationToken: cancellationToken);
            Assert.Equal(SiloStatus.Active, manager.MembershipTableSnapshot.GetSiloStatus(victim));
        }

        /// <summary>
        /// 在较大的集群中宣告一个 Silo 死亡，按配置需要 2 票。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_TrySuspectOrKill_DeclareDead_LargerCluster()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:300@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:400@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:500@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:600@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:700@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:800@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.1:900@100"), SiloStatus.Dead, now),
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                siloLifecycle: this.lifecycle,
                timeProvider: TimeProvider.System);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            using var membershipEvents = new DiagnosticEventCollector(MembershipEvents.ListenerName);

            // 来自同一节点的多次投票不应导致目标节点被宣告死亡。
            var victim = otherSilos.First().SiloAddress;
            var completions = WaitForSuspectOrKillCompletions(
                membershipEvents,
                victim,
                expectedCount: 3,
                cancellationToken: cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            Assert.All(await completions, completion => Assert.True(completion.Success));
            Assert.Equal(SiloStatus.Active, manager.MembershipTableSnapshot.GetSiloStatus(victim));

            // 手动移除我们的投票并添加另一 Silo 的投票，以便由我们宣告目标 Silo 死亡。
            while (true)
            {
                var table = await membershipTable.ReadAllAsync(cancellationToken);
                var row = table.Members.Single(e => e.Item1.SiloAddress.Equals(victim));
                var entry = row.Item1.Copy();
                entry.SuspectTimes?.Clear();
                entry.AddSuspector(otherSilos[2].SiloAddress, DateTime.UtcNow);
                if (await membershipTable.UpdateRowAsync(entry, row.Item2, table.Version.Next(), cancellationToken)) break;
            }

            var completion = WaitForSuspectOrKillCompletion(membershipEvents, victim, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            Assert.True((await completion).Success);
            Assert.Equal(SiloStatus.Dead, manager.MembershipTableSnapshot.GetSiloStatus(victim));

            // 已终止一个，还剩一个。现在令投票超过所需票数，改为自行终止（由于内部错误）。
            victim = otherSilos[1].SiloAddress;
            while (true)
            {
                var table = await membershipTable.ReadAllAsync(cancellationToken);
                var row = table.Members.Single(e => e.Item1.SiloAddress.Equals(victim));
                var entry = row.Item1.Copy();
                entry.SuspectTimes?.Clear();
                entry.AddSuspector(otherSilos[2].SiloAddress, DateTime.UtcNow);
                entry.AddSuspector(otherSilos[3].SiloAddress, DateTime.UtcNow);
                entry.AddSuspector(otherSilos[4].SiloAddress, DateTime.UtcNow);
                if (await membershipTable.UpdateRowAsync(entry, row.Item2, table.Version.Next(), cancellationToken)) break;
            }

            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            completion = WaitForSuspectOrKillCompletion(membershipEvents, victim, cancellationToken);
            await manager.TryToSuspectOrKill(victim, null, cancellationToken);
            Assert.False((await completion).Success);
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);

            // 我们已自行终止，不应将另一个 Silo 标记为死亡。
            await manager.Refresh(cancellationToken: cancellationToken);
            Assert.Equal(SiloStatus.Active, manager.MembershipTableSnapshot.GetSiloStatus(victim));
        }

        /// <summary>
        /// 测试 <see cref="MembershipTableManager"/> 的表刷新行为。
        /// </summary>
        [Fact]
        public async Task MembershipTableManager_Refresh()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var timers = new List<DelegateAsyncTimer>();
            var timerCalls = new ConcurrentQueue<(TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion)>();
            var timerFactory = new DelegateAsyncTimerFactory(
                (period, name) =>
                {
                    var t = new DelegateAsyncTimer(
                        overridePeriod =>
                        {
                            var task = new TaskCompletionSource<bool>();
                            timerCalls.Enqueue((overridePeriod, task));
                            return task.Task;
                        });
                    timers.Add(t);
                    return t;
                });

            var now = DateTimeOffset.UtcNow;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, now)
            };
            var membershipTable = new InMemoryMembershipTable(new TableVersion(123, "123"), otherSilos);

            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: timerFactory,
                siloLifecycle: this.lifecycle,
                timeProvider: TimeProvider.System);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);

            // 测试发生异常后是否执行重试。
            (TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion) timer = (default, default!);
            while (!timerCalls.TryDequeue(out timer)) await Task.Delay(1, cancellationToken);
            var counter = 0;
            membershipTable.OnReadAll = () => { if (counter++ == 0) throw new Exception("no"); };
            timer.Completion.TrySetResult(true);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);

            // 暂时性故障后应提供较短的延迟。
            while (!timerCalls.TryDequeue(out timer)) await Task.Delay(10, cancellationToken);
            membershipTable.OnReadAll = null;
            Assert.True(timer.DelayOverride.HasValue);
            timer.Completion.TrySetResult(true);

            // 此后应使用标准延迟。
            while (!timerCalls.TryDequeue(out timer)) await Task.Delay(10, cancellationToken);
            Assert.False(timer.DelayOverride.HasValue);
            timer.Completion.TrySetResult(true);

            // 如果计时器本身（或其他操作）因某种原因发生故障，Silo 应崩溃。
            while (!timerCalls.TryDequeue(out timer)) await Task.Delay(10, cancellationToken);
            timer.Completion.TrySetException(new Exception("no again"));
            this.fatalErrorHandler.ReceivedWithAnyArgs().OnFatalException(default, default, default);
            Assert.False(timerCalls.TryDequeue(out timer));
            await this.lifecycle.OnStop(cancellationToken);
        }

        [Fact]
        public async Task Refresh_CallerCancellation_DoesNotCancelSharedRefresh()
        {
            var readCompletion = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(readCompletion.Task);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            using var cancellation = new CancellationTokenSource();
            var first = manager.Refresh(cancellationToken: cancellation.Token);
            var second = manager.Refresh(cancellationToken: TestContext.Current.CancellationToken);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.False(second.IsCompleted);
            Assert.False(readCompletion.Task.IsCompleted);
            Assert.Single(membershipTable.Inner.ReceivedCalls());

            readCompletion.SetResult(await new InMemoryMembershipTable(new TableVersion(1, "1")).ReadAllAsync(TestContext.Current.CancellationToken));
            await second;
            Assert.Equal(new MembershipVersion(1), manager.MembershipTableSnapshot.Version);
        }

        [Fact]
        public async Task Refresh_Disposal_CancelsSharedWaitWithoutApplyingLateRead()
        {
            var readCompletion = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(readCompletion.Task);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            var refresh = manager.Refresh(cancellationToken: TestContext.Current.CancellationToken);

            manager.Dispose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => refresh.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.False(readCompletion.Task.IsCompleted);
            readCompletion.SetException(new InvalidOperationException("Late membership table failure"));
            Assert.Equal(MembershipVersion.MinValue, manager.MembershipTableSnapshot.Version);
        }

        [Fact]
        public async Task Refresh_MaintenanceStop_PreservesSharedAndSubsequentRefreshes()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var initial = await new InMemoryMembershipTable(new TableVersion(1, "1")).ReadAllAsync(cancellationToken);
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(initial);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            var readCompletion = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            membershipTable.ConfigureReadAll(readCompletion.Task);
            var refresh = manager.Refresh(cancellationToken: cancellationToken);

            await this.lifecycle.OnStop(cancellationToken);
            Assert.False(refresh.IsCompleted);
            readCompletion.SetResult(await new InMemoryMembershipTable(new TableVersion(2, "2")).ReadAllAsync(cancellationToken));
            await refresh;
            Assert.Equal(new MembershipVersion(2), manager.MembershipTableSnapshot.Version);

            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(new TableVersion(3, "3")).ReadAllAsync(cancellationToken));
            await manager.Refresh(cancellationToken: cancellationToken);
            Assert.Equal(new MembershipVersion(3), manager.MembershipTableSnapshot.Version);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        [Fact]
        public async Task PeriodicRefresh_ProviderFaultDuringStop_RemainsRecoverable()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(new TableVersion(1, "1")).ReadAllAsync(cancellationToken));
            var tick = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var manager = this.CreateMembershipTableManager(
                membershipTable, timerFactory: new DelegateAsyncTimerFactory((_, _) => new DelegateAsyncTimer(_ => tick.Task)));
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            var stopping = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            membershipTable.ConfigureReadAll(() =>
            {
                stopping.SetResult(this.lifecycle.OnStop(cancellationToken));
                return Task.FromException<MembershipTableData>(new InvalidOperationException("Provider failure during shutdown"));
            });

            tick.SetResult(true);
            var stop = await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await stop.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            Assert.Equal(new MembershipVersion(1), manager.MembershipTableSnapshot.Version);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Refresh_MaintenanceStop_SettlesQueuedAndBackoffCleanupAcknowledgments(bool duringBackoff)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(new TableVersion(1, "1")).ReadAllAsync(cancellationToken));
            var clock = new BackoffTimeProvider();
            using var manager = this.CreateMembershipTableManager(
                membershipTable, clock, new DelegateAsyncTimerFactory((_, _) => new DelegateAsyncTimer(_ => Task.FromResult(false))));
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            var workerRead = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            membershipTable.ConfigureReadAll(() =>
            {
                workerStarted.TrySetResult();
                return workerRead.Task;
            });
            await manager.TryKill(Silo("127.0.0.1:200@100"), cancellationToken);
            await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            var older = Silo("127.0.0.1:100@99");
            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(
                new TableVersion(2, "2"), Entry(older, SiloStatus.Active, DateTimeOffset.UtcNow)).ReadAllAsync(cancellationToken));
            using var events = new DiagnosticEventCollector(MembershipEvents.ListenerName);
            var acknowledged = events.WaitForEventAsync(
                nameof(MembershipEvents.SuspectOrKillRequestCompleted),
                evt => evt.Payload is MembershipEvents.SuspectOrKillRequestCompleted completed
                    && completed.ObserverSiloAddress.Equals(this.localSilo) && completed.SiloAddress.Equals(older),
                TimeSpan.FromSeconds(10), cancellationToken);

            var refresh = manager.Refresh(cancellationToken: cancellationToken);
            Assert.False(refresh.IsCompleted);
            if (duringBackoff)
            {
                workerRead.SetException(new InvalidOperationException("Worker failure before acknowledged cleanup"));
                await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            await this.lifecycle.OnStop(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await refresh.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            var completed = Assert.IsType<MembershipEvents.SuspectOrKillRequestCompleted>((await acknowledged).Payload);
            Assert.False(completed.Success);
            Assert.IsAssignableFrom<OperationCanceledException>(completed.Exception);
            Assert.Equal(new MembershipVersion(2), manager.MembershipTableSnapshot.Version);
            if (!duringBackoff)
            {
                Assert.False(workerRead.Task.IsCompleted);
                workerRead.SetException(new InvalidOperationException("Late worker read failure"));
            }

            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(new TableVersion(3, "3")).ReadAllAsync(cancellationToken));
            await manager.Refresh(cancellationToken: cancellationToken);
            Assert.Equal(new MembershipVersion(3), manager.MembershipTableSnapshot.Version);
        }

        [Fact]
        public async Task Refresh_QueueOverflowSettlesCleanupAcknowledgment()
        {
            await QueueOverflowSettlesCleanupAcknowledgment(retryWrite: false);
        }

        [Fact]
        public async Task Refresh_RetryQueueOverflowSettlesCleanupAcknowledgment()
        {
            await QueueOverflowSettlesCleanupAcknowledgment(retryWrite: true);
        }

        private async Task QueueOverflowSettlesCleanupAcknowledgment(bool retryWrite)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(new TableVersion(1, "1")).ReadAllAsync(cancellationToken));
            var clock = new BackoffTimeProvider();
            using var manager = this.CreateMembershipTableManager(
                membershipTable, clock, new DelegateAsyncTimerFactory((_, _) => new DelegateAsyncTimer(_ => Task.FromResult(false))));
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            var workerRead = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            membershipTable.ConfigureReadAll(() =>
            {
                workerStarted.TrySetResult();
                return workerRead.Task;
            });
            await manager.TryKill(Silo("127.0.0.1:200@100"), cancellationToken);
            await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(
                new TableVersion(2, "2"), Entry(Silo("127.0.0.1:100@99"), SiloStatus.Active, DateTimeOffset.UtcNow)).ReadAllAsync(cancellationToken));

            var refresh = manager.Refresh(cancellationToken: cancellationToken);
            Assert.False(refresh.IsCompleted);
            var writes = retryWrite ? 99 : 100;
            for (var i = 0; i < writes; i++)
            {
                await manager.TryKill(Silo("127.0.0.1:300@100"), cancellationToken);
            }

            if (retryWrite)
            {
                Assert.False(refresh.IsCompleted);
                workerRead.SetException(new InvalidOperationException("Retry the request after filling its queue"));
                await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            await refresh.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.Equal(new MembershipVersion(2), manager.MembershipTableSnapshot.Version);
            await this.lifecycle.OnStop(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (!retryWrite)
            {
                Assert.False(workerRead.Task.IsCompleted);
                workerRead.SetException(new InvalidOperationException("Late worker read failure"));
            }
        }

        [Fact]
        public async Task UpdateLocalStatus_Cancellation_StopsPendingReadWithoutRetry()
        {
            var readCompletion = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(readCompletion.Task);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            using var cancellation = new CancellationTokenSource();
            var update = ((IMembershipManager)manager).UpdateLocalStatus(SiloStatus.Joining, cancellation.Token);

            cancellation.Cancel();
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => update.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Single(membershipTable.Inner.ReceivedCalls());
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);
            Assert.Empty(this.membershipGossiper.ReceivedCalls());
            readCompletion.SetException(new InvalidOperationException("Late status read failure"));
        }

        [Fact]
        public async Task UpdateLocalStatus_Cancellation_LeavesProviderWriteRunning()
        {
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(await new InMemoryMembershipTable(new TableVersion(1, "1")).ReadAllAsync(TestContext.Current.CancellationToken));
            var write = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            membershipTable.ConfigureInsertRow(write.Task);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            using var cancellation = new CancellationTokenSource();
            var update = ((IMembershipManager)manager).UpdateLocalStatus(SiloStatus.Joining, cancellation.Token);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => update.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.False(write.Task.IsCompleted);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);
            Assert.Empty(this.membershipGossiper.ReceivedCalls());
            Assert.Single(membershipTable.Inner.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IMembershipTable.InsertRow));
            write.SetException(new InvalidOperationException("Late status write failure"));
        }

        [Fact]
        public async Task UpdateIAmAlive_Cancellation_InterruptsProviderWait()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureUpdateIAmAlive(completion.Task);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            using var cancellation = new CancellationTokenSource();
            var update = ((IMembershipManager)manager).UpdateIAmAlive(cancellation.Token);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => update.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.False(completion.Task.IsCompleted);
            Assert.Single(membershipTable.Inner.ReceivedCalls());
            completion.SetResult();
        }

        [Fact]
        public async Task UpdateIAmAlive_ForwardsCallerTokenToNativeProvider()
        {
            var membershipTable = Substitute.For<IMembershipTable>();
            using var cancellation = new CancellationTokenSource();
            CancellationToken receivedToken = default;
            membershipTable.UpdateIAmAliveAsync(Arg.Any<MembershipEntry>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                receivedToken = call.ArgAt<CancellationToken>(1);
                return Task.CompletedTask;
            });
            using var manager = this.CreateMembershipTableManager(membershipTable);

            await manager.UpdateIAmAlive(cancellation.Token);

            Assert.Equal(cancellation.Token, receivedToken);
            var call = Assert.Single(membershipTable.ReceivedCalls());
            Assert.Equal(2, call.GetArguments().Length);
            Assert.Equal(this.localSilo, Assert.IsType<MembershipEntry>(call.GetArguments()[0]).SiloAddress);
        }

        [Fact]
        public async Task SuspectOrKill_PreCanceled_DoesNotQueueProviderWork()
        {
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            using var manager = this.CreateMembershipTableManager(membershipTable);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var membershipManager = (IMembershipManager)manager;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => membershipManager.TryKillSilo(this.localSilo, cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => membershipManager.TrySuspectSilo(this.localSilo, null, cancellation.Token));

            Assert.Empty(membershipTable.Inner.ReceivedCalls());
        }

        [Fact]
        public async Task ProcessGossipSnapshot_CancellationInterruptsPendingRefresh()
        {
            var readCompletion = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var membershipTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            membershipTable.ConfigureReadAll(readCompletion.Task);
            using var manager = this.CreateMembershipTableManager(membershipTable);
            var refresh = manager.Refresh(cancellationToken: TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var gossip = ((IMembershipManager)manager).ProcessGossipSnapshot(
                Snapshot(new MembershipVersion(2)), cancellation.Token);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => gossip.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.False(refresh.IsCompleted);
            Assert.Equal(MembershipVersion.MinValue, manager.MembershipTableSnapshot.Version);

            readCompletion.SetResult(await new InMemoryMembershipTable(new TableVersion(1, "1")).ReadAllAsync(TestContext.Current.CancellationToken));
            await refresh;
            Assert.Equal(new MembershipVersion(1), manager.MembershipTableSnapshot.Version);
        }

        [Fact]
        public async Task MembershipOwnerRechecksCancellationAfterSharedRefreshCompleted()
        {
            var read = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            var table = Substitute.For<IMembershipTable>();
            table.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(read.Task);
            using var manager = CreateMembershipTableManager(table);
            var refresh = manager.Refresh(cancellationToken: TestContext.Current.CancellationToken);
            var context = new MembershipContinuationContext();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var peer = Silo("127.0.0.1:200@100");
            var incoming = Snapshot(
                new MembershipVersion(2), Entry(peer, SiloStatus.Active, DateTimeOffset.UnixEpoch));
            Task gossip;
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                gossip = ((IMembershipManager)manager).ProcessGossipSnapshot(incoming, cancellation.Token);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            Assert.False(gossip.IsCompleted);
            read.SetResult(new MembershipTableData(new TableVersion(1, "1")));
            var continuation = await context.TakeContinuation(TestContext.Current.CancellationToken);
            await refresh.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var committed = manager.MembershipTableSnapshot;
            Assert.Equal(new MembershipVersion(1), committed.Version);

            cancellation.Cancel();
            continuation.Callback(continuation.State);

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => gossip.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Same(committed, manager.MembershipTableSnapshot);
            Assert.DoesNotContain(peer, manager.MembershipTableSnapshot.Entries.Keys);
        }

        [Theory]
        [InlineData(false, 3000)]
        [InlineData(true, 500)]
        public async Task UpdateStatus_Terminating_GossipDeadlinePreservesCommittedStatus(bool systemTargetProvider, int deadlineMilliseconds)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var clock = new FakeTimeProvider();
            IMembershipTable membershipTable = new InMemoryMembershipTable(
                new TableVersion(1, "1"), Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, DateTimeOffset.UtcNow));
            if (systemTargetProvider)
            {
                membershipTable = CreateSystemTargetBasedMembershipTable(membershipTable);
            }

            using var manager = this.CreateMembershipTableManager(membershipTable, clock);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await this.lifecycle.OnStop(cancellationToken);
            var gossipStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            MembershipTableSnapshot? gossipSnapshot = null;
            this.membershipGossiper.GossipToRemoteSilos(
                Arg.Any<List<SiloAddress>>(), Arg.Any<MembershipTableSnapshot>(), this.localSilo, SiloStatus.Dead, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var token = call.ArgAt<CancellationToken>(4);
                    gossipSnapshot = call.ArgAt<MembershipTableSnapshot>(1);
                    gossipStarted.SetResult(token);
                    return Task.Delay(Timeout.InfiniteTimeSpan, token);
                });

            var update = manager.UpdateStatus(SiloStatus.Dead, cancellationToken);
            var gossipToken = await gossipStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.NotNull(gossipSnapshot);
            Assert.Equal(SiloStatus.Dead, gossipSnapshot.Entries[this.localSilo].Status);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            Assert.True(gossipToken.CanBeCanceled);
            clock.Advance(TimeSpan.FromMilliseconds(deadlineMilliseconds - 1));
            Assert.False(gossipToken.IsCancellationRequested);
            Assert.False(update.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await update.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.True(gossipToken.IsCancellationRequested);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        [Fact]
        public async Task UpdateStatus_Terminating_CallerCancellationPreservesCommittedStatus()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            using var manager = this.CreateMembershipTableManager(new InMemoryMembershipTable(new TableVersion(1, "1")));
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await this.lifecycle.OnStop(cancellationToken);
            var gossipStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.membershipGossiper.GossipToRemoteSilos(
                Arg.Any<List<SiloAddress>>(), Arg.Any<MembershipTableSnapshot>(), this.localSilo, SiloStatus.Dead, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var token = call.ArgAt<CancellationToken>(4);
                    gossipStarted.SetResult(token);
                    return Task.Delay(Timeout.InfiniteTimeSpan, token);
                });
            using var cancellation = new CancellationTokenSource();
            var update = manager.UpdateStatus(SiloStatus.Dead, cancellation.Token);
            var gossipToken = await gossipStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => update.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(gossipToken.IsCancellationRequested);
            Assert.Equal(SiloStatus.Dead, manager.CurrentStatus);
        }

        [Fact]
        public async Task UpdateStatus_TerminatingSystemTarget_PreservesActualStateWhileWriteIsPending()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var clock = new FakeTimeProvider();
            var backingTable = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            backingTable.ConfigureReadAll(await new InMemoryMembershipTable(
                new TableVersion(1, "1"), Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, DateTimeOffset.UtcNow)).ReadAllAsync(cancellationToken));
            var write = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            backingTable.ConfigureInsertRow(write.Task);
            using var manager = this.CreateMembershipTableManager(CreateSystemTargetBasedMembershipTable(backingTable), clock);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);
            await this.lifecycle.OnStart(cancellationToken);
            await this.lifecycle.OnStop(cancellationToken);
            var gossipStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.membershipGossiper.GossipToRemoteSilos(
                Arg.Any<List<SiloAddress>>(), Arg.Any<MembershipTableSnapshot>(), this.localSilo, SiloStatus.Dead, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var token = call.ArgAt<CancellationToken>(4);
                    gossipStarted.SetResult(token);
                    return Task.Delay(Timeout.InfiniteTimeSpan, token);
                });
            using var cleanup = new CancellationTokenSource();
            var update = manager.UpdateStatus(SiloStatus.Dead, cleanup.Token);
            Assert.Single(backingTable.Inner.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IMembershipTable.InsertRow));
            Assert.False(gossipStarted.Task.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(500));
            var gossipToken = await gossipStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.False(gossipToken.IsCancellationRequested);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await update.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            Assert.True(gossipToken.IsCancellationRequested);
            Assert.False(write.Task.IsCompleted);
            Assert.Equal(SiloStatus.Created, manager.CurrentStatus);
            Assert.DoesNotContain(this.localSilo, manager.MembershipTableSnapshot.Entries.Keys);
            cleanup.Cancel();
            write.SetException(new InvalidOperationException("Late terminal status write failure"));
        }

        private SystemTargetBasedMembershipTable CreateSystemTargetBasedMembershipTable(IMembershipTable backingTable)
        {
            var primarySilo = Silo("127.0.0.1:200@100");
            var membershipTarget = Substitute.For<IMembershipTableSystemTarget>();
            membershipTarget.ReadAllAsync(Arg.Any<CancellationToken>())
                .Returns(call => backingTable.ReadAllAsync(call.ArgAt<CancellationToken>(0)));
            membershipTarget.InsertRowAsync(Arg.Any<MembershipEntry>(), Arg.Any<TableVersion>(), Arg.Any<CancellationToken>())
                .Returns(call => backingTable.InsertRowAsync(call.ArgAt<MembershipEntry>(0), call.ArgAt<TableVersion>(1), call.ArgAt<CancellationToken>(2)));
            membershipTarget.UpdateRowAsync(Arg.Any<MembershipEntry>(), Arg.Any<string>(), Arg.Any<TableVersion>(), Arg.Any<CancellationToken>())
                .Returns(call => backingTable.UpdateRowAsync(call.ArgAt<MembershipEntry>(0), call.ArgAt<string>(1), call.ArgAt<TableVersion>(2), call.ArgAt<CancellationToken>(3)));
            var grainFactory = Substitute.For<IInternalGrainFactory>();
            grainFactory.GetSystemTarget<IMembershipTableSystemTarget>(Constants.SystemMembershipTableType, Arg.Any<SiloAddress>())
                .Returns(membershipTarget);
            if (backingTable is IMembershipTableSystemTarget suppliedTarget)
            {
                // 已退役行测试检查实际接收方测试替身上的调用。
                grainFactory.GetSystemTarget<IMembershipTableSystemTarget>(Constants.SystemMembershipTableType, Arg.Any<SiloAddress>())
                    .Returns(suppliedTarget);
            }

            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IOptions<DevelopmentClusterMembershipOptions>)).Returns(
                Options.Create(new DevelopmentClusterMembershipOptions { PrimarySiloEndpoint = primarySilo.Endpoint }));
            services.GetService(typeof(ILocalSiloDetails)).Returns(this.localSiloDetails);
            services.GetService(typeof(IInternalGrainFactory)).Returns(grainFactory);
            services.GetService(typeof(IMembershipManager)).Returns(_ => this.membershipTableManager);
            return new SystemTargetBasedMembershipTable(
                services, this.loggerFactory.CreateLogger<SystemTargetBasedMembershipTable>(), this.fatalErrorHandler);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DevelopmentMembershipRowReads_AreUnsupportedWithoutRpc(bool initialized)
        {
            const string guidance = "Use ReadAllAsync and MembershipTableData.TryGet instead.";
            var target = Substitute.For<IMembershipTableSystemTarget>();
            var provider = CreateSystemTargetBasedMembershipTable(target);
            if (initialized)
            {
                await provider.InitializeMembershipTableAsync(true, TestContext.Current.CancellationToken);
            }

            target.ClearReceivedCalls();
            foreach (var key in new[] { this.localSilo, null! })
            {
#pragma warning disable CS0618 // 已退役的入口点必须直接以异常结束，不得检查键或转发 RPC。
                var legacy = provider.ReadRow(key);
                var current = provider.ReadRowAsync(key, TestContext.Current.CancellationToken);
#pragma warning restore CS0618
                Assert.True(legacy.IsFaulted);
                Assert.True(current.IsFaulted);
                Assert.Equal(guidance, (await Assert.ThrowsAsync<NotSupportedException>(() => legacy)).Message);
                Assert.Equal(guidance, (await Assert.ThrowsAsync<NotSupportedException>(() => current)).Message);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
#pragma warning disable CS0618 // 取消必须先于退役判断及任何提供程序访问。
                var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => provider.ReadRowAsync(key, cancellation.Token));
#pragma warning restore CS0618
                Assert.Equal(cancellation.Token, canceled.CancellationToken);
            }

            foreach (var name in new[] { nameof(IMembershipTable.ReadRow), nameof(IMembershipTable.ReadRowAsync) })
            {
                var obsolete = Assert.IsType<ObsoleteAttribute>(
                    Attribute.GetCustomAttribute(typeof(SystemTargetBasedMembershipTable).GetMethod(name)!, typeof(ObsoleteAttribute)));
                Assert.Equal(guidance, obsolete.Message);
                Assert.False(obsolete.IsError);
            }

            Assert.Empty(target.ReceivedCalls());
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(101)]
        public async Task DevelopmentMembershipReadChecksVersionLearnedFromGossip(int tableVersion)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var local = Entry(this.localSilo, SiloStatus.Active, DateTimeOffset.UnixEpoch);
            var backing = Substitute.For<IMembershipTable>();
            backing.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(
                new MembershipTableData(Tuple.Create(local, "local"), new TableVersion(100, "100")));
            var provider = CreateSystemTargetBasedMembershipTable(backing);
            await provider.InitializeMembershipTableAsync(true, cancellationToken);
            using var manager = CreateMembershipTableManager(provider);
            await manager.Refresh(cancellationToken: cancellationToken);
            backing.ClearReceivedCalls();
            var peer = Entry(Silo("127.0.0.1:200@100"), SiloStatus.Active, DateTimeOffset.UnixEpoch);
            var incoming = Snapshot(new MembershipVersion(101), local, peer);

            await manager.RefreshFromSnapshot(incoming, cancellationToken);

            var accepted = manager.MembershipTableSnapshot;
            Assert.Equal(new MembershipVersion(101), accepted.Version);
            Assert.Contains(peer.SiloAddress, accepted.Entries.Keys);
            Assert.Empty(backing.ReceivedCalls());
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            var version = new TableVersion(tableVersion, tableVersion.ToString());
            var entries = new List<Tuple<MembershipEntry, string>> { Tuple.Create(local, "local") };
            if (tableVersion == 101)
            {
                entries.Add(Tuple.Create(peer, "peer"));
            }

            backing.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(new MembershipTableData(entries, version));
            var read = provider.ReadAllAsync(cancellationToken);
            if (tableVersion < accepted.Version.Value)
            {
                var error = await Assert.ThrowsAsync<OrleansException>(() => read);
                Assert.Contains("version decreased from 101 to 100", error.Message, StringComparison.Ordinal);
                this.fatalErrorHandler.Received(1).OnFatalException(provider, error.Message, error);
            }
            else
            {
                var result = await read;
                Assert.Equal(tableVersion, result.Version.Version);
                this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            }

            Assert.Same(accepted, manager.MembershipTableSnapshot);
        }

        [Fact]
        public async Task DevelopmentMembershipReadChecksCommittedWriteVersion()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var backing = new InMemoryMembershipTable(new TableVersion(1, "1"));
            var provider = CreateSystemTargetBasedMembershipTable(backing);
            await provider.InitializeMembershipTableAsync(true, cancellationToken);
            using var manager = CreateMembershipTableManager(provider);
            await manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            var accepted = manager.MembershipTableSnapshot;
            Assert.Equal(new MembershipVersion(2), accepted.Version);
            Assert.Equal(SiloStatus.Joining, manager.CurrentStatus);
            backing.Version = new TableVersion(1, "reset");

            var read = provider.ReadAllAsync(cancellationToken);
            var error = await Assert.ThrowsAsync<OrleansException>(() => read);

            Assert.Contains("version decreased from 2 to 1", error.Message, StringComparison.Ordinal);
            this.fatalErrorHandler.Received(1).OnFatalException(provider, error.Message, error);
            Assert.Same(accepted, manager.MembershipTableSnapshot);
        }

        [Fact]
        public async Task DevelopmentMembershipReadObservesCancellationBeforeReportingRollback()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var backing = Substitute.For<IMembershipTable>();
            backing.ReadAllAsync(Arg.Any<CancellationToken>())
                .Returns(new MembershipTableData(new TableVersion(100, "100")));
            var provider = CreateSystemTargetBasedMembershipTable(backing);
            await provider.InitializeMembershipTableAsync(true, cancellationToken);
            using var manager = CreateMembershipTableManager(provider);
            await manager.Refresh(cancellationToken: cancellationToken);
            var accepted = manager.MembershipTableSnapshot;
            var read = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            backing.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(read.Task);
            using var cancellation = new CancellationTokenSource();
            var pending = provider.ReadAllAsync(cancellation.Token);

            cancellation.Cancel();
            read.SetResult(new MembershipTableData(new TableVersion(1, "1")));

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Same(accepted, manager.MembershipTableSnapshot);
            this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MembershipTableResetStopsOnlyTheVolatileProviderIncarnation(bool developmentProvider)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var original = await new InMemoryMembershipTable(new TableVersion(100, "old"),
                Entry(this.localSilo, SiloStatus.Active, DateTimeOffset.UtcNow)).ReadAllAsync(cancellationToken);
            var reset = await new InMemoryMembershipTable(new TableVersion(1, "new")).ReadAllAsync(cancellationToken);
            var backing = new LegacyMembershipTable(Substitute.For<IMembershipTable>());
            backing.ConfigureReadAll(original);
            IMembershipTable provider = developmentProvider ? CreateSystemTargetBasedMembershipTable(backing) : backing;
            await provider.InitializeMembershipTableAsync(true, cancellationToken);
            using var manager = CreateMembershipTableManager(provider);
            await manager.Refresh(cancellationToken: cancellationToken, requireFresh: true);
            var accepted = manager.MembershipTableSnapshot;
            Assert.Equal(new MembershipVersion(100), accepted.Version);
            backing.ConfigureReadAll(reset);

            if (developmentProvider)
            {
                var error = await Assert.ThrowsAsync<OrleansException>(
                    () => provider.ReadAllAsync(cancellationToken));
                Assert.Contains("version decreased from 100 to 1", error.Message, StringComparison.Ordinal);
                await Assert.ThrowsAsync<OrleansException>(
                    () => manager.Refresh(cancellationToken: cancellationToken, requireFresh: true));
                this.fatalErrorHandler.Received(1).OnFatalException(provider, error.Message, error);
            }
            else
            {
                await manager.Refresh(cancellationToken: cancellationToken, requireFresh: true);
                this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            }

            Assert.Same(accepted, manager.MembershipTableSnapshot);
        }

        [Fact]
        public async Task OlderDevelopmentReadCompletingAfterNewerReadIsNotATableReset()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var original = await new InMemoryMembershipTable(new TableVersion(100, "old"),
                Entry(this.localSilo, SiloStatus.Active, DateTimeOffset.UtcNow)).ReadAllAsync(cancellationToken);
            var newer = new MembershipTableData(original.Members.ToList(), new TableVersion(101, "new"));
            var backing = Substitute.For<IMembershipTable>();
            backing.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(original);
            var provider = CreateSystemTargetBasedMembershipTable(backing);
            await provider.InitializeMembershipTableAsync(true, cancellationToken);
            using var manager = CreateMembershipTableManager(provider);
            await manager.Refresh(cancellationToken: cancellationToken, requireFresh: true);
            var read = new TaskCompletionSource<MembershipTableData>(TaskCreationOptions.RunContinuationsAsynchronously);
            backing.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(read.Task);
            Task pending = manager.Refresh(cancellationToken: cancellationToken, requireFresh: true);
            try
            {
                Assert.False(pending.IsCompleted);
                backing.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(newer);
                await manager.Refresh(cancellationToken: cancellationToken, requireFresh: true);
                Assert.Equal(new MembershipVersion(101), manager.MembershipTableSnapshot.Version);
                read.SetResult(original);
                await pending.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

                Assert.Equal(new MembershipVersion(101), manager.MembershipTableSnapshot.Version);
                this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default, default, default);
            }
            finally
            {
                read.TrySetResult(original);
                await pending.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        private sealed class MembershipContinuationContext : SynchronizationContext
        {
            private readonly Channel<(SendOrPostCallback Callback, object? State)> _continuations =
                Channel.CreateUnbounded<(SendOrPostCallback, object?)>();

            public override void Post(SendOrPostCallback callback, object? state) =>
                _continuations.Writer.TryWrite((callback, state));

            public async Task<(SendOrPostCallback Callback, object? State)> TakeContinuation(CancellationToken cancellationToken)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                return await _continuations.Reader.ReadAsync(deadline.Token);
            }
        }

        private sealed class BackoffTimeProvider : FakeTimeProvider
        {
            public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = base.CreateTimer(callback, state, dueTime, period);
                TimerCreated.TrySetResult();
                return timer;
            }
        }

        [Fact]
        public async Task MembershipTableManager_RequireFreshStartsNewReadWhileRefreshInFlight()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable();
            var manager = CreateMembershipTableManager(membershipTable);
            var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseFirstRead = new ManualResetEventSlim();
            var readCount = 0;
            membershipTable.OnReadAll = () =>
            {
                if (Interlocked.Increment(ref readCount) == 1)
                {
                    firstReadStarted.TrySetResult();
                    if (!releaseFirstRead.Wait(TimeSpan.FromSeconds(30)))
                    {
                        throw new TimeoutException("Timed out waiting to release the first membership-table read");
                    }
                }
            };

            var inFlightRefresh = Task.Run(
                () => manager.Refresh(cancellationToken: cancellationToken),
                cancellationToken);
            await firstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            var causalRefresh = ((IMembershipManager)manager).Refresh(
                targetVersion: null,
                cancellationToken: CancellationToken.None,
                requireFresh: true);

            try
            {
                await causalRefresh.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                Assert.Equal(2, readCount);
            }
            finally
            {
                releaseFirstRead.Set();
                await inFlightRefresh;
            }
            Assert.Equal(2, readCount);
        }

        [Fact]
        public async Task MembershipTableManager_PreCancelledFreshRefreshDoesNotRead()
        {
            var membershipTable = new InMemoryMembershipTable();
            var manager = CreateMembershipTableManager(membershipTable);
            var readCount = 0;
            membershipTable.OnReadAll = () => Interlocked.Increment(ref readCount);
            var cancellation = new CancellationToken(canceled: true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                manager.Refresh(
                    targetVersion: null,
                    cancellationToken: cancellation,
                    requireFresh: true));

            Assert.Equal(0, readCount);
        }

        [Fact]
        public async Task MembershipTableManager_ShutdownFreshRefreshDoesNotRead()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var membershipTable = new InMemoryMembershipTable();
            var lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            using var manager = CreateMembershipTableManager(membershipTable, lifecycle: lifecycle);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(lifecycle);
            await lifecycle.OnStart(cancellationToken);
            await lifecycle.OnStop(cancellationToken);
            var readCount = 0;
            membershipTable.OnReadAll = () => Interlocked.Increment(ref readCount);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                manager.Refresh(
                    targetVersion: null,
                    cancellationToken: CancellationToken.None,
                    requireFresh: true));

            Assert.Equal(0, readCount);
        }

        [Fact]
        public async Task MembershipTableManager_FreshTargetRefreshStopsOnShutdownWithoutCancellingSharedRefresh()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var innerMembershipTable = new InMemoryMembershipTable();
            var membershipTable = new DelegatingMembershipTable(innerMembershipTable);
            var lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            using var manager = CreateMembershipTableManager(membershipTable, lifecycle: lifecycle);
            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(lifecycle);
            await lifecycle.OnStart(cancellationToken);

            var blockedRead = new TaskCompletionSource<MembershipTableData>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var blockedReadStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var blockedReadResult = await innerMembershipTable.ReadAllAsync(cancellationToken);
            var readCount = 0;
            membershipTable.ReadAllOverride = token =>
            {
                if (Interlocked.Increment(ref readCount) <= 2)
                {
                    return innerMembershipTable.ReadAllAsync(token);
                }

                blockedReadStarted.TrySetResult();
                return blockedRead.Task;
            };

            var freshRefresh = manager.Refresh(
                targetVersion: new MembershipVersion(long.MaxValue),
                cancellationToken: CancellationToken.None,
                requireFresh: true);
            await blockedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            var sharedRefresh = manager.Refresh(cancellationToken: cancellationToken);

            try
            {
                await lifecycle.OnStop(cancellationToken);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => freshRefresh);
                Assert.False(sharedRefresh.IsCompleted);
            }
            finally
            {
                blockedRead.TrySetResult(blockedReadResult);
            }

            await sharedRefresh.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }

        private static SiloAddress Silo(string value) => SiloAddress.FromParsableString(value);

        // 这些测试夹具使用人为设定的世代 100，而不是由时钟派生的标识。
        // 退役状态发布在 MembershipRetirementTests 中使用受控时钟单独测试。
        private static ClusterMembershipOptions CreateMembershipOptions() => new() { SiloGenerationRetirementAge = null };

        private MembershipTableManager CreateMembershipTableManager(
            IMembershipTable membershipTable,
            TimeProvider? timeProvider = null,
            IAsyncTimerFactory? timerFactory = null,
            SiloLifecycleSubject? lifecycle = null)
        {
            return this.membershipTableManager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(CreateMembershipOptions()),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: timerFactory ?? new AsyncTimerFactory(this.loggerFactory),
                siloLifecycle: lifecycle ?? this.lifecycle,
                timeProvider: timeProvider ?? TimeProvider.System);
        }

        private sealed class RecordingSiloStatusListener : ISiloStatusListener
        {
            private readonly Channel<(SiloAddress Silo, SiloStatus Status)> notifications =
                Channel.CreateUnbounded<(SiloAddress Silo, SiloStatus Status)>();

            public ChannelReader<(SiloAddress Silo, SiloStatus Status)> Notifications => this.notifications.Reader;

            public void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status) =>
                this.notifications.Writer.TryWrite((updatedSilo, status));
        }

        private sealed class DelegatingMembershipTable(IMembershipTable inner) : IMembershipTable
        {
            public Func<CancellationToken, Task<MembershipTableData>>? ReadAllOverride { get; set; }
            public Func<MembershipEntry, TableVersion, CancellationToken, Task<bool>>? InsertRowOverride { get; set; }

            public Task InitializeMembershipTable(bool tryInitTableVersion) =>
                InitializeMembershipTableAsync(tryInitTableVersion);

            public Task InitializeMembershipTableAsync(
                bool tryInitTableVersion,
                CancellationToken cancellationToken = default) =>
                inner.InitializeMembershipTableAsync(tryInitTableVersion, cancellationToken);

            public Task DeleteMembershipTableEntries(string clusterId) =>
                DeleteMembershipTableEntriesAsync(clusterId);

            public Task DeleteMembershipTableEntriesAsync(
                string clusterId,
                CancellationToken cancellationToken = default) =>
                inner.DeleteMembershipTableEntriesAsync(clusterId, cancellationToken);

            public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate) =>
                CleanupDefunctSiloEntriesAsync(beforeDate);

            public Task CleanupDefunctSiloEntriesAsync(
                DateTimeOffset beforeDate,
                CancellationToken cancellationToken = default) =>
                inner.CleanupDefunctSiloEntriesAsync(beforeDate, cancellationToken);

            [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
            public Task<MembershipTableData> ReadRow(SiloAddress key) => ReadRowAsync(key);

            [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
            public Task<MembershipTableData> ReadRowAsync(
                SiloAddress key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromException<MembershipTableData>(new NotSupportedException("Use ReadAllAsync and MembershipTableData.TryGet instead."));
            }

            public Task<MembershipTableData> ReadAll() => ReadAllAsync();

            public async Task<MembershipTableData> ReadAllAsync(
                CancellationToken cancellationToken = default)
            {
                var operation = ReadAllOverride?.Invoke(cancellationToken)
                    ?? inner.ReadAllAsync(cancellationToken);
                return await operation.WaitAsync(cancellationToken);
            }

            public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) =>
                InsertRowAsync(entry, tableVersion);

            public Task<bool> InsertRowAsync(
                MembershipEntry entry,
                TableVersion tableVersion,
                CancellationToken cancellationToken = default) =>
                InsertRowOverride?.Invoke(entry, tableVersion, cancellationToken)
                    ?? inner.InsertRowAsync(entry, tableVersion, cancellationToken);

            public Task<bool> UpdateRow(
                MembershipEntry entry,
                string etag,
                TableVersion tableVersion) =>
                UpdateRowAsync(entry, etag, tableVersion);

            public Task<bool> UpdateRowAsync(
                MembershipEntry entry,
                string etag,
                TableVersion tableVersion,
                CancellationToken cancellationToken = default) =>
                inner.UpdateRowAsync(entry, etag, tableVersion, cancellationToken);

            public Task UpdateIAmAlive(MembershipEntry entry) =>
                UpdateIAmAliveAsync(entry);

            public Task UpdateIAmAliveAsync(
                MembershipEntry entry,
                CancellationToken cancellationToken = default) =>
                inner.UpdateIAmAliveAsync(entry, cancellationToken);
        }

        private static MembershipTableSnapshot Snapshot(MembershipVersion version, params MembershipEntry[] entries)
        {
            return new MembershipTableSnapshot(version, entries.ToImmutableDictionary(entry => entry.SiloAddress));
        }

        private async Task<MembershipEvents.SuspectOrKillRequestCompleted> WaitForSuspectOrKillCompletion(
            DiagnosticEventCollector membershipEvents,
            SiloAddress silo,
            CancellationToken cancellationToken)
        {
            var completions = await WaitForSuspectOrKillCompletions(
                membershipEvents,
                silo,
                expectedCount: 1,
                cancellationToken: cancellationToken);
            return completions[0];
        }

        private async Task<List<MembershipEvents.SuspectOrKillRequestCompleted>> WaitForSuspectOrKillCompletions(
            DiagnosticEventCollector membershipEvents,
            SiloAddress silo,
            int expectedCount,
            CancellationToken cancellationToken)
        {
            Assert.True(expectedCount > 0);

            membershipEvents.Clear();
            var completions = new List<MembershipEvents.SuspectOrKillRequestCompleted>(expectedCount);

            while (completions.Count < expectedCount)
            {
                var diagnosticEvent = await membershipEvents.WaitForEventAsync(
                    nameof(MembershipEvents.SuspectOrKillRequestCompleted),
                    evt => evt.Payload is MembershipEvents.SuspectOrKillRequestCompleted completed
                        && completed.ObserverSiloAddress.Equals(this.localSilo)
                        && completed.RequestType == MembershipEvents.SuspectOrKillRequestType.SuspectOrKill
                        && completed.SiloAddress.Equals(silo)
                        && !completions.Contains(completed),
                    TimeSpan.FromSeconds(40),
                    cancellationToken);

                completions.Add(Assert.IsType<MembershipEvents.SuspectOrKillRequestCompleted>(diagnosticEvent.Payload));
            }

            return completions;
        }

        private static MembershipEntry Entry(SiloAddress address, SiloStatus status, DateTimeOffset iAmAliveTime)
        {
            return new MembershipEntry { SiloAddress = address, Status = status, IAmAliveTime = iAmAliveTime.UtcDateTime, StartTime = iAmAliveTime.UtcDateTime };
        }

    }
}
