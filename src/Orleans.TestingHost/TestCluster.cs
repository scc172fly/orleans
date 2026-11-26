using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Core.Internal;
using Orleans.Runtime;
using Orleans.Runtime.Placement;
using Orleans.TestingHost.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Orleans.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime.Messaging;
using Orleans.Connections.Transport;
using Orleans.TestingHost.UnixSocketTransport;
using System.Net;
using Orleans.Statistics;
using Orleans.TestingHost.InMemoryTransport;
using Orleans.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Runtime.TestHooks;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 本地测试的主机舱 Orleans 正在使用中处理 silos.
    /// 在不同的应用程序域内运行初级和可选的二级 silos ,在主应用程序域内运行客户端。
    /// 如果特定测试案例需要,还可根据需求启动额外的 silos 程序。
    /// </summary>
    /// <remarks>
    /// 确保您的测试工程引用您的测试 grains 并测试 grain 接口
    /// 工程,并且 CopyLocal= 设置了这些引用的底线[这应该是默认数]。
    /// </remarks>
    public class TestCluster : IDisposable, IAsyncDisposable
    {
        private readonly List<SiloHandle> additionalSilos = new List<SiloHandle>();
        private readonly HashSet<IPEndPoint> siloEndpoints = [];
        private readonly TestClusterOptions options;
        private readonly StringBuilder log = new StringBuilder();
        private readonly InMemoryTransportConnectionHub _transportHub = new();
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "DisposeAsync awaits the cleanup delegate which disposes this observer after the client and silos; Dispose delegates to DisposeAsync.")]
        private readonly GrainDirectoryObserver _grainDirectoryObserver = new();
        private Func<string, IConfiguration, Task<SiloHandle>> _createSiloAsync;
        private Func<string, IConfiguration, CancellationToken, Task<SiloHandle>> _createSiloAsyncWithCancellation;
        private bool _disposed;
        private int startedInstances;

        /// <summary>
        /// 初级 silo 处理,如果适用。
        /// </summary>
        /// <remarks>此处理程序只有在使用 Grain- 成员制时有效。</remarks>
        public SiloHandle? Primary { get; private set; }

        /// <summary>
        /// 二次 silos的控点列表 。
        /// </summary>
        public IReadOnlyList<SiloHandle> SecondarySilos
        {
            get
            {
                lock (this.additionalSilos)
                {
                    return new List<SiloHandle>(this.additionalSilos);
                }
            }
        }

        /// <summary>
        /// 收集所有已知的 silos。
        /// </summary>
        public ReadOnlyCollection<SiloHandle> Silos
        {
            get
            {
                var result = new List<SiloHandle>();
                if (this.Primary != null)
                {
                    result.Add(this.Primary);
                }

                lock (this.additionalSilos)
                {
                    result.AddRange(this.additionalSilos);
                }

                return result.AsReadOnly();
            }
        }

        /// <summary>
        /// 用于配置测试组集的选项。
        /// </summary>
        /// <remarks>这是您配置的测试组选项, 或者默认的选项 。
        /// 如果该组是通过 ClusterConfiguration配置的,则此对象可能不会反映真实设置。
        /// </remarks>
        public TestClusterOptions Options => this.options;

        /// <summary>
        /// 返回地址是否属于此组 。
        /// </summary>
        /// <param name="siloAddress"> silo 地址。</param>
        /// 当地址属于本组时,<returns><see langword="true"/> 。</returns>
        public bool ContainsSilo(SiloAddress siloAddress)
        {
            ArgumentNullException.ThrowIfNull(siloAddress);
            lock (siloEndpoints)
            {
                return siloEndpoints.Contains(siloAddress.Endpoint);
            }
        }

        /// <summary>
        /// 内部客户界面。
        /// </summary>
        internal IHost? ClientHost { get; private set; }

        /// <summary>
        /// 内部客户界面。
        /// </summary>
        internal IInternalClusterClient? InternalClient => ClientHost?.Services.GetRequiredService<IInternalClusterClient>();

        /// <summary>
        /// 客户
        /// </summary>
        /// <exception cref="InvalidOperationException">集群尚未部署或客户被停止。</exception>
        public IClusterClient Client => this.InternalClient ?? throw new InvalidOperationException(
            "The test cluster client is unavailable because the cluster has not been deployed or has been stopped.");

        /// <summary>
        /// GrainFactory 用于试验
        /// </summary>
        public IGrainFactory GrainFactory => this.Client;

        /// <summary>
        /// GrainFactory 用于试验
        /// </summary>
        internal IInternalGrainFactory? InternalGrainFactory => this.InternalClient;

        /// <summary>
        /// 测试中使用的客户端 <see cref="IServiceProvider"/>
        /// </summary>
        public IServiceProvider ServiceProvider => this.Client.ServiceProvider;

        /// <summary>
        /// 用于创建和启动个人 silo的代表。
        /// </summary>
        public Func<string, IConfiguration, Task<SiloHandle>> CreateSiloAsync
        {
            private get => _createSiloAsync;
            set
            {
                _createSiloAsync = value;
                _createSiloAsyncWithCancellation = (name, configuration, _) => value(name, configuration);
            }
        }

        /// <summary>
        /// 用于创建和启动单个 silo 的取消代表。
        /// </summary>
        public Func<string, IConfiguration, CancellationToken, Task<SiloHandle>> CreateSiloAsyncWithCancellation
        {
            private get => _createSiloAsyncWithCancellation;
            set
            {
                _createSiloAsyncWithCancellation = value;
                _createSiloAsync = (name, configuration) => value(name, configuration, CancellationToken.None);
            }
        }

        /// <summary>
        /// 港口分配器
        /// </summary>
        public ITestClusterPortAllocator PortAllocator { get; }

        /// <summary>
        /// 配置测试组群集加客户端正在处理中 。
        /// </summary>
        public TestCluster(
            TestClusterOptions options,
            IReadOnlyList<IConfigurationSource> configurationSources,
            ITestClusterPortAllocator portAllocator)
        {
            this.options = options;
            this.ConfigurationSources = configurationSources.ToArray();
            this.PortAllocator = portAllocator;
            _createSiloAsync = DefaultCreateSiloAsync;
            _createSiloAsyncWithCancellation = DefaultCreateSiloAsync;
        }

        /// <summary>
        /// 返回 <see cref="IServiceProvider"/> 与给定的 <paramref name="silo"/>.
        /// </summary>
        /// <param name="silo">下 silo 向服务提供方发送进程。</param>
        /// <remarks>如果 <paramref name="silo"/> 是 <see langword="null"/> 现有 silos 将随机选择。</remarks>
        public IServiceProvider GetSiloServiceProvider(SiloAddress? silo = null)
        {
            if (silo != null)
            {
                var handle = Silos.FirstOrDefault(x => x.SiloAddress.Equals(silo));
                return handle != null ? ((InProcessSiloHandle)handle).SiloHost.Services :
                    throw new ArgumentException($"The provided silo address '{silo}' is unknown.");
            }
            else
            {
                var index = Random.Shared.Next(Silos.Count);
                return ((InProcessSiloHandle)Silos[index]).SiloHost.Services;
            }
        }

        /// <summary>
        /// 试图寻找 <see cref="IGrainContext"/> 排排 grain 指定 <paramref name="grainId"/>
        /// 中查找组内的所有 silos 。
        /// </summary>
        /// <param name="grainId">要找到的 grain 的代号。</param>
        /// <param name="grainContext">当此方法返回时, 如果发现包含 grain 上下文; 否则, <see langword="null"/>.</param>
        /// <returns><see langword="true"/> 如果 grain 是在其中之一被发现的 silos;否则, <see langword="false"/>.</returns>
        public bool TryGetGrainContext(GrainId grainId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IGrainContext? grainContext)
        {
            foreach (var silo in Silos)
            {
                var activationDirectory = ((InProcessSiloHandle)silo).SiloHost.Services.GetRequiredService<ActivationDirectory>();
                grainContext = activationDirectory.FindTarget(grainId);
                if (grainContext is not null)
                {
                    return true;
                }
            }

            grainContext = null;
            return false;
        }

        /// <summary>
        /// 得到a <see cref="Task"/> 当指定 grain
        /// 完成的取消活动。 如果 grain 目前尚未激活, 返回 <see cref="Task.CompletedTask"/>。
        /// </summary>
        /// <param name="grainId">所要观察的 grain 识别号。</param>
        /// <returns>任务在 grain当前激活已停止时完成。</returns>
        /// <remarks>
        /// 可能存在在测试中先需要记录这项任务,然后才拨打 DeactivateOnIdle/MigrateOnIdle
        /// 以避免在完成返回的任务之前,在停止使用 grain 和恢复使用该任务之前进行竞赛。
        /// </remarks>
        /// <example>
        /// <code>
        /// var deactivated = cluster.WaitForDeactivationAsync(grain.GetGrainId());
        /// await grain.DoDeactivate();
        /// await deactivated;
        /// </code>
        /// </example>
        public Task WaitForDeactivationAsync(GrainId grainId)
        {
            if (TryGetGrainContext(grainId, out var grainContext))
            {
                return grainContext.Deactivated;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 禁用指定 grain 的当前激活,等待解除功能完成。
        /// </summary>
        /// <param name="grainId">身份识别 grain 停止使用。</param>
        /// <returns>任务在 grain 被停止使用后完成。</returns>
        /// <example>
        /// <code>
        /// await cluster.DeactivateAsync(grain.GetGrainId());
        /// </code>
        /// </example>
        public async Task DeactivateAsync(GrainId grainId)
        {
            var deactivated = WaitForDeactivationAsync(grainId);
            await GrainFactory.GetGrain(grainId).Cast<IGrainManagementExtension>().DeactivateOnIdle(CancellationToken.None);
            await deactivated;
        }

        /// <summary>
        /// 将指定 grain 的当前激活移到不同的 silo , 等待迁移完成 。
        /// 如果指定了 <paramref name="targetSilo"/> ,将设置一个位置提示来引导向该 silo迁移。
        /// </summary>
        /// <param name="grainId">身份识别 grain 移徙。</param>
        /// <param name="targetSilo">目标目标 silo 地址,或 <see langword="null"/> 任由安置主任选择</param>
        /// <returns>一项任务,当它完成的时候, grain 已经迁移( 在源头停止活动) silo).</returns>
        /// <example>
        /// <code>
        /// var targetSilo = cluster.GetActiveSilos().First(s => s.SiloAddress != originalHost).SiloAddress;
        /// await cluster.MigrateAsync(grain.GetGrainId(), targetSilo);
        /// </code>
        /// </example>
        public async Task MigrateAsync(GrainId grainId, SiloAddress? targetSilo = null)
        {
            var deactivated = WaitForDeactivationAsync(grainId);
            if (targetSilo is not null)
            {
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetSilo);
            }

            await GrainFactory.GetGrain(grainId).Cast<IGrainManagementExtension>().MigrateOnIdle(CancellationToken.None);
            await deactivated;
        }

        /// <inheritdoc cref="WaitForDeactivationAsync(GrainId)"/>
        /// <param name="grain">下 grain 观察。</param>
        public Task WaitForDeactivationAsync(IAddressable grain) => WaitForDeactivationAsync(grain.GetGrainId());

        /// <inheritdoc cref="DeactivateAsync(GrainId)"/>
        /// <param name="grain">下 grain 停止使用。</param>
        public Task DeactivateAsync(IAddressable grain) => DeactivateAsync(grain.GetGrainId());

        /// <inheritdoc cref="MigrateAsync(GrainId, SiloAddress?)"/>
        /// <param name="grain">下 grain 移徙。</param>
        /// <param name="targetSilo">目标目标 silo 地址,或 <see langword="null"/> 任由安置主任选择</param>
        public Task MigrateAsync(IAddressable grain, SiloAddress? targetSilo = null) => MigrateAsync(grain.GetGrainId(), targetSilo);

        /// <summary>
        /// 使用指定的配置部署组群,并启动客户端的处理。
        /// 它将开始于 silos 定义内 <see cref="TestClusterOptions.InitialSilosCount"/>.
        /// </summary>
        public void Deploy()
        {
            this.DeployAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 使用指定的配置部署组群,并启动客户端的处理。
        /// </summary>
        public Task DeployAsync() => DeployAsync(CancellationToken.None);

        /// <summary>
        /// 使用指定的配置部署组群,并启动客户端的处理。
        /// </summary>
        /// <param name="cancellationToken">用于取消部署的标记。</param>
        public async Task DeployAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.Primary != null || this.additionalSilos.Count > 0) throw new InvalidOperationException("Cluster host already deployed.");

            AppDomain.CurrentDomain.UnhandledException += ReportUnobservedException;

            try
            {
                string startMsg = "----------------------------- STARTING NEW UNIT TEST SILO HOST: " + GetType().FullName + " -------------------------------------";
                WriteLog(startMsg);
                await InitializeAsync(cancellationToken);

                if (this.options.InitializeClientOnDeploy)
                {
                    await WaitForInitialStabilization(cancellationToken);
                }
            }
            catch (TimeoutException te)
            {
                FlushLogToConsole();
                throw new TimeoutException("Timeout during test initialization", te);
            }
            catch (Exception ex)
            {
                await StopAllSilosAsync(CancellationToken.None);

                Exception baseExc = ex.GetBaseException();
                FlushLogToConsole();

                if (baseExc is TimeoutException)
                {
                    throw new TimeoutException("Timeout during test initialization", ex);
                }

                // 重要:
                // 在此不重新丢放原例外, 也不得作为内部例外在 AggregateException内重新投入
                // 由于MS测试工作方式,如果原来的例外是 Orleans 例外,
                // 其组件可能还没有装入 在试验的这个阶段。
                // 因此,我们将获得“MSTest:单位测试适配器被丢弃例外: 类型对于成员三十没有解决”
                // 并会松开原来的例外。这会使调试测试变得超重!
                // 根本原因在于我们从测试构建器而不是 TestInitialize 方法开始测试。
                // 更详细信息: http://dobrzanski.net/2010/09/20/mstest-unit-test-adapter-threw-exception-type-is-not-resolved-for-member/
                //丢弃新的例外( )
                //    string.Format("试验初始化期间的例外:{0}")",
                //        LogFormatter.PrintException(baseExc)));
                throw;
            }
        }

        private async Task WaitForInitialStabilization(CancellationToken cancellationToken)
        {
            // 每人打 silo ,检查是否知道活动 silos的预期数目。
            // 如果有任何 silo 在组群成员中没有预期的活跃 silos 数目,请再试一次。
            // 如果该组成员在一段时间后没有稳定下来,就放弃并无论如何继续。
            var totalWait = Stopwatch.StartNew();
            while (true)
            {
                var silos = this.Silos;
                var expectedCount = silos.Count;
                var remainingSilos = expectedCount;

                foreach (var silo in silos)
                {
                    var hooks = this.InternalClient!.GetTestHooks(silo); // 成员稳定化需要初始化客户。
                    var statuses = await hooks.GetApproximateSiloStatuses().WaitAsync(cancellationToken);
                    var activeCount = statuses.Count(s => s.Value == SiloStatus.Active);
                    if (activeCount != expectedCount) break;
                    remainingSilos--;
                }

                if (remainingSilos == 0)
                {
                    totalWait.Stop();
                    break;
                }

                WriteLog($"{remainingSilos} silos do not have a consistent cluster view, waiting until stabilization.");
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

                if (totalWait.Elapsed < TimeSpan.FromSeconds(60))
                {
                    WriteLog($"Warning! {remainingSilos} silos do not have a consistent cluster view after {totalWait.ElapsedMilliseconds}ms, continuing without stabilization.");
                    break;
                }
            }
        }

        /// <summary>
        /// 获取当前活动的列表 silos。
        /// </summary>
        /// <returns>当前列表列表 silos.</returns>
        public IEnumerable<SiloHandle> GetActiveSilos()
        {
            var additional = new List<SiloHandle>();
            lock (additionalSilos)
            {
                additional.AddRange(additionalSilos);
            }

            WriteLog("GetActiveSilos: Primary={0} + {1} Additional={2}",
                Primary, additional.Count, Runtime.Utils.EnumerableToString(additional));

            if (Primary?.IsActive == true) yield return Primary;


            if (additional.Count > 0)
                foreach (var s in additional)
                    if (s?.IsActive == true)
                        yield return s;
        }

        /// <summary>
        /// 查找 silo 指定控件的控件 silo 地址。
        /// </summary>
        /// <param name="siloAddress">Silo 要找到地址 。</param>
        /// <returns>SiloHandle 适当 silo,或 <c>null</c> 找不到的话。</returns>
        public SiloHandle? GetSiloForAddress(SiloAddress siloAddress)
        {
            var activeSilos = GetActiveSilos().ToList();
            var ret = activeSilos.Find(s => s.SiloAddress.Equals(siloAddress));
            return ret;
        }

        /// <summary>
        /// 等待 silo 活性子系统发现最近分组成员的任何变化并就其采取行动。
        /// </summary>
        /// <param name="didKill">我们通过优雅的停止方式, 是否改变最近的成员。</param>
        public async Task WaitForLivenessToStabilizeAsync(bool didKill = false)
        {
            var clusterMembershipOptions = this.ServiceProvider.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value;
            TimeSpan stabilizationTime = GetLivenessStabilizationTime(clusterMembershipOptions, didKill);
            var activeSilos = GetActiveSilos().ToArray();
            var testHooks = activeSilos.Select(GetTestHooks).ToArray();
            var gatewayManager = this.InternalClient!.ServiceProvider.GetRequiredService<GatewayManager>();
            var inProcessSilos = activeSilos.OfType<InProcessSiloHandle>().ToArray();
            Func<TimeSpan, Task<bool>>? waitForGrainDirectoryConvergence =
                inProcessSilos.Length == activeSilos.Length && GrainDirectoryObserver.CanObserve(inProcessSilos)
                    ? timeout => _grainDirectoryObserver.WaitForConvergenceAsync(inProcessSilos, timeout)
                    : null;
            WriteLog(Environment.NewLine + Environment.NewLine + "WaitForLivenessToStabilize is waiting up to {0} for {1} active silo(s)", stabilizationTime, activeSilos.Length);
            if (await LivenessStabilizationHelper.WaitForExpectedActiveSilosAndGatewaysAsync(
                activeSilos,
                testHooks,
                gatewayManager,
                stabilizationTime,
                waitForGrainDirectoryConvergence))
            {
                WriteLog("WaitForLivenessToStabilize observed stable active silo and gateway views");
            }
            else
            {
                WriteLog("WaitForLivenessToStabilize reached the fallback wait of {0}", stabilizationTime);
            }
        }

        private ITestHooks GetTestHooks(SiloHandle silo)
        {
            if (silo is InProcessSiloHandle inProcessSilo)
            {
                return inProcessSilo.ServiceProvider.GetRequiredService<TestHooksSystemTarget>();
            }

            return this.InternalClient!.GetTestHooks(silo);
        }

        /// <summary>
        /// 等待活动 silos 对所有活动 silos的集散清单进行观察更新。
        /// </summary>
        public async Task WaitForClusterManifestToStabilizeAsync(bool didKill = false)
        {
            var clusterMembershipOptions = this.ServiceProvider.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value;
            var stabilizationTime = GetLivenessStabilizationTime(clusterMembershipOptions, didKill);
            var activeSilos = GetActiveSilos().ToArray();
            var testHooks = activeSilos.Select(silo => this.InternalClient!.GetTestHooks(silo)).ToArray();

            WriteLog(Environment.NewLine + Environment.NewLine + "WaitForClusterManifestToStabilize is waiting up to {0} for {1} active silo manifest(s)", stabilizationTime, activeSilos.Length);
            if (await ClusterManifestStabilizationHelper.WaitForExpectedClusterManifestAsync(activeSilos, testHooks, stabilizationTime))
            {
                WriteLog("WaitForClusterManifestToStabilize observed stable cluster manifests");
            }
            else
            {
                WriteLog("WaitForClusterManifestToStabilize reached the fallback wait of {0}", stabilizationTime);
            }
        }

        /// <summary>
        /// 获取超时值, 等待 silo 活性子系统检测最近分组成员变化的情况并采取行动。
        /// <seealso cref="WaitForLivenessToStabilizeAsync"/>
        /// </summary>
        public static TimeSpan GetLivenessStabilizationTime(ClusterMembershipOptions clusterMembershipOptions, bool didKill = false)
        {
            TimeSpan stabilizationTime = TimeSpan.Zero;
            if (didKill)
            {
                // 如果发生硬杀(杀人而不是停止),我们应该给 silos 时间首先发现故障。
                stabilizationTime = TestingUtils.Multiply(clusterMembershipOptions.MaxProbeTimeout, clusterMembershipOptions.NumMissedProbesLimit);
            }
            if (clusterMembershipOptions.UseLivenessGossip)
            {
                stabilizationTime += TimeSpan.FromSeconds(5);
            }
            else
            {
                stabilizationTime += TestingUtils.Multiply(clusterMembershipOptions.TableRefreshTimeout, 2);
            }
            return stabilizationTime;
        }

        /// <summary>
        /// 启动一个额外的 silo,以便加入现有组群。
        /// </summary>
        /// <returns>SiloHandle 新建的 silo.</returns>
        public SiloHandle StartAdditionalSilo(bool startAdditionalSiloOnNewPort = false)
        {
            return StartAdditionalSiloAsync(startAdditionalSiloOnNewPort).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 启动一个额外的 silo,以便加入现有组群。
        /// </summary>
        /// <returns>SiloHandle 新建的 silo.</returns>
        public async Task<SiloHandle> StartAdditionalSiloAsync(bool startAdditionalSiloOnNewPort = false)
        {
            return (await this.StartAdditionalSilosAsync(1, startAdditionalSiloOnNewPort)).Single();
        }

        /// <summary>
        /// 启动若干额外的 silo,以便加入现有组群。
        /// </summary>
        /// <param name="silosToStart">数目数量 silos 开始。</param>
        /// <param name="startAdditionalSiloOnNewPort"></param>
        /// <returns>名单清单 SiloHandles 新建的 silos.</returns>
        public async Task<List<SiloHandle>> StartAdditionalSilosAsync(int silosToStart, bool startAdditionalSiloOnNewPort = false)
            => await StartAdditionalSilosAsync(
                silosToStart,
                startAdditionalSiloOnNewPort,
                CancellationToken.None);

        /// <summary>
        /// 启动若干额外的 silos,以便加入现有组群。
        /// </summary>
        /// <param name="silosToStart">数目数量 silos 开始。</param>
        /// <param name="startAdditionalSiloOnNewPort">是否在新端口启动每个 silo 。</param>
        /// <param name="cancellationToken">取消时使用的牌点 silo 启动。</param>
        /// <returns>列表 silo 中新启动的 silos的控件</returns>。
        public async Task<List<SiloHandle>> StartAdditionalSilosAsync(
            int silosToStart,
            bool startAdditionalSiloOnNewPort,
            CancellationToken cancellationToken)
        {
            var instances = new List<SiloHandle>();
            if (silosToStart > 0)
            {
                var silos = Silos;
                var firstInstanceNumber = silos.Count == 0
                    ? 0
                    : silos.Max(static silo => silo.InstanceNumber) + 1;
                var siloStartTasks = Enumerable.Range(firstInstanceNumber, silosToStart)
                    .Select(instanceNumber => Task.Run(
                        () => StartSiloAsync(
                            (short)instanceNumber,
                            this.options,
                            configurationOverrides: null,
                            startAdditionalSiloOnNewPort,
                            cancellationToken),
                        cancellationToken))
                    .ToArray();

                try
                {
                    await Task.WhenAll(siloStartTasks);
                }
                catch (Exception)
                {
                    lock (additionalSilos)
                    {
                        this.additionalSilos.AddRange(
                            siloStartTasks
                                .Where(static task => task.Status == TaskStatus.RanToCompletion)
                                .Select(static task => task.Result));
                    }

                    throw;
                }

                instances.AddRange(siloStartTasks.Select(t => t.Result));
                lock (additionalSilos)
                {
                    this.additionalSilos.AddRange(instances);
                }
            }

            return instances;
        }

        /// <summary>
        /// 停止任何附加 silos,不包括默认的初级 silo。
        /// </summary>
        public Task StopSecondarySilosAsync() => StopSecondarySilosAsync(CancellationToken.None);

        /// <summary>
        /// 停止任何附加 silos,不包括默认的初级 silo。
        /// </summary>
        /// <param name="cancellationToken">取消关闭时使用的标记。</param>
        public async Task StopSecondarySilosAsync(CancellationToken cancellationToken)
        {
            foreach (var instance in this.additionalSilos.ToList())
            {
                await StopSiloAsync(instance, cancellationToken);
            }
        }

        /// <summary>
        /// 停止默认主 silo。
        /// </summary>
        public Task StopPrimarySiloAsync() => StopPrimarySiloAsync(CancellationToken.None);

        /// <summary>
        /// 停止默认主 silo。
        /// </summary>
        /// <param name="cancellationToken">取消关闭时使用的标记。</param>
        public async Task StopPrimarySiloAsync(CancellationToken cancellationToken)
        {
            if (Primary == null) throw new InvalidOperationException("There is no primary silo");
            await StopClusterClientAsync(cancellationToken);
            await StopSiloAsync(Primary, cancellationToken);
        }

        /// <summary>
        /// 停止将客户群分组为非同步操作 。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表非同步操作。</returns>
        public Task StopClusterClientAsync() => StopClusterClientAsync(CancellationToken.None);

        /// <summary>
        /// 停止将客户群分组为非同步操作 。
        /// </summary>
        /// <param name="cancellationToken">取消关闭时使用的标记。</param>
        /// <returns>A <see cref="Task"/> 代表非同步操作。</returns>
        public async Task StopClusterClientAsync(CancellationToken cancellationToken)
        {
            var client = this.ClientHost;
            try
            {
                if (client is not null)
                {
                    await client.StopAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exc)
            {
                WriteLog("Exception stopping client: {0}", exc);
                if (exc is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
            finally
            {
                await DisposeAsync(client).ConfigureAwait(false);
                ClientHost = null;
            }
        }

        /// <summary>
        /// 停止全部当前 silos。
        /// </summary>
        public void StopAllSilos()
        {
            StopAllSilosAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 停止全部当前 silos。
        /// </summary>
        public Task StopAllSilosAsync() => StopAllSilosAsync(CancellationToken.None);

        /// <summary>
        /// 停止全部当前 silos。
        /// </summary>
        /// <param name="cancellationToken">取消关闭时使用的标记。</param>
        public async Task StopAllSilosAsync(CancellationToken cancellationToken)
        {
            try
            {
                await StopClusterClientAsync(cancellationToken);
                await StopSecondarySilosAsync(cancellationToken);
                if (Primary != null)
                {
                    await StopPrimarySiloAsync(cancellationToken);
                }
            }
            finally
            {
                AppDomain.CurrentDomain.UnhandledException -= ReportUnobservedException;
            }
        }

        /// <summary>
        /// 执行指定的 silo半可观停止。
        /// </summary>
        /// <param name="instance">Silo 以停止。</param>
        public Task StopSiloAsync(SiloHandle instance) => StopSiloAsync(instance, CancellationToken.None);

        /// <summary>
        /// 执行指定的 silo半可观停止。
        /// </summary>
        /// <param name="instance">Silo 以停止。</param>
        /// <param name="cancellationToken">取消关闭时使用的标记。</param>
        public async Task StopSiloAsync(SiloHandle instance, CancellationToken cancellationToken)
        {
            if (instance != null)
            {
                try
                {
                    await StopSiloAsync(instance, true, cancellationToken);
                }
                finally
                {
                    if (Primary == instance)
                    {
                        Primary = null;
                    }
                    else
                    {
                        lock (additionalSilos)
                        {
                            additionalSilos.Remove(instance);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 立即杀死指定 silo。
        /// </summary>
        /// <param name="instance">Silo 被杀死</param>
        public Task KillSiloAsync(SiloHandle instance) => KillSiloAsync(instance, CancellationToken.None);

        /// <summary>
        /// 立即杀死指定 silo。
        /// </summary>
        /// <param name="instance">Silo 被杀死</param>
        /// <param name="cancellationToken">取消操作所用的标记。</param>
        public async Task KillSiloAsync(SiloHandle instance, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (instance != null)
            {
                try
                {
                    // 而不是停止,只是直接杀死,模拟坠机。
                    await StopSiloAsync(instance, false, cancellationToken);
                }
                finally
                {
                    if (Primary == instance)
                    {
                        Primary = null;
                    }
                    else
                    {
                        lock (additionalSilos)
                        {
                            additionalSilos.Remove(instance);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 执行对客户端的硬性杀戮。 客户端不会清理资源 。
        /// </summary>
        public async Task KillClientAsync()
        {
            var client = ClientHost;
            if (client != null)
            {
                var cancelled = new CancellationToken(canceled: true);
                try
                {
                    await client.StopAsync(cancelled).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (!exception.IsCancellation())
                    {
                        throw;
                    }
                }
                finally
                {
                    await DisposeAsync(client);
                    ClientHost = null;
                }
            }
        }

        /// <summary>
        /// 停止或终止指定的 silo, 然后重新开始 。
        /// </summary>
        /// <param name="instance">Silo 要重新开始。</param>
        public async Task<SiloHandle?> RestartSiloAsync(SiloHandle instance)
        {
            if (instance is not null)
            {
                var instanceNumber = instance.InstanceNumber;
                var siloName = instance.Name;
                await StopSiloAsync(instance);
                var newInstance = await StartSiloAsync(instanceNumber, this.options);

                if (siloName == Silo.PrimarySiloName)
                {
                    Primary = newInstance;
                }
                else
                {
                    lock (additionalSilos)
                    {
                        additionalSilos.Add(newInstance);
                    }
                }

                return newInstance;
            }

            return null;
        }

        /// <summary>
        /// 重新启动先前已停止的状态 。
        /// </summary>
        /// <param name="siloName">Silo 要重新开始。</param>
        public async Task<SiloHandle> RestartStoppedSecondarySiloAsync(string siloName)
        {
            if (siloName == null) throw new ArgumentNullException(nameof(siloName));
            var siloHandle = this.Silos.Single(s => s.Name.Equals(siloName, StringComparison.Ordinal));
            var newInstance = await this.StartSiloAsync(this.Silos.IndexOf(siloHandle), this.options);
            lock (additionalSilos)
            {
                additionalSilos.Add(newInstance);
            }
            return newInstance;
        }

        /// <summary>
        /// 初始化 grain 客户端。 这项工作应该已经由 <see cref="Deploy()"/> 或 <see cref="DeployAsync()"/>完成
        /// </summary>
        public Task InitializeClientAsync() => InitializeClientAsync(CancellationToken.None);

        /// <summary>
        /// 初始化 grain 客户端。
        /// </summary>
        /// <param name="cancellationToken">取消初始化所用的标记。</param>
        public async Task InitializeClientAsync(CancellationToken cancellationToken)
        {
            WriteLog("Initializing Cluster Client");

            if (ClientHost is not null)
            {
                await StopClusterClientAsync(cancellationToken);
            }

            var configurationBuilder = new ConfigurationBuilder();
            foreach (var source in ConfigurationSources)
            {
                configurationBuilder.Add(source);
            }
            if (options.UseTestClusterMembership)
            {
                var gateways = Silos
                    .Where(silo => silo.IsActive && silo.GatewayAddress?.Endpoint is { Port: > 0 })
                    .Select(silo => new IPEndPoint(silo.GatewayAddress.Endpoint.Address, silo.GatewayAddress.Endpoint.Port))
                    .Distinct()
                    .ToList();

                if (gateways.Count == 0)
                {
                    if (options.GatewayPerSilo)
                    {
                        gateways.AddRange(Enumerable.Range(options.BaseGatewayPort, options.InitialSilosCount).Select(port => new IPEndPoint(IPAddress.Loopback, port)));
                    }
                    else
                    {
                        gateways.Add(new IPEndPoint(IPAddress.Loopback, options.BaseGatewayPort));
                    }
                }

                var clustering = new Dictionary<string, string?>();
                var i = 0;
                foreach (var v in gateways)
                {
                    clustering[$"Orleans:Clustering:Gateways:{i++}"] = v.ToString();
                }

                clustering["Orleans:Clustering:ProviderType"] = "Development";
                configurationBuilder.AddInMemoryCollection(clustering);
            }

            var configuration = configurationBuilder.Build();

            var clientHost = TestClusterHostFactory.CreateClusterClient(
                "MainClient",
                configuration,
                hostBuilder =>
                {
                    hostBuilder.UseOrleansClient((context, clientBuilder) =>
                    {
                        Enum.TryParse<ConnectionTransportType>(context.Configuration[nameof(TestClusterOptions.ConnectionTransport)], out var transport);
                        switch (transport)
                        {
                            case ConnectionTransportType.TcpSocket:
                                // TCP 默认使用
                                break;
                            case ConnectionTransportType.InMemory:
                                {
                                    clientBuilder.UseInMemoryTransport(_transportHub);
                                    break;
                                }
                            case ConnectionTransportType.UnixSocket:
                                {
                                    clientBuilder.UseUnixSocketConnection();
                                    break;
                                }
                            default:
                                throw new ArgumentException($"Unsupported {nameof(ConnectionTransportType)}: {transport}");
                        }
                    });
                });
            try
            {
                await clientHost.StartAsync(cancellationToken);
                this.ClientHost = clientHost;
            }
            catch
            {
                await DisposeAsync(clientHost);
                throw;
            }
        }

        /// <summary>
        /// 获取配置源。
        /// </summary>
        /// <value>配置源。</value>
        public IReadOnlyList<IConfigurationSource> ConfigurationSources { get; }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            short silosToStart = this.options.InitialSilosCount;

            if (this.options.UseTestClusterMembership)
            {
                this.Primary = await StartSiloAsync(
                    this.startedInstances,
                    this.options,
                    configurationOverrides: null,
                    startSiloOnNewPort: false,
                    cancellationToken);
                silosToStart--;
            }

            if (silosToStart > 0)
            {
                await this.StartAdditionalSilosAsync(
                    silosToStart,
                    startAdditionalSiloOnNewPort: false,
                    cancellationToken);
            }

            WriteLog("Done initializing cluster");

            if (this.options.InitializeClientOnDeploy)
            {
                await InitializeClientAsync(cancellationToken);
            }
        }

        /// <summary>
        /// <see cref="CreateSiloAsync"/>的默认值,它创建了一个新的 silo 控件。
        /// </summary>
        /// <param name="siloName">国名 国 名 国 名 silo.</param>
        /// <param name="configuration">配置。</param>
        /// <returns> silo 控件。</returns>
        public async Task<SiloHandle> DefaultCreateSiloAsync(string siloName, IConfiguration configuration)
            => await DefaultCreateSiloAsync(siloName, configuration, CancellationToken.None);

        /// <summary>
        /// 创建一个新的 silo 控件。
        /// </summary>
        /// <param name="siloName">国名 国 名 国 名 silo.</param>
        /// <param name="configuration">配置。</param>
        /// <param name="cancellationToken">取消时使用的牌点 silo 启动。</param>
        /// <returns> silo 控件。</returns>
        public async Task<SiloHandle> DefaultCreateSiloAsync(
            string siloName,
            IConfiguration configuration,
            CancellationToken cancellationToken)
        {
            return await InProcessSiloHandle.CreateAsync(siloName, configuration, hostBuilder =>
            {
                hostBuilder.UseOrleans((context, siloBuilder) =>
                {
                    Enum.TryParse<ConnectionTransportType>(context.Configuration[nameof(TestClusterOptions.ConnectionTransport)], out var transport);
                    switch (transport)
                    {
                        case ConnectionTransportType.TcpSocket:
                            break;
                        case ConnectionTransportType.InMemory:
                            {
                                siloBuilder.UseInMemoryTransport(_transportHub);
                                break;
                            }
                        case ConnectionTransportType.UnixSocket:
                            {
                                siloBuilder.UseUnixSocketConnection();
                                break;
                            }
                        default:
                            throw new ArgumentException($"Unsupported {nameof(ConnectionTransportType)}: {transport}");
                    }

                    if (options.UseRealEnvironmentStatistics)
                    {
                        var descriptor = siloBuilder.Services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(IEnvironmentStatisticsProvider));
                        if (descriptor != null)
                        {
                            siloBuilder.Services.Remove(descriptor);
                            siloBuilder.Services.AddSingleton<IEnvironmentStatisticsProvider, EnvironmentStatisticsProvider>();
                        }
                    }
                });
            }, cancellationToken);
        }

        /// <summary>
        /// 在目标组中开始新 silo
        /// </summary>
        /// <param name="cluster">下 TestCluster 和在 silo 应部署</param>
        /// <param name="instanceNumber">部署的试数</param>
        /// <param name="clusterOptions">使用选项。</param>
        /// <param name="configurationOverrides">配置覆盖。</param>
        /// <param name="startSiloOnNewPort">我们是否在新端口启动此 silo , 而不是默认的端口</param>
        /// <returns>部署的 silo 的手柄</returns>
        public static async Task<SiloHandle> StartSiloAsync(TestCluster cluster, int instanceNumber, TestClusterOptions clusterOptions, IReadOnlyList<IConfigurationSource>? configurationOverrides = null, bool startSiloOnNewPort = false)
        {
            if (cluster == null) throw new ArgumentNullException(nameof(cluster));
            var silo = await cluster.StartSiloAsync(instanceNumber, clusterOptions, configurationOverrides, startSiloOnNewPort);
            lock (cluster.additionalSilos)
            {
                cluster.additionalSilos.Add(silo);
            }

            return silo;
        }

        /// <summary>
        /// 启动新的 silo。
        /// </summary>
        /// <param name="instanceNumber">部署的试数</param>
        /// <param name="clusterOptions">使用选项。</param>
        /// <param name="configurationOverrides">配置覆盖。</param>
        /// <param name="startSiloOnNewPort">我们是否在新端口启动此 silo , 而不是默认的端口</param>
        /// <returns>部署的 silo的把手。</returns>
        public async Task<SiloHandle> StartSiloAsync(int instanceNumber, TestClusterOptions clusterOptions, IReadOnlyList<IConfigurationSource>? configurationOverrides = null, bool startSiloOnNewPort = false)
            => await StartSiloAsync(
                instanceNumber,
                clusterOptions,
                configurationOverrides,
                startSiloOnNewPort,
                CancellationToken.None);

        /// <summary>
        /// 启动新的 silo。
        /// </summary>
        /// <param name="instanceNumber">要部署的试数。</param>
        /// <param name="clusterOptions">使用选项。</param>
        /// <param name="configurationOverrides">配置覆盖。</param>
        /// <param name="startSiloOnNewPort">是否在新端口启动此 silo 。</param>
        /// <param name="cancellationToken">取消时使用的牌点 silo 启动。</param>
        /// <returns>部署的 silo的把手。</returns>
        public async Task<SiloHandle> StartSiloAsync(
            int instanceNumber,
            TestClusterOptions clusterOptions,
            IReadOnlyList<IConfigurationSource>? configurationOverrides,
            bool startSiloOnNewPort,
            CancellationToken cancellationToken)
        {
            var configurationSources = this.ConfigurationSources.ToList();

            // 添加超文本执行
            if (configurationOverrides != null) configurationSources.AddRange(configurationOverrides);
            var siloSpecificOptions = TestSiloSpecificOptions.Create(this, clusterOptions, instanceNumber, startSiloOnNewPort);
            var endpoint = new IPEndPoint(IPAddress.Loopback, siloSpecificOptions.SiloPort);
            bool endpointAdded;
            lock (siloEndpoints)
            {
                endpointAdded = siloEndpoints.Add(endpoint);
            }

            configurationSources.Add(new MemoryConfigurationSource
            {
                InitialData = siloSpecificOptions.ToDictionary()
            });

            var configurationBuilder = new ConfigurationBuilder();
            foreach (var source in configurationSources)
            {
                configurationBuilder.Add(source);
            }
            var configuration = configurationBuilder.Build();
            try
            {
                var handle = await _createSiloAsyncWithCancellation(
                    siloSpecificOptions.SiloName,
                    configuration,
                    cancellationToken);
                handle.InstanceNumber = (short)instanceNumber;
                Interlocked.Increment(ref this.startedInstances);
                return handle;
            }
            catch
            {
                if (endpointAdded)
                {
                    lock (siloEndpoints)
                    {
                        siloEndpoints.Remove(endpoint);
                    }
                }

                throw;
            }
        }

        private async Task StopSiloAsync(SiloHandle instance, bool stopGracefully, CancellationToken cancellationToken)
        {
            try
            {
                await instance.StopSiloAsync(stopGracefully, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await DisposeAsync(instance).ConfigureAwait(false);

                Interlocked.Decrement(ref this.startedInstances);
            }
        }

        /// <summary>
        /// 拿到日志了
        /// </summary>
        /// <returns>日志内容。</returns>
        public string GetLog()
        {
            return this.log.ToString();
        }

        private void ReportUnobservedException(object? sender, UnhandledExceptionEventArgs eventArgs)
        {
            Exception exception = (Exception)eventArgs.ExceptionObject;
            this.WriteLog("Unobserved exception: {0}", exception);
        }

        private void WriteLog(string format, params object?[] args)
        {
            log.AppendFormat(format + Environment.NewLine, args);
        }

        private void FlushLogToConsole()
        {
            Console.WriteLine(GetLog());
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            await Task.Run(async () =>
            {
                AppDomain.CurrentDomain.UnhandledException -= ReportUnobservedException;
                await DisposeAsync(ClientHost).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                foreach (var handle in SecondarySilos)
                {
                    await DisposeAsync(handle).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }

                if (Primary is not null)
                {
                    await DisposeAsync(Primary).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
                ClientHost = null;

                PortAllocator?.Dispose();
                _grainDirectoryObserver.Dispose();
            });

            _disposed = true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

#pragma warning disable RS0030 // IDisposable requires synchronously completing test cluster cleanup.
            DisposeAsync().AsTask().Wait();
#pragma warning restore RS0030
        }

        private static async Task DisposeAsync(IDisposable? value)
        {
            if (value is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().AsTask().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
            else if (value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
