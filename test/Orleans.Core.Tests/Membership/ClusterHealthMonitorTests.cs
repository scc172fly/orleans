using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NonSilo.Tests.Utilities;
using NSubstitute;
using Orleans.Connections;
using Orleans.Connections.Transport;
using Orleans.Configuration;
using Orleans.Core.Diagnostics;
using Orleans.Internal;
using Orleans.Messaging;
using Orleans.Placement.Repartitioning;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;
using Orleans.Runtime.Messaging;
using Orleans.TestingHost.Diagnostics;
using TestExtensions;
using Xunit;

namespace NonSilo.Tests.Membership
{
    /// <summary>
    /// Orleans集聚健康监测系统测试,该系统负责检测失败 silos
    /// 卫生监测员定期调查其他疾病和其他疾病 silos
    /// 并使用投票机制宣布 silos 为死亡,防止分布式系统中出现分裂型人才情况。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Membership")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ClusterHealthMonitorTests
    {
        private readonly ITestOutputHelper output;
        private readonly LoggerFactory loggerFactory;
        private readonly ILocalSiloDetails localSiloDetails;
        private readonly SiloAddress localSilo;
        private readonly IFatalErrorHandler fatalErrorHandler;
        private readonly IMembershipGossiper membershipGossiper;
        private readonly SiloLifecycleSubject lifecycle;
        private readonly List<DelegateAsyncTimer> timers;
        private readonly ConcurrentQueue<(TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion)> timerCalls;
        private readonly DelegateAsyncTimerFactory timerFactory;
        private readonly ILocalSiloHealthMonitor localSiloHealthMonitor;
        private readonly InMemoryMembershipTable membershipTable;
        private readonly IRemoteSiloProber prober;
        private readonly ConnectionManager connectionManager;

        public ClusterHealthMonitorTests(ITestOutputHelper output)
        {
            this.output = output;
            this.loggerFactory = new LoggerFactory(new[] { new XunitLoggerProvider(this.output) });

            this.localSiloDetails = Substitute.For<ILocalSiloDetails>();
            this.localSilo = Silo("127.0.0.1:100@100");
            this.localSiloDetails.SiloAddress.Returns(this.localSilo);
            this.localSiloDetails.DnsHostName.Returns("MyServer11");
            this.localSiloDetails.Name.Returns(Guid.NewGuid().ToString("N"));

            this.fatalErrorHandler = Substitute.For<IFatalErrorHandler>();
            this.membershipGossiper = Substitute.For<IMembershipGossiper>();
            this.lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            this.timers = new List<DelegateAsyncTimer>();
            this.timerCalls = new ConcurrentQueue<(TimeSpan? DelayOverride, TaskCompletionSource<bool> Completion)>();
            this.timerFactory = new DelegateAsyncTimerFactory(
                (period, name) =>
                {
                    var t = new DelegateAsyncTimer(
                        overridePeriod =>
                        {
                            var task = new TaskCompletionSource<bool>();
                            this.timerCalls.Enqueue((overridePeriod, task));
                            return task.Task;
                        });
                    this.timers.Add(t);
                    return t;
                });

            this.localSiloHealthMonitor = Substitute.For<ILocalSiloHealthMonitor>();
            this.localSiloHealthMonitor.GetLocalHealthStatus(default, default, default).ReturnsForAnyArgs(new LocalSiloHealthStatus(0, []));

            this.prober = Substitute.For<IRemoteSiloProber>();
            this.membershipTable = new InMemoryMembershipTable(new TableVersion(1, "1"));
            this.connectionManager = new ConnectionManager(
                Options.Create(new ConnectionOptions()),
                null!,
                this.loggerFactory.CreateLogger<ConnectionManager>());
        }

        /// <summary>
        /// <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本运行测试。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_BasicScenario()
        {
            await ClusterHealthMonitor_BasicScenario_Runner(
                enableIndirectProbes: true,
                numVotesForDeathDeclaration: 2,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试如果 silos 是老化的,则所有其他 silos都加以监测。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_MonitorAllStaleSilos()
        {
            await ClusterHealthMonitor_BasicScenario_Runner(
                enableIndirectProbes: true,
                numVotesForDeathDeclaration: 2,
                otherSilosAreStale: true,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ClusterHealthMonitor_JoiningSiloMonitorsStaleAndSuspectedEvictableSilos()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var clusterMembershipOptions = new ClusterMembershipOptions
            {
                NumProbedSilos = 3,
            };

            var testRig = CreateClusterHealthMonitorTestRig(clusterMembershipOptions);
            await this.lifecycle.OnStart(cancellationToken);

            var staleSilo = Silo("127.0.0.200:100@100");
            var freshSilo = Silo("127.0.0.200:200@100");
            var suspectedSilo = Silo("127.0.0.200:300@100");
            var shuttingDownSilo = Silo("127.0.0.200:500@100");
            var suspectedEntry = Entry(suspectedSilo, SiloStatus.Active, now);
            suspectedEntry.AddSuspector(Silo("127.0.0.200:400@100"), now.UtcDateTime);

            var otherSilos = new[]
            {
                Entry(staleSilo, SiloStatus.Active, now.Subtract(TimeSpan.FromHours(1))),
                Entry(freshSilo, SiloStatus.Active, now),
                suspectedEntry,
                Entry(shuttingDownSilo, SiloStatus.ShuttingDown, now.Subtract(TimeSpan.FromHours(1)))
            };

            var lastVersion = testRig.TestAccessor.ObservedVersion;
            foreach (var entry in otherSilos)
            {
                var table = await this.membershipTable.ReadAllAsync(cancellationToken);
                Assert.True(await this.membershipTable.InsertRowAsync(entry, table.Version.Next(), cancellationToken));
            }

            await testRig.Manager.Refresh(cancellationToken: cancellationToken);
            await Until(() => testRig.TestAccessor.ObservedVersion > lastVersion, cancellationToken);
            Assert.Empty(testRig.TestAccessor.MonitoredSilos);

            lastVersion = testRig.TestAccessor.ObservedVersion;
            await testRig.Manager.UpdateStatus(SiloStatus.Joining, cancellationToken);
            await Until(() => testRig.TestAccessor.ObservedVersion > lastVersion, cancellationToken);
            await Until(() => testRig.TestAccessor.MonitoredSilos.Count == 3, cancellationToken);

            Assert.Contains(testRig.TestAccessor.MonitoredSilos, pair => pair.Key.Equals(staleSilo));
            Assert.Contains(testRig.TestAccessor.MonitoredSilos, pair => pair.Key.Equals(suspectedSilo));
            Assert.Contains(testRig.TestAccessor.MonitoredSilos, pair => pair.Key.Equals(shuttingDownSilo));
            Assert.DoesNotContain(testRig.TestAccessor.MonitoredSilos, pair => pair.Key.Equals(freshSilo));

            await StopLifecycle(cancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本操作,但间接探测中断。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_NoIndirectProbes()
        {
            await ClusterHealthMonitor_BasicScenario_Runner(
                enableIndirectProbes: false,
                numVotesForDeathDeclaration: 2,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本操作,但间接探测中断。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_ThreeVotesNeededToKill()
        {
            await ClusterHealthMonitor_BasicScenario_Runner(
                enableIndirectProbes: true,
                numVotesForDeathDeclaration: 3,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本操作,但间接探测中断。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_OneVoteNeededToKill()
        {
            await ClusterHealthMonitor_BasicScenario_Runner(
                enableIndirectProbes: false,
                numVotesForDeathDeclaration: 1,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本操作,但启用了 EvictWhenMaxJoinAttemptTimeExceeded 。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_SilosWithStaleCreatedOrJoiningState_OneVoteNeededToKill()
        {
            await ClusterHealthMonitor_StaleJoinOrCreatedSilos_Runner(
                evictWhenMaxJoinAttemptTimeExceeded: true,
                numVotesForDeathDeclaration: 1,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本操作,但启用了 EvictWhenMaxJoinAttemptTimeExceeded 。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_SilosWithStaleCreatedOrJoiningState_TwoVotesNeededToKill()
        {
            await ClusterHealthMonitor_StaleJoinOrCreatedSilos_Runner(
                evictWhenMaxJoinAttemptTimeExceeded: true,
                numVotesForDeathDeclaration: 2,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本操作,但启用了 EvictWhenMaxJoinAttemptTimeExceeded 。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_SilosWithStaleCreatedOrJoiningState_ThreeVotesNeededToKill()
        {
            await ClusterHealthMonitor_StaleJoinOrCreatedSilos_Runner(
                evictWhenMaxJoinAttemptTimeExceeded: true,
                numVotesForDeathDeclaration: 3,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterHealthMonitor"/> 和 <see cref="SiloHealthMonitor"/>的基本操作,但启用了 EvictWhenMaxJoinAttemptTimeExceeded 。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_SilosWithStaleCreatedOrJoiningState_Disabled()
        {
            await ClusterHealthMonitor_StaleJoinOrCreatedSilos_Runner(
                evictWhenMaxJoinAttemptTimeExceeded: false,
                numVotesForDeathDeclaration: 3,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试当激活连接最近收到目标 silo发来的信息时,
        /// 即使探测器失灵 也禁止投票怀疑/杀害
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_ConnectionCanary_SuppressesVoteWhenConnectionActive()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var clusterMembershipOptions = new ClusterMembershipOptions
            {
                EnableIndirectProbes = false,
                NumProbedSilos = 1,
                NumVotesForDeathDeclaration = 1,
            };

            var canaryConnectionManager = new ConnectionManager(
                Options.Create(new ConnectionOptions()),
                null!,
                this.loggerFactory.CreateLogger<ConnectionManager>());

            var testRig = CreateClusterHealthMonitorTestRig(clusterMembershipOptions, canaryConnectionManager);

            // 设置探测器, 总是失败。
            var probeCalls = new ConcurrentQueue<SiloAddress>();
            this.prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue(info.ArgAt<SiloAddress>(0));
                return Task.FromException(new Exception("probe failed"));
            });

            await this.lifecycle.OnStart(cancellationToken);

            var targetSilo = Silo("127.0.0.200:100@100");
            await this.membershipTable.InsertRowAsync(Entry(targetSilo, SiloStatus.Active, now), this.membershipTable.Version.Next(), cancellationToken);
            await testRig.Manager.Refresh(cancellationToken: cancellationToken);
            await testRig.Manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            await testRig.Manager.Refresh(cancellationToken: cancellationToken);

            await Until(() => testRig.TestAccessor.MonitoredSilos.Count > 0, cancellationToken);

            // 登记测试连接并模拟最近的电文活动。
            var testConnection = CreateTestConnection(this.loggerFactory);
            canaryConnectionManager.OnConnected(targetSilo, testConnection);
            testConnection.SimulateMessageReceived();

            // 启动足够的探测失败 通常会引发投票
            for (var i = 0; i < clusterMembershipOptions.NumMissedProbesLimit + 1; i++)
            {
                if (this.timerCalls.TryDequeue(out var timer))
                {
                    timer.Completion.TrySetResult(true);
                }

                // 继续重新印金丝雀,让它保持新鲜
                testConnection.SimulateMessageReceived();
                await Task.Delay(50, cancellationToken);
            }

            await Until(() => probeCalls.Count >= clusterMembershipOptions.NumMissedProbesLimit, cancellationToken);
            await Task.Delay(100, cancellationToken);

            // silo 不应死,因为金丝雀检测到主动连接流量。
            var table = await this.membershipTable.ReadAllAsync(cancellationToken);
            var entry = table.Members.SingleOrDefault(m => m.Item1.SiloAddress.Equals(targetSilo));
            Assert.NotNull(entry);
            Assert.NotEqual(SiloStatus.Dead, entry.Item1.Status);

            await StopLifecycle(cancellationToken);
        }

        /// <summary>
        /// 测试当目标 silo没有连接时,罐体不会干扰
        /// 并且 silo 通常在探测失败后宣布死亡。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_ConnectionCanary_AllowsVoteWhenNoConnection()
        {
            // 与连接管理器没有连接, 卡纳里返回无效 - > 投票。
            await ClusterHealthMonitor_BasicScenario_Runner(
                enableIndirectProbes: false,
                numVotesForDeathDeclaration: 1,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// 测试 <see cref="ClusterMembershipOptions.EnableConnectionLivenessCheck"/> 被禁用时
        /// 即使存在主动连接, 也仍然正常地继续。
        /// </summary>
        [Fact]
        public async Task ClusterHealthMonitor_ConnectionCanary_DisabledByOption()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var now = DateTimeOffset.UtcNow;
            var clusterMembershipOptions = new ClusterMembershipOptions
            {
                EnableIndirectProbes = false,
                NumProbedSilos = 1,
                NumVotesForDeathDeclaration = 1,
                EnableConnectionLivenessCheck = false,
            };

            var canaryConnectionManager = new ConnectionManager(
                Options.Create(new ConnectionOptions()),
                null!,
                this.loggerFactory.CreateLogger<ConnectionManager>());

            var testRig = CreateClusterHealthMonitorTestRig(clusterMembershipOptions, canaryConnectionManager);

            var probeCalls = new ConcurrentQueue<SiloAddress>();
            this.prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue(info.ArgAt<SiloAddress>(0));
                return Task.FromException(new Exception("probe failed"));
            });

            await this.lifecycle.OnStart(cancellationToken);

            var targetSilo = Silo("127.0.0.200:100@100");
            await this.membershipTable.InsertRowAsync(Entry(targetSilo, SiloStatus.Active, now), this.membershipTable.Version.Next(), cancellationToken);
            await testRig.Manager.Refresh(cancellationToken: cancellationToken);
            await testRig.Manager.UpdateStatus(SiloStatus.Active, cancellationToken);
            await testRig.Manager.Refresh(cancellationToken: cancellationToken);

            await Until(() => testRig.TestAccessor.MonitoredSilos.Count > 0, cancellationToken);

            // 登记测试连接并模拟最近的电文活动。
            var testConnection = CreateTestConnection(this.loggerFactory);
            canaryConnectionManager.OnConnected(targetSilo, testConnection);
            testConnection.SimulateMessageReceived();

            // 足够驱动检测失败引发投票
            for (var i = 0; i < clusterMembershipOptions.NumMissedProbesLimit + 1; i++)
            {
                if (this.timerCalls.TryDequeue(out var timer))
                {
                    timer.Completion.TrySetResult(true);
                }

                testConnection.SimulateMessageReceived();
                await Task.Delay(50, cancellationToken);
            }

            await Until(async () =>
            {
                var snapshot = await this.membershipTable.ReadAllAsync(cancellationToken);
                return snapshot.Members.Any(m => m.Item1.SiloAddress.Equals(targetSilo) && m.Item1.Status == SiloStatus.Dead);
            }, cancellationToken);

            // 尽管存在积极联系,但 silo 应该已经死亡,因为选择是残疾的。
            var table = await this.membershipTable.ReadAllAsync(cancellationToken);
            var entry = table.Members.SingleOrDefault(m => m.Item1.SiloAddress.Equals(targetSilo));
            Assert.NotNull(entry);
            Assert.Equal(SiloStatus.Dead, entry.Item1.Status);

            await StopLifecycle(cancellationToken);
        }

        private async Task ClusterHealthMonitor_BasicScenario_Runner(
            bool enableIndirectProbes,
            int? numVotesForDeathDeclaration,
            CancellationToken cancellationToken,
            bool otherSilosAreStale = false)
        {
            var now = DateTimeOffset.UtcNow;
            var clusterMembershipOptions = new ClusterMembershipOptions
            {
                EnableIndirectProbes = enableIndirectProbes,
                NumProbedSilos = 3,
            };

            if (numVotesForDeathDeclaration.HasValue)
            {
                clusterMembershipOptions.NumVotesForDeathDeclaration = numVotesForDeathDeclaration.Value;
            }

            var testRig = CreateClusterHealthMonitorTestRig(clusterMembershipOptions);
            using var membershipEvents = new DiagnosticEventCollector(MembershipEvents.ListenerName);
            var probeCalls = new ConcurrentQueue<(SiloAddress Target, int ProbeNumber, bool IsIndirect)>();
            this.prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue((info.ArgAt<SiloAddress>(0), info.ArgAt<int>(1), false));
                return Task.CompletedTask;
            });
            this.prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue((info.ArgAt<SiloAddress>(1), info.ArgAt<int>(3), true));
                return Task.FromResult(new IndirectProbeResponse
                {
                    IntermediaryHealthScore = 0,
                    ProbeResponseTime = TimeSpan.FromMilliseconds(1),
                    Succeeded = true
                });
            });

            await this.lifecycle.OnStart(cancellationToken);
            Assert.Empty(testRig.TestAccessor.MonitoredSilos);

            var iAmAliveTime = otherSilosAreStale ? now.Subtract(TimeSpan.FromHours(1)) : now;
            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.200:100@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:200@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:300@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:400@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:500@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:600@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:700@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:800@100"), SiloStatus.Active, iAmAliveTime),
                Entry(Silo("127.0.0.200:900@100"), SiloStatus.Active, iAmAliveTime)
            };

            var lastVersion = testRig.TestAccessor.ObservedVersion;

            // 添加新的 silos
            var table = await this.membershipTable.ReadAllAsync(cancellationToken);
            foreach (var entry in otherSilos)
            {
                table = await this.membershipTable.ReadAllAsync(cancellationToken);
                Assert.True(await this.membershipTable.InsertRowAsync(entry, table.Version.Next(), cancellationToken));
            }

            await testRig.Manager.Refresh(cancellationToken: cancellationToken);

            await Until(() => testRig.TestAccessor.ObservedVersion > lastVersion, cancellationToken);
            lastVersion = testRig.TestAccessor.ObservedVersion;

            // 无 silos 应以这种方式加以监测 silo 直到它开始活动
            Assert.Empty(testRig.TestAccessor.MonitoredSilos);

            await testRig.Manager.UpdateStatus(SiloStatus.Active, cancellationToken);

            await Until(() => testRig.TestAccessor.ObservedVersion > lastVersion, cancellationToken);
            lastVersion = testRig.TestAccessor.ObservedVersion;

            // 由于此 silo 活动, 它应该正在监测其他活动的部分 silos
            await Until(() => testRig.TestAccessor.MonitoredSilos.Count > 0, cancellationToken);
            Assert.NotEmpty(this.timers);
            Assert.DoesNotContain(testRig.TestAccessor.MonitoredSilos, s => s.Key.Equals(this.localSilo));
            var expectedNumProbedSilos = otherSilosAreStale ? otherSilos.Length : clusterMembershipOptions.NumProbedSilos;
            Assert.Equal(expectedNumProbedSilos, testRig.TestAccessor.MonitoredSilos.Count);
            Assert.All(testRig.TestAccessor.MonitoredSilos, m => m.Key.Equals(m.Value.TargetSiloAddress));
            Assert.Empty(probeCalls);

            // 检查这些 silos 是否正在实际定期被调查
            await UntilEqual(expectedNumProbedSilos, () =>
            {
                if (this.timerCalls.TryDequeue(out var timer))
                {
                    timer.Completion.TrySetResult(true);
                }

                return probeCalls.Count;
            }, cancellationToken);
            Assert.Equal(expectedNumProbedSilos, probeCalls.Count);
            while (probeCalls.TryDequeue(out var call)) Assert.Contains(testRig.TestAccessor.MonitoredSilos, k => k.Key.Equals(call.Item1));

            var monitoredSilos = testRig.TestAccessor.MonitoredSilos.Values.ToList();
            foreach (var siloMonitor in monitoredSilos)
            {
                Assert.Equal(0, ((SiloHealthMonitor.ITestAccessor)siloMonitor).MissedProbes);
            }

            // 使探测器失效
            this.prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue((info.ArgAt<SiloAddress>(0), info.ArgAt<int>(1), true));
                return Task.FromException(new Exception("no"));
            });
            this.prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue((info.ArgAt<SiloAddress>(1), info.ArgAt<int>(3), true));
                return Task.FromResult(new IndirectProbeResponse
                {
                    FailureMessage = "We failed the probe on purpose, as a joke",
                    IntermediaryHealthScore = 0,
                    ProbeResponseTime = TimeSpan.FromSeconds(1),
                    Succeeded = false
                });
            });

            // 上述要求具体说明探测行为也标注了一个价值,此处明确了这一点。
            while (probeCalls.TryDequeue(out _)) ;

            for (var expectedMissedProbes = 1; expectedMissedProbes <= clusterMembershipOptions.NumMissedProbesLimit; expectedMissedProbes++)
            {
                this.membershipTable.ClearCalls();

                // 等待探测器发射
                await UntilEqual(expectedNumProbedSilos, () =>
                {
                    if (this.timerCalls.TryDequeue(out var timer))
                    {
                        timer.Completion.TrySetResult(true);
                    }

                    return probeCalls.Count;
                }, cancellationToken);

                while (probeCalls.TryDequeue(out var call)) ;

                if (expectedMissedProbes >= clusterMembershipOptions.NumMissedProbesLimit)
                {
                    var expectDead = (clusterMembershipOptions.NumVotesForDeathDeclaration <= 2 && enableIndirectProbes) || numVotesForDeathDeclaration == 1;
                    await WaitForMembershipSnapshot(membershipEvents, snapshot =>
                    {
                        return monitoredSilos.All(siloMonitor =>
                        {
                            if (!snapshot.Entries.TryGetValue(siloMonitor.TargetSiloAddress, out var entry))
                            {
                                return false;
                            }

                            var votes = entry.GetFreshVotes(now.UtcDateTime, clusterMembershipOptions.DeathVoteExpirationTimeout);
                            return votes.Any(vote => vote.Item1.Equals(localSiloDetails.SiloAddress)) && (!expectDead || entry.Status == SiloStatus.Dead);
                        });
                    }, cancellationToken);
                }

                // 检查探测器是否匹配预期的未选中探测器
                table = await this.membershipTable.ReadAllAsync(cancellationToken);
                foreach (var siloMonitor in monitoredSilos)
                {
                    Assert.Equal(expectedMissedProbes, ((SiloHealthMonitor.ITestAccessor)siloMonitor).MissedProbes);

                    var entry = table.Members.Single(m => m.Item1.SiloAddress.Equals(siloMonitor.TargetSiloAddress)).Item1;
                    var votes = entry.GetFreshVotes(now.UtcDateTime, clusterMembershipOptions.DeathVoteExpirationTimeout);
                    if (expectedMissedProbes < clusterMembershipOptions.NumMissedProbesLimit)
                    {
                        Assert.Empty(votes);
                    }
                    else
                    {
                        // 在出现一定数目的失败后,应在表中增加表决。
                        Assert.Contains(votes, vote => vote.Item1.Equals(localSiloDetails.SiloAddress));
                        if (clusterMembershipOptions.NumVotesForDeathDeclaration <= 2 && enableIndirectProbes || numVotesForDeathDeclaration == 1)
                        {
                            Assert.Equal(SiloStatus.Dead, entry.Status);
                        }
                    }
                }
            }

            if (enableIndirectProbes && numVotesForDeathDeclaration <= 2 || numVotesForDeathDeclaration == 1)
            {
                table = await this.membershipTable.ReadAllAsync(cancellationToken);
                Assert.Equal(expectedNumProbedSilos, table.Members.Count(m => m.Item1.Status == SiloStatus.Dead));

                // 这里没有更多的试验了,因为所有监测的 silos 都已死亡。
                return;
            }

            await testRig.Manager.Refresh(cancellationToken: cancellationToken);

            // 让探测器再次成功
            this.prober.Probe(default!, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue((info.ArgAt<SiloAddress>(0), info.ArgAt<int>(1), false));
                return Task.CompletedTask;
            });
            this.prober.ProbeIndirectly(default!, default!, default, default, cancellationToken).ReturnsForAnyArgs(info =>
            {
                probeCalls.Enqueue((info.ArgAt<SiloAddress>(1), info.ArgAt<int>(3), true));
                return Task.FromResult(new IndirectProbeResponse
                {
                    IntermediaryHealthScore = 0,
                    ProbeResponseTime = TimeSpan.FromMilliseconds(1),
                    Succeeded = true
                });
            });

            // 上述要求具体说明探测行为也标注了一个价值,此处明确了这一点。
            while (probeCalls.TryDequeue(out _)) ;

            // 等待探测器发射
            this.output.WriteLine($"Firing probes for silos: {string.Join(", ", testRig.TestAccessor.MonitoredSilos.Keys)}");
            var probesReceived = new HashSet<SiloAddress>();
            await UntilEqual(testRig.TestAccessor.MonitoredSilos.Count, () =>
            {
                if (this.timerCalls.TryDequeue(out var timer))
                {
                    timer.Completion.TrySetResult(true);
                }

                while (probeCalls.TryDequeue(out var call))
                {
                    probesReceived.Add(call.Target);
                }

                return probesReceived.Count;
            }, cancellationToken);

            foreach (var siloMonitor in testRig.TestAccessor.MonitoredSilos.Values)
            {
                this.output.WriteLine($"Checking missed probes on {siloMonitor.TargetSiloAddress}: {((SiloHealthMonitor.ITestAccessor)siloMonitor).MissedProbes}");
                Assert.Equal(0, ((SiloHealthMonitor.ITestAccessor)siloMonitor).MissedProbes);
            }

            await StopLifecycle(cancellationToken);
        }

        private async Task ClusterHealthMonitor_StaleJoinOrCreatedSilos_Runner(
            bool evictWhenMaxJoinAttemptTimeExceeded,
            int? numVotesForDeathDeclaration,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var clusterMembershipOptions = new ClusterMembershipOptions
            {
                EvictWhenMaxJoinAttemptTimeExceeded = evictWhenMaxJoinAttemptTimeExceeded
            };

            if (numVotesForDeathDeclaration.HasValue)
            {
                clusterMembershipOptions.NumVotesForDeathDeclaration = numVotesForDeathDeclaration.Value;
            }

            var testRig = CreateClusterHealthMonitorTestRig(clusterMembershipOptions);
            using var membershipEvents = new DiagnosticEventCollector(MembershipEvents.ListenerName);

            var otherSilos = new[]
            {
                Entry(Silo("127.0.0.200:100@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:200@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:300@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:400@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:500@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:600@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:700@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:800@100"), SiloStatus.Active, now),
                Entry(Silo("127.0.0.200:900@100"), SiloStatus.Active, now)
            };

            var joiningSilo = "127.0.0.200:111@100";
            var createdSilo = "127.0.0.200:112@100";

            // 默认 MaxJoinAttemptTime 为5分钟,设定为6分钟前的6分钟将保证它们立即被标记
            var staleCreatedOrJoiningSilos = new[]
            {
                Entry(Silo(joiningSilo), SiloStatus.Joining, DateTime.UtcNow.AddMinutes(-6)),
                Entry(Silo(createdSilo), SiloStatus.Created, DateTime.UtcNow.AddMinutes(-6)),
            };

            otherSilos = [.. otherSilos, .. staleCreatedOrJoiningSilos];

            var lastVersion = testRig.TestAccessor.ObservedVersion;

            // 添加新的 silos
            var table = await this.membershipTable.ReadAllAsync(cancellationToken);
            foreach (var entry in otherSilos)
            {
                table = await this.membershipTable.ReadAllAsync(cancellationToken);
                Assert.True(await this.membershipTable.InsertRowAsync(entry, table.Version.Next(), cancellationToken));
            }

            table = await this.membershipTable.ReadAllAsync(cancellationToken);
            var joiningEntry = GetEntryFromTable(table, joiningSilo);
            var createdEntry = GetEntryFromTable(table, createdSilo);

            Assert.NotNull(joiningEntry);
            Assert.NotNull(createdEntry);

            Assert.Equal(expected: SiloStatus.Joining, actual: joiningEntry.Item1.Status);
            Assert.Equal(expected: SiloStatus.Created, actual: createdEntry.Item1.Status);

            // 我们要加上 numVotesForDeathDeclaration - 1票给创建或加入的选民 silos
            var totalRequiredVotes = clusterMembershipOptions.NumVotesForDeathDeclaration;

            var votesNeeded = totalRequiredVotes - 1;

            // 加入和创建 silos 后,在获得所需票数之前不应宣布死亡。
            while (votesNeeded > 0)
            {
                table = await this.membershipTable.ReadAllAsync(cancellationToken);
                joiningEntry = GetEntryFromTable(table, joiningSilo);
                Assert.NotNull(joiningEntry);
                joiningEntry.Item1.AddSuspector(otherSilos[0].SiloAddress, DateTime.UtcNow);
                Assert.True(await this.membershipTable.UpdateRowAsync(joiningEntry.Item1, joiningEntry.Item2, table.Version.Next(), cancellationToken));

                table = await this.membershipTable.ReadAllAsync(cancellationToken);
                createdEntry = GetEntryFromTable(table, createdSilo);
                Assert.NotNull(createdEntry);
                createdEntry.Item1.AddSuspector(otherSilos[0].SiloAddress, DateTime.UtcNow);
                Assert.True(await this.membershipTable.UpdateRowAsync(createdEntry.Item1, createdEntry.Item2, table.Version.Next(), cancellationToken));

                votesNeeded--;
            }

            table = await this.membershipTable.ReadAllAsync(cancellationToken);
            joiningEntry = GetEntryFromTable(table, joiningSilo);
            createdEntry = GetEntryFromTable(table, createdSilo);
            Assert.NotNull(joiningEntry);
            Assert.NotNull(createdEntry);

            // 如果 numVotesForDeathDeclaration =1
            if (totalRequiredVotes > 1 && evictWhenMaxJoinAttemptTimeExceeded)
            {
                var initialJoiningSuspectTimes = joiningEntry.Item1.SuspectTimes;
                var initialCreatedSuspectTimes = createdEntry.Item1.SuspectTimes;
                Assert.NotNull(initialJoiningSuspectTimes);
                Assert.NotNull(initialCreatedSuspectTimes);
                Assert.Equal(totalRequiredVotes - 1, initialJoiningSuspectTimes.Count);
                Assert.Equal(totalRequiredVotes - 1, initialCreatedSuspectTimes.Count);
            }

            // 现在,我们开始生命周期 让当地 silo 加入最后的投票
            await this.lifecycle.OnStart(cancellationToken);

            await testRig.Manager.Refresh(cancellationToken: cancellationToken);

            if (evictWhenMaxJoinAttemptTimeExceeded)
            {
                await WaitForMembershipSnapshot(membershipEvents, snapshot =>
                {
                    return snapshot.Entries.TryGetValue(Silo(joiningSilo), out var joining)
                        && joining.Status == SiloStatus.Dead
                        && snapshot.Entries.TryGetValue(Silo(createdSilo), out var created)
                        && created.Status == SiloStatus.Dead;
                }, cancellationToken);
            }

            await Until(() => testRig.TestAccessor.ObservedVersion > lastVersion, cancellationToken);

            lastVersion = testRig.TestAccessor.ObservedVersion;

            table = await this.membershipTable.ReadAllAsync(cancellationToken);
            joiningEntry = GetEntryFromTable(table, joiningSilo);
            createdEntry = GetEntryFromTable(table, createdSilo);
            Assert.NotNull(joiningEntry);
            Assert.NotNull(createdEntry);

            var expectedVotes = totalRequiredVotes == 1
                ? 2
                : totalRequiredVotes;

            expectedVotes = evictWhenMaxJoinAttemptTimeExceeded
                ? totalRequiredVotes
                : totalRequiredVotes - 1;

            var joiningSuspectTimes = joiningEntry.Item1.SuspectTimes;
            var createdSuspectTimes = createdEntry.Item1.SuspectTimes;
            Assert.NotNull(joiningSuspectTimes);
            Assert.NotNull(createdSuspectTimes);
            Assert.True(expectedVotes <= joiningSuspectTimes.Count);
            Assert.True(expectedVotes <= createdSuspectTimes.Count);

            Assert.Equal(expected: evictWhenMaxJoinAttemptTimeExceeded ? SiloStatus.Dead : SiloStatus.Joining, actual: joiningEntry.Item1.Status);
            Assert.Equal(expected: evictWhenMaxJoinAttemptTimeExceeded ? SiloStatus.Dead : SiloStatus.Created, actual: createdEntry.Item1.Status);

            await StopLifecycle(cancellationToken);

            static Tuple<MembershipEntry, string>? GetEntryFromTable(MembershipTableData table, string siloAddress)
            {
                return table.Members.FirstOrDefault(entry => entry.Item1.SiloAddress.ToParsableString() == siloAddress);
            }
        }

        private static SiloAddress Silo(string value) => SiloAddress.FromParsableString(value);

        private static MembershipEntry Entry(SiloAddress address, SiloStatus status, DateTimeOffset startTime = default) => new MembershipEntry { SiloAddress = address, Status = status, StartTime = startTime.UtcDateTime, IAmAliveTime = startTime.UtcDateTime };

        private static async Task UntilEqual<T>(
            T expected,
            Func<T> getActual,
            CancellationToken cancellationToken)
        {
            var maxTimeout = 40_000;
            var equalityComparer = EqualityComparer<T>.Default;
            var actual = getActual();
            while (!equalityComparer.Equals(expected, actual) && (maxTimeout -= 10) > 0)
            {
                await Task.Delay(10, cancellationToken);
                actual = getActual();
            }

            Assert.Equal(expected, actual);
            Assert.True(maxTimeout > 0);
        }

        private static async Task Until(Func<bool> condition, CancellationToken cancellationToken)
        {
            var maxTimeout = 40_000;
            while (!condition() && (maxTimeout -= 10) > 0) await Task.Delay(10, cancellationToken);
            Assert.True(maxTimeout > 0);
        }

        private static async Task Until(
            Func<Task<bool>> condition,
            CancellationToken cancellationToken)
        {
            var maxTimeout = 40_000;
            while (!await condition() && (maxTimeout -= 10) > 0)
            {
                await Task.Delay(10, cancellationToken);
            }

            Assert.True(maxTimeout > 0);
        }

        private static async Task<MembershipTableSnapshot> WaitForMembershipSnapshot(
            DiagnosticEventCollector membershipEvents,
            Func<MembershipTableSnapshot, bool> condition,
            CancellationToken cancellationToken)
        {
            var diagnosticEvent = await membershipEvents.WaitForEventAsync(
                nameof(MembershipEvents.ViewChanged),
                evt => evt.Payload is MembershipEvents.ViewChanged viewChanged && condition(viewChanged.Snapshot),
                TimeSpan.FromSeconds(40),
                cancellationToken);

            return Assert.IsType<MembershipEvents.ViewChanged>(diagnosticEvent.Payload).Snapshot;
        }

        private async Task StopLifecycle(CancellationToken cancellationToken)
        {
            var stopped = this.lifecycle.OnStop(cancellationToken);

            while (!stopped.IsCompleted)
            {
                while (this.timerCalls.TryDequeue(out var call)) call.Completion.TrySetResult(false);
                await Task.Delay(15, cancellationToken);
            }

            await stopped;
        }

        private class ClusterHealthMonitorTestRig(
            MembershipTableManager manager,
            IOptionsMonitor<ClusterMembershipOptions> optionsMonitor,
            ClusterHealthMonitor.ITestAccessor testAccessor)
        {
            public readonly MembershipTableManager Manager = manager;
            public readonly IOptionsMonitor<ClusterMembershipOptions> OptionsMonitor = optionsMonitor;
            public readonly ClusterHealthMonitor.ITestAccessor TestAccessor = testAccessor;
        }

        private ClusterHealthMonitorTestRig CreateClusterHealthMonitorTestRig(ClusterMembershipOptions clusterMembershipOptions)
        {
            return CreateClusterHealthMonitorTestRig(clusterMembershipOptions, this.connectionManager);
        }

        private ClusterHealthMonitorTestRig CreateClusterHealthMonitorTestRig(
            ClusterMembershipOptions clusterMembershipOptions,
            ConnectionManager connManager,
            TimeProvider? timeProvider = null)
        {
            timeProvider ??= TimeProvider.System;
            // 这些固定装置使用固定世代,与时钟无关。
            clusterMembershipOptions.SiloGenerationRetirementAge = null;
            var manager = new MembershipTableManager(
                localSiloDetails: this.localSiloDetails,
                clusterMembershipOptions: Options.Create(clusterMembershipOptions),
                membershipTable: membershipTable,
                fatalErrorHandler: this.fatalErrorHandler,
                gossiper: this.membershipGossiper,
                log: this.loggerFactory.CreateLogger<MembershipTableManager>(),
                timerFactory: new AsyncTimerFactory(this.loggerFactory),
                this.lifecycle,
                timeProvider: timeProvider);
            manager.GetDateTimeUtcNow = () => timeProvider.GetUtcNow().UtcDateTime;

            ((ILifecycleParticipant<ISiloLifecycle>)manager).Participate(this.lifecycle);

            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.ReturnsForAnyArgs(clusterMembershipOptions);

            var monitor = new ClusterHealthMonitor(
                this.localSiloDetails,
                manager,
                this.loggerFactory.CreateLogger<ClusterHealthMonitor>(),
                optionsMonitor,
                this.fatalErrorHandler,
                null!,
                connManager,
                timeProvider);

            ((ILifecycleParticipant<ISiloLifecycle>)monitor).Participate(this.lifecycle);

            var testAccessor = (ClusterHealthMonitor.ITestAccessor)monitor;
            testAccessor.CreateMonitor = s => new SiloHealthMonitor(
                s,
                testAccessor.OnProbeResult,
                optionsMonitor,
                this.loggerFactory,
                this.prober,
                this.timerFactory,
                this.localSiloHealthMonitor,
                manager,
                this.localSiloDetails,
                timeProvider);

            return new(
                manager: manager,
                optionsMonitor: optionsMonitor,
                testAccessor: testAccessor);
        }

        /// <summary>
        /// 创建最小测试 <see cref="Connection"/> 适合登记 <see cref="ConnectionManager"/>.
        /// </summary>
        private TestConnection CreateTestConnection(ILoggerFactory loggerFactory)
        {
            var transport = Substitute.For<MessageTransport>();
            transport.Features.Returns(new FeatureCollection());
            var services = new ServiceCollection();
            services.AddMetrics();
            services.AddSingleton<OrleansInstruments>();
            services.AddSingleton<MessagingInstruments>();
            services.AddSingleton<NetworkingInstruments>();
            services.AddSingleton<MessagingProcessingInstruments>();
            var serviceProvider = services.BuildServiceProvider();
            var messagingInstruments = serviceProvider.GetRequiredService<MessagingInstruments>();
            var messagingTrace = new MessagingTrace(
                loggerFactory,
                messagingInstruments,
                serviceProvider.GetRequiredService<MessagingProcessingInstruments>());
            var shared = new ConnectionCommon(
                serviceProvider,
                null!,
                messagingTrace,
                new ConnectionTrace(loggerFactory),
                messagingInstruments,
                serviceProvider.GetRequiredService<NetworkingInstruments>(),
                new NoOpMessageStatisticsSink());
            return new TestConnection(transport, shared);
        }

        private sealed class TestConnection(MessageTransport transport, ConnectionCommon shared)
            : Connection(transport, shared)
        {
            protected override ConnectionDirection ConnectionDirection => ConnectionDirection.SiloToSilo;
            protected override TimeSpan CloseConnectionTimeout => TimeSpan.FromSeconds(1);
            protected override IMessageCenter MessageCenter => null!;
            protected override bool PrepareMessageForSend(Message msg) => true;
            protected internal override void OnReceivedMessage(Message msg) { }
            protected internal override void RecordMessageReceive(Message message, int totalBytes, int headerBytes) { }
            protected internal override void RecordMessageSend(Message message, int totalBytes, int headerBytes) { }
            protected override void RetryMessage(Message msg, Exception? ex = null) { }
            public void SimulateMessageReceived() => MarkMessageReceived();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public async Task ClusterHealthMonitor_JoiningSiloReevaluatesStalePeersThroughNormalDeathVotes(int healthyPeerCount)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var options = new ClusterMembershipOptions
            {
                IAmAliveTablePublishTimeout = TimeSpan.FromSeconds(15),
                NumMissedTableIAmAliveLimit = 5,
                TableRefreshTimeout = TimeSpan.FromSeconds(15),
                ProbeTimeout = TimeSpan.FromMilliseconds(20),
                MaxJoinAttemptTime = TimeSpan.FromMinutes(3),
                NumMissedProbesLimit = 3,
                NumVotesForDeathDeclaration = 2,
                EnableIndirectProbes = false,
            };
            var clock = new MonitorSelectionTimeProvider(options.TableRefreshTimeout);
            var peer = Silo("127.0.0.1:200@100");
            var peerEntry = Entry(peer, SiloStatus.Active, clock.GetUtcNow());
            var table = await this.membershipTable.ReadAllAsync(cancellationToken);
            Assert.True(await this.membershipTable.InsertRowAsync(peerEntry, table.Version.Next(), cancellationToken));
            var healthyPeers = Enumerable.Range(0, healthyPeerCount)
                .Select(index => Entry(Silo($"127.0.0.2:{300 + index}@100"), SiloStatus.Active, clock.GetUtcNow()))
                .ToArray();
            foreach (var entry in healthyPeers)
            {
                table = await this.membershipTable.ReadAllAsync(cancellationToken);
                Assert.True(await this.membershipTable.InsertRowAsync(entry, table.Version.Next(), cancellationToken));
            }

            var probeTicks = Channel.CreateUnbounded<TaskCompletionSource<bool>>();
            this.timerFactory.CreateDelegate = (_, _) => new DelegateAsyncTimer(_ =>
            {
                var tick = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.True(probeTicks.Writer.TryWrite(tick));
                return tick.Task;
            });
            var rig = CreateClusterHealthMonitorTestRig(options, this.connectionManager, clock);
            var createMonitor = rig.TestAccessor.CreateMonitor;
            rig.TestAccessor.CreateMonitor = silo => silo.Equals(peer) ? createMonitor(silo) : new SiloHealthMonitor(
                silo, rig.TestAccessor.OnProbeResult, rig.OptionsMonitor, this.loggerFactory, this.prober,
                new DelegateAsyncTimerFactory((_, _) => new DelegateAsyncTimer(_ => Task.FromResult(false))),
                this.localSiloHealthMonitor, rig.Manager, this.localSiloDetails, clock);
            var validationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            this.prober.Probe(Arg.Any<SiloAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    if (!call.ArgAt<SiloAddress>(0).Equals(peer))
                    {
                        return Task.CompletedTask;
                    }

                    if (call.ArgAt<int>(1) == 0)
                    {
                        validationStarted.TrySetResult();
                    }

                    return Task.FromException(new SiloUnavailableException("The previous silo has exited."));
                });
            using var agent = new MembershipAgent(
                rig.Manager, this.localSiloDetails, this.fatalErrorHandler, Options.Create(options),
                this.loggerFactory.CreateLogger<MembershipAgent>(),
                new DelegateAsyncTimerFactory((_, _) => new DelegateAsyncTimer(_ => Task.FromResult(false))),
                this.prober, clock);
            ((MembershipAgent.ITestAccessor)agent).GetDateTime = () => clock.GetUtcNow().UtcDateTime;
            ((ILifecycleParticipant<ISiloLifecycle>)agent).Participate(this.lifecycle);
            using var events = new DiagnosticEventCollector(MembershipEvents.ListenerName);
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var started = this.lifecycle.OnStart(startupCancellation.Token);
            TaskCompletionSource<bool>? pendingTick = null;
            try
            {
                await validationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                await clock.TimerCreated.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                var joiningSnapshot = rig.Manager.MembershipTableSnapshot;
                Assert.Equal(SiloStatus.Joining, rig.Manager.CurrentStatus);
                Assert.Empty(rig.TestAccessor.MonitoredSilos);
                Assert.False(started.IsCompleted);
                Assert.Empty(joiningSnapshot.Entries[peer].GetFreshVotes(clock.GetUtcNow().UtcDateTime, options.DeathVoteExpirationTimeout));

                clock.Advance(options.AllowedIAmAliveMissPeriod + options.TableRefreshTimeout);
                foreach (var entry in healthyPeers)
                {
                    await this.membershipTable.UpdateIAmAliveAsync(entry.WithIAmAliveTime(clock.GetUtcNow().UtcDateTime), cancellationToken);
                }

                if (healthyPeerCount > 0)
                {
                    await rig.Manager.Refresh(cancellationToken: cancellationToken, requireFresh: true);
                }

                pendingTick = await probeTicks.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                if (healthyPeerCount == 0)
                {
                    Assert.Same(joiningSnapshot, rig.Manager.MembershipTableSnapshot);
                }

                Assert.Equal(SiloStatus.Joining, rig.Manager.CurrentStatus);

                var suspected = WaitForMembershipSnapshot(events, snapshot =>
                    snapshot.Entries.TryGetValue(peer, out var entry)
                    && entry.GetFreshVotes(clock.GetUtcNow().UtcDateTime, options.DeathVoteExpirationTimeout).Count > 0,
                    cancellationToken);
                for (var probe = 1; probe <= options.NumMissedProbesLimit; ++probe)
                {
                    pendingTick.SetResult(true);
                    pendingTick = await probeTicks.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    if (probe < options.NumMissedProbesLimit)
                    {
                        Assert.Equal(SiloStatus.Active, rig.Manager.MembershipTableSnapshot.GetSiloStatus(peer));
                        Assert.Empty(rig.Manager.MembershipTableSnapshot.Entries[peer].SuspectTimes ?? []);
                    }
                }

                var suspectedSnapshot = await suspected;
                Assert.Equal([this.localSilo], suspectedSnapshot.Entries[peer]
                    .GetFreshVotes(clock.GetUtcNow().UtcDateTime, options.DeathVoteExpirationTimeout)
                    .Select(vote => vote.Item1).Distinct());
                Assert.Equal(healthyPeerCount == 0 ? SiloStatus.Dead : SiloStatus.Active, suspectedSnapshot.GetSiloStatus(peer));
                if (healthyPeerCount == 0)
                {
                    await started.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    Assert.Equal(SiloStatus.Active, rig.Manager.CurrentStatus);
                }
                else
                {
                    Assert.False(started.IsCompleted);
                    Assert.Equal(SiloStatus.Joining, rig.Manager.CurrentStatus);
                    startupCancellation.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(
                        () => started.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
                }

                Assert.True(clock.GetUtcNow().UtcDateTime - peerEntry.IAmAliveTime < options.MaxJoinAttemptTime);
                this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default!, default!, default!);
            }
            finally
            {
                startupCancellation.Cancel();
                pendingTick?.TrySetResult(false);
                await started.SuppressThrowing();
                await StopLifecycle(cancellationToken);
                rig.Manager.Dispose();
            }
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(false, 0)]
        [InlineData(false, 60)]
        public async Task ClusterHealthMonitor_JoiningSiloMonitorsPeerWhichBecomesStaleWithoutMembershipUpdates(bool reachable, int refreshPeriodDays)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var clock = new FakeTimeProvider();
            var options = new ClusterMembershipOptions
            {
                IAmAliveTablePublishTimeout = TimeSpan.FromSeconds(15),
                NumMissedTableIAmAliveLimit = 5,
                TableRefreshTimeout = refreshPeriodDays == 0 ? TimeSpan.FromSeconds(15) : TimeSpan.FromDays(refreshPeriodDays),
            };
            var peer = Silo("127.0.0.1:200@100");
            var snapshot = new MembershipTableSnapshot(
                new MembershipVersion(1),
                ImmutableDictionary<SiloAddress, MembershipEntry>.Empty
                    .Add(this.localSilo, Entry(this.localSilo, SiloStatus.Joining, clock.GetUtcNow()))
                    .Add(peer, Entry(peer, SiloStatus.Active, clock.GetUtcNow())));
            var updates = new ControlledMembershipUpdateStream();
            var manager = Substitute.For<IMembershipManager>();
            manager.CurrentSnapshot.Returns(snapshot);
            manager.LocalSiloStatus.Returns(SiloStatus.Joining);
            manager.MembershipUpdates.Returns(updates);
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(options);
            var lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            await using var monitor = new ClusterHealthMonitor(
                this.localSiloDetails, manager, this.loggerFactory.CreateLogger<ClusterHealthMonitor>(),
                optionsMonitor, this.fatalErrorHandler, null!, this.connectionManager, clock);
            var accessor = (ClusterHealthMonitor.ITestAccessor)monitor;
            var monitorCreated = new TaskCompletionSource<SiloAddress>(TaskCreationOptions.RunContinuationsAsynchronously);
            var probeTicks = Channel.CreateUnbounded<TaskCompletionSource<bool>>();
            var results = Channel.CreateUnbounded<SiloHealthMonitor.ProbeResult>();
            var onProbeResult = accessor.OnProbeResult;
            accessor.OnProbeResult = async (siloMonitor, result) =>
            {
                await onProbeResult(siloMonitor, result);
                Assert.True(results.Writer.TryWrite(result));
            };
            this.prober.Probe(Arg.Any<SiloAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(reachable ? Task.CompletedTask : Task.FromException(new SiloUnavailableException("The peer is unreachable.")));
            accessor.CreateMonitor = silo =>
            {
                monitorCreated.SetResult(silo);
                return new SiloHealthMonitor(
                    silo, accessor.OnProbeResult, optionsMonitor, this.loggerFactory, this.prober,
                    new DelegateAsyncTimerFactory((_, _) => new DelegateAsyncTimer(_ =>
                    {
                        var tick = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        Assert.True(probeTicks.Writer.TryWrite(tick));
                        return tick.Task;
                    })),
                    this.localSiloHealthMonitor, manager, this.localSiloDetails, clock);
            };
            ((ILifecycleParticipant<ISiloLifecycle>)monitor).Participate(lifecycle);

            await lifecycle.OnStart(cancellationToken);
            TaskCompletionSource<bool>? pendingTick = null;
            try
            {
                updates.Publish(snapshot);
                await updates.WaitForReadAsync(2, cancellationToken);
                Assert.Empty(accessor.MonitoredSilos);
                Assert.False(snapshot.Entries[peer].HasMissedIAmAlives(options, clock.GetUtcNow().UtcDateTime));

                clock.Advance(options.AllowedIAmAliveMissPeriod + options.TableRefreshTimeout);

                Assert.True(snapshot.Entries[peer].HasMissedIAmAlives(options, clock.GetUtcNow().UtcDateTime));
                Assert.Equal(peer, await monitorCreated.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
                Assert.Same(snapshot, manager.CurrentSnapshot);
                Assert.Equal(snapshot.Version, accessor.ObservedVersion);
                await manager.DidNotReceive().TrySuspectSilo(
                    Arg.Any<SiloAddress>(), Arg.Any<SiloAddress?>(), Arg.Any<CancellationToken>());
                pendingTick = await probeTicks.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                for (var probe = 1; probe <= options.NumMissedProbesLimit; ++probe)
                {
                    pendingTick.SetResult(true);
                    var result = await results.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    Assert.Equal(reachable ? SiloHealthMonitor.ProbeResultStatus.Succeeded : SiloHealthMonitor.ProbeResultStatus.Failed, result.Status);
                    Assert.Equal(reachable ? 0 : probe, result.FailedProbeCount);
                    await manager.Received(!reachable && probe == options.NumMissedProbesLimit ? 1 : 0)
                        .TrySuspectSilo(peer, null, Arg.Any<CancellationToken>());
                    pendingTick = await probeTicks.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
            finally
            {
                pendingTick?.TrySetResult(false);
                await lifecycle.OnStop(cancellationToken);
                await updates.Completed.WaitAsync(cancellationToken);
            }
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(60d * 24 * 60 * 60 * 1000)]
        public async Task ClusterHealthMonitor_ReevaluationSupportsTableRefreshIntervalAndStreamCompletion(double milliseconds)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var period = TimeSpan.FromMilliseconds(milliseconds);
            TimeProvider clock = milliseconds is > 0 and < 1 ? TimeProvider.System : new FakeTimeProvider();
            var snapshot = new MembershipTableSnapshot(
                new MembershipVersion(1),
                ImmutableDictionary<SiloAddress, MembershipEntry>.Empty
                    .Add(this.localSilo, Entry(this.localSilo, SiloStatus.Joining, clock.GetUtcNow())));
            var updates = new ControlledMembershipUpdateStream();
            var manager = Substitute.For<IMembershipManager>();
            manager.CurrentSnapshot.Returns(snapshot);
            manager.LocalSiloStatus.Returns(SiloStatus.Joining);
            manager.MembershipUpdates.Returns(updates);
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(new ClusterMembershipOptions { TableRefreshTimeout = period });
            this.fatalErrorHandler.IsUnexpected(Arg.Any<Exception>()).Returns(true);
            var lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            await using var monitor = new ClusterHealthMonitor(
                this.localSiloDetails, manager, this.loggerFactory.CreateLogger<ClusterHealthMonitor>(),
                optionsMonitor, this.fatalErrorHandler, null!, this.connectionManager, clock);
            var accessor = (ClusterHealthMonitor.ITestAccessor)monitor;
            ((ILifecycleParticipant<ISiloLifecycle>)monitor).Participate(lifecycle);

            await lifecycle.OnStart(cancellationToken);
            try
            {
                updates.Publish(snapshot);
                await updates.WaitForReadAsync(2, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                Assert.Equal(snapshot.Version, accessor.ObservedVersion);

                updates.Complete();
                await updates.Completed.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                Assert.Empty(monitor.SiloMonitors);
                this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default!, default!, default!);
            }
            finally
            {
                await lifecycle.OnStop(cancellationToken);
            }
        }

        [Fact]
        public async Task ClusterHealthMonitor_ActiveSiloReevaluatesStalePeersAndPreservesExistingMonitors()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var clock = new FakeTimeProvider();
            var options = new ClusterMembershipOptions
            {
                NumProbedSilos = 1,
                NumVotesForDeathDeclaration = 1,
                TableRefreshTimeout = TimeSpan.FromSeconds(15),
            };
            var peers = new[] { Silo("127.0.0.1:200@100"), Silo("127.0.0.1:300@100") };
            var ring = peers.Append(this.localSilo).OrderBy(silo => silo.GetConsistentHashCode(0)).ToArray();
            var ringPeer = ring[(Array.IndexOf(ring, this.localSilo) + 1) % ring.Length];
            var stalePeer = peers.Single(silo => !silo.Equals(ringPeer));
            var snapshot = new MembershipTableSnapshot(
                new MembershipVersion(1),
                peers.Append(this.localSilo).ToImmutableDictionary(
                    silo => silo,
                    silo => Entry(silo, SiloStatus.Active, clock.GetUtcNow())));
            var updates = new ControlledMembershipUpdateStream();
            var manager = Substitute.For<IMembershipManager>();
            manager.CurrentSnapshot.Returns(snapshot);
            manager.LocalSiloStatus.Returns(SiloStatus.Active);
            manager.MembershipUpdates.Returns(updates);
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(options);
            this.fatalErrorHandler.IsUnexpected(Arg.Any<Exception>()).Returns(true);
            var lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            await using var monitor = new ClusterHealthMonitor(
                this.localSiloDetails, manager, this.loggerFactory.CreateLogger<ClusterHealthMonitor>(),
                optionsMonitor, this.fatalErrorHandler, null!, this.connectionManager, clock);
            var accessor = (ClusterHealthMonitor.ITestAccessor)monitor;
            var stalePeerMonitorCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            accessor.CreateMonitor = silo =>
            {
                if (silo.Equals(stalePeer))
                {
                    stalePeerMonitorCreated.SetResult();
                }

                return new SiloHealthMonitor(
                    silo, accessor.OnProbeResult, optionsMonitor, this.loggerFactory, this.prober,
                    new DelegateAsyncTimerFactory((_, _) => new DelegateAsyncTimer(_ => Task.FromResult(false))),
                    this.localSiloHealthMonitor, manager, this.localSiloDetails, clock);
            };
            ((ILifecycleParticipant<ISiloLifecycle>)monitor).Participate(lifecycle);

            await lifecycle.OnStart(cancellationToken);
            try
            {
                updates.Publish(snapshot);
                await updates.WaitForReadAsync(2, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                var originalMonitor = Assert.Single(accessor.MonitoredSilos);
                Assert.Equal(ringPeer, originalMonitor.Key);
                Assert.False(stalePeerMonitorCreated.Task.IsCompleted);
                Assert.False(snapshot.Entries[stalePeer].HasMissedIAmAlives(options, clock.GetUtcNow().UtcDateTime));

                clock.Advance(options.AllowedIAmAliveMissPeriod + options.TableRefreshTimeout);
                await stalePeerMonitorCreated.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                updates.Complete();
                await updates.Completed.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                Assert.Equal(2, accessor.MonitoredSilos.Count);
                Assert.Contains(stalePeer, accessor.MonitoredSilos.Keys);
                Assert.Same(originalMonitor.Value, accessor.MonitoredSilos[ringPeer]);
                Assert.Equal(snapshot.Version, accessor.ObservedVersion);
                await manager.DidNotReceive().TrySuspectSilo(
                    Arg.Any<SiloAddress>(), Arg.Any<SiloAddress?>(), Arg.Any<CancellationToken>());
                this.fatalErrorHandler.DidNotReceiveWithAnyArgs().OnFatalException(default!, default!, default!);
            }
            finally
            {
                await lifecycle.OnStop(cancellationToken);
                await updates.Completed.WaitAsync(cancellationToken);
            }
        }

        [Fact]
        public async Task ClusterHealthMonitor_MembershipUpdateFailureRemainsVisibleWithPendingReevaluation()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var clock = new FakeTimeProvider();
            var updates = new ControlledMembershipUpdateStream();
            var manager = Substitute.For<IMembershipManager>();
            manager.CurrentSnapshot.Returns(new MembershipTableSnapshot(
                MembershipVersion.MinValue, ImmutableDictionary<SiloAddress, MembershipEntry>.Empty));
            manager.MembershipUpdates.Returns(updates);
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(new ClusterMembershipOptions());
            var fatal = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.fatalErrorHandler.IsUnexpected(Arg.Any<Exception>()).Returns(true);
            this.fatalErrorHandler.When(handler => handler.OnFatalException(Arg.Any<object>(), Arg.Any<string>(), Arg.Any<Exception>()))
                .Do(call => fatal.SetResult(call.ArgAt<Exception>(2)));
            var lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            await using var monitor = new ClusterHealthMonitor(
                this.localSiloDetails, manager, this.loggerFactory.CreateLogger<ClusterHealthMonitor>(),
                optionsMonitor, this.fatalErrorHandler, null!, this.connectionManager, clock);
            ((ILifecycleParticipant<ISiloLifecycle>)monitor).Participate(lifecycle);
            await lifecycle.OnStart(cancellationToken);
            try
            {
                await updates.WaitForReadAsync(1, cancellationToken);
                var failure = new InvalidOperationException("Membership provider failed.");
                updates.Complete(failure);

                Assert.Same(failure, await fatal.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
                await updates.Completed.WaitAsync(cancellationToken);
                Assert.Empty(monitor.SiloMonitors);
            }
            finally
            {
                await lifecycle.OnStop(cancellationToken);
            }
        }

        [Fact]
        public async Task ClusterHealthMonitor_StaleJoinEvictionUsesInjectedTimeProvider()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var maxJoinAttemptTime = TimeSpan.FromMinutes(5);
            var timeProvider = new FakeTimeProvider(start);
            var updates = new ControlledMembershipUpdateStream();
            var manager = Substitute.For<IMembershipManager>();
            manager.CurrentSnapshot.Returns(new MembershipTableSnapshot(
                MembershipVersion.MinValue,
                ImmutableDictionary<SiloAddress, MembershipEntry>.Empty));
            manager.MembershipUpdates.Returns(updates);
            manager.TrySuspectSilo(default!, default, cancellationToken).ReturnsForAnyArgs(true);
            var options = new ClusterMembershipOptions
            {
                EvictWhenMaxJoinAttemptTimeExceeded = true,
                MaxJoinAttemptTime = maxJoinAttemptTime,
            };
            var optionsMonitor = Substitute.For<IOptionsMonitor<ClusterMembershipOptions>>();
            optionsMonitor.CurrentValue.Returns(options);
            var lifecycle = new SiloLifecycleSubject(this.loggerFactory.CreateLogger<SiloLifecycleSubject>());
            using var services = new ServiceCollection().BuildServiceProvider();
            await using var monitor = new ClusterHealthMonitor(
                this.localSiloDetails,
                manager,
                this.loggerFactory.CreateLogger<ClusterHealthMonitor>(),
                optionsMonitor,
                this.fatalErrorHandler,
                services,
                this.connectionManager,
                timeProvider);
            ((ILifecycleParticipant<ISiloLifecycle>)monitor).Participate(lifecycle);
            var accessor = (ClusterHealthMonitor.ITestAccessor)monitor;
            var joiningSilo = Silo("127.0.0.200:111@100");
            var joiningEntry = Entry(joiningSilo, SiloStatus.Joining, start);

            await lifecycle.OnStart(cancellationToken);
            try
            {
                await updates.WaitForReadAsync(1, cancellationToken);
                timeProvider.Advance(maxJoinAttemptTime);
                updates.Publish(CreateSnapshot(1, joiningEntry));
                await updates.WaitForReadAsync(2, cancellationToken);

                Assert.Equal(new MembershipVersion(1), accessor.ObservedVersion);
                await manager.DidNotReceive().TrySuspectSilo(
                    Arg.Any<SiloAddress>(),
                    Arg.Any<SiloAddress?>(),
                    Arg.Any<CancellationToken>());

                timeProvider.Advance(TimeSpan.FromTicks(1));
                updates.Publish(CreateSnapshot(2, joiningEntry));
                await updates.WaitForReadAsync(3, cancellationToken);

                Assert.Equal(new MembershipVersion(2), accessor.ObservedVersion);
                await manager.Received(1).TrySuspectSilo(
                    joiningSilo,
                    null,
                    Arg.Is<CancellationToken>(token => token.CanBeCanceled && !token.IsCancellationRequested));
            }
            finally
            {
                var stopTask = lifecycle.OnStop(cancellationToken);
                updates.Complete();
                await stopTask;
                await updates.Completed.WaitAsync(cancellationToken);
            }

            static MembershipTableSnapshot CreateSnapshot(long version, MembershipEntry joiningEntry)
                => new(
                    new MembershipVersion(version),
                    ImmutableDictionary<SiloAddress, MembershipEntry>.Empty.Add(joiningEntry.SiloAddress, joiningEntry));
        }

        private sealed class MonitorSelectionTimeProvider(TimeSpan period) : FakeTimeProvider(DateTimeOffset.UtcNow)
        {
            private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task TimerCreated => _timerCreated.Task;

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan timerPeriod)
            {
                var timer = base.CreateTimer(callback, state, dueTime, timerPeriod);
                if (dueTime == period)
                {
                    _timerCreated.TrySetResult();
                }

                return timer;
            }
        }

        private sealed class ControlledMembershipUpdateStream : IAsyncEnumerable<MembershipTableSnapshot>
        {
            private readonly Channel<MembershipTableSnapshot> _updates = Channel.CreateUnbounded<MembershipTableSnapshot>();
            private readonly Dictionary<int, TaskCompletionSource> _readWaiters = [];
            private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly object _lock = new();
            private int _readCount;

            public Task Completed => _completed.Task;

            public void Publish(MembershipTableSnapshot snapshot) => Assert.True(_updates.Writer.TryWrite(snapshot));

            public void Complete(Exception? exception = null) => _updates.Writer.TryComplete(exception);

            public Task WaitForReadAsync(int readCount, CancellationToken cancellationToken)
            {
                lock (_lock)
                {
                    if (_readCount >= readCount)
                    {
                        return Task.CompletedTask;
                    }

                    if (!_readWaiters.TryGetValue(readCount, out var waiter))
                    {
                        waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        _readWaiters.Add(readCount, waiter);
                    }

                    return waiter.Task.WaitAsync(cancellationToken);
                }
            }

            public IAsyncEnumerator<MembershipTableSnapshot> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                => new Enumerator(this, _updates.Reader, cancellationToken);

            private void OnReadStarted()
            {
                TaskCompletionSource? waiter;
                lock (_lock)
                {
                    _readCount++;
                    _readWaiters.Remove(_readCount, out waiter);
                }

                waiter?.TrySetResult();
            }

            private sealed class Enumerator(
                ControlledMembershipUpdateStream owner,
                ChannelReader<MembershipTableSnapshot> reader,
                CancellationToken cancellationToken) : IAsyncEnumerator<MembershipTableSnapshot>
            {
                public MembershipTableSnapshot Current { get; private set; } = null!;

                public async ValueTask<bool> MoveNextAsync()
                {
                    owner.OnReadStarted();
                    while (await reader.WaitToReadAsync(cancellationToken))
                    {
                        if (reader.TryRead(out var item))
                        {
                            Current = item;
                            return true;
                        }
                    }

                    return false;
                }

                public ValueTask DisposeAsync()
                {
                    owner._completed.TrySetResult();
                    return ValueTask.CompletedTask;
                }
            }
        }
    }
}
