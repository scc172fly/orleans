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
    /// 使用进程内 Silo 进行 Orleans 本地测试的宿主类。
    /// 在独立应用程序域中运行主 Silo 及可选的辅助 Silo，并在主应用程序域中运行客户端。
    /// 特定测试用例需要时，也可以按需在进程内启动额外 Silo。
    /// </summary>
    /// <remarks>
    /// 确保测试项目引用测试 Grain 和测试 Grain 接口
    /// 项目，并在这些引用上设置 CopyLocal=True（这应为默认值）。
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
        /// 主 Silo 的句柄（如适用）。
        /// </summary>
        /// <remarks>仅在使用基于 Grain 的成员资格机制时，此句柄才有效。</remarks>
        public SiloHandle? Primary { get; private set; }

        /// <summary>
        /// 辅助 Silo 的句柄列表。
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
        /// 所有已知 Silo 的集合。
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
        /// 用于配置测试集群的选项。
        /// </summary>
        /// <remarks>这是配置测试集群时使用的选项，或默认选项。
        /// 如果通过 ClusterConfiguration 配置集群，则此对象可能不反映实际设置。
        /// </remarks>
        public TestClusterOptions Options => this.options;

        /// <summary>
        /// 返回地址是否属于此集群。
        /// </summary>
        /// <param name="siloAddress">Silo 地址。</param>
        /// <returns>当地址属于此集群时为 <see langword="true"/>。</returns>
        public bool ContainsSilo(SiloAddress siloAddress)
        {
            ArgumentNullException.ThrowIfNull(siloAddress);
            lock (siloEndpoints)
            {
                return siloEndpoints.Contains(siloAddress.Endpoint);
            }
        }

        /// <summary>
        /// 内部客户端接口。（译注：此属性实际为承载客户端的 IHost。）
        /// </summary>
        internal IHost? ClientHost { get; private set; }

        /// <summary>
        /// 内部客户端接口。
        /// </summary>
        internal IInternalClusterClient? InternalClient => ClientHost?.Services.GetRequiredService<IInternalClusterClient>();

        /// <summary>
        /// 客户端。
        /// </summary>
        /// <exception cref="InvalidOperationException">集群尚未部署，或客户端已停止。</exception>
        public IClusterClient Client => this.InternalClient ?? throw new InvalidOperationException(
            "The test cluster client is unavailable because the cluster has not been deployed or has been stopped.");

        /// <summary>
        /// 测试中使用的 GrainFactory。
        /// </summary>
        public IGrainFactory GrainFactory => this.Client;

        /// <summary>
        /// 测试中使用的 GrainFactory。
        /// </summary>
        internal IInternalGrainFactory? InternalGrainFactory => this.InternalClient;

        /// <summary>
        /// 测试中使用的客户端 <see cref="IServiceProvider"/>。
        /// </summary>
        public IServiceProvider ServiceProvider => this.Client.ServiceProvider;

        /// <summary>
        /// 用于创建并启动单个 Silo 的委托。
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
        /// 用于创建并启动单个 Silo 且支持取消的委托。
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
        /// 端口分配器。
        /// </summary>
        public ITestClusterPortAllocator PortAllocator { get; }

        /// <summary>
        /// 在进程内配置测试集群及客户端。
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
        /// 返回 <see cref="IServiceProvider"/>，它与给定的 <paramref name="silo"/> 关联。
        /// </summary>
        /// <param name="silo">要获取其服务提供程序的 Silo 进程。</param>
        /// <remarks>如果 <paramref name="silo"/> 为 <see langword="null"/>，则随机选择一个现有 Silo。</remarks>
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
        /// 尝试查找 Grain 的 <see cref="IGrainContext"/>，该 Grain 具有指定的 <paramref name="grainId"/>，
        /// 搜索范围为集群中的所有 Silo。
        /// </summary>
        /// <param name="grainId">要查找的 Grain 的 ID。</param>
        /// <param name="grainContext">此方法返回时，如果找到 Grain，则包含其上下文；否则为 <see langword="null"/>。</param>
        /// <returns><see langword="true"/> 表示在某个 Silo 中找到了 Grain；否则为 <see langword="false"/>。</returns>
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
        /// 获取 <see cref="Task"/>，在指定 Grain 的当前激活实例
        /// 完成停用时完成。如果 Grain 当前未激活，则返回 <see cref="Task.CompletedTask"/>。
        /// </summary>
        /// <param name="grainId">要观察的 Grain 的 ID。</param>
        /// <returns>在 Grain 的当前激活实例停用后完成的任务。</returns>
        /// <remarks>
        /// 某些场景可能需要在测试中调用 DeactivateOnIdle/MigrateOnIdle 之前获取此任务，
        /// 以避免在取得返回任务之前 Grain 已停用并重新激活的竞态条件。
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
        /// 停用指定 Grain 的当前激活实例，并等待停用完成。
        /// </summary>
        /// <param name="grainId">要停用的 Grain 的 ID。</param>
        /// <returns>Grain 停用后完成的任务。</returns>
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
        /// 将指定 Grain 的当前激活实例迁移到其他 Silo，并等待迁移完成。
        /// 如果指定了 <paramref name="targetSilo"/>，则设置放置提示，引导迁移到该 Silo。
        /// </summary>
        /// <param name="grainId">要迁移的 Grain 的 ID。</param>
        /// <param name="targetSilo">目标 Silo 地址；若让放置决策器选择，则为 <see langword="null"/>。</param>
        /// <returns>Grain 迁移后（在源 Silo 上停用后）完成的任务。</returns>
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
        /// <param name="grain">要观察的 Grain。</param>
        public Task WaitForDeactivationAsync(IAddressable grain) => WaitForDeactivationAsync(grain.GetGrainId());

        /// <inheritdoc cref="DeactivateAsync(GrainId)"/>
        /// <param name="grain">要停用的 Grain。</param>
        public Task DeactivateAsync(IAddressable grain) => DeactivateAsync(grain.GetGrainId());

        /// <inheritdoc cref="MigrateAsync(GrainId, SiloAddress?)"/>
        /// <param name="grain">要迁移的 Grain。</param>
        /// <param name="targetSilo">目标 Silo 地址；若让放置决策器选择，则为 <see langword="null"/>。</param>
        public Task MigrateAsync(IAddressable grain, SiloAddress? targetSilo = null) => MigrateAsync(grain.GetGrainId(), targetSilo);

        /// <summary>
        /// 使用指定的配置部署集群，并在进程内启动客户端。
        /// 启动 <see cref="TestClusterOptions.InitialSilosCount"/> 中定义数量的 Silo。
        /// </summary>
        public void Deploy()
        {
            this.DeployAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 使用指定的配置部署集群，并在进程内启动客户端。
        /// </summary>
        public Task DeployAsync() => DeployAsync(CancellationToken.None);

        /// <summary>
        /// 使用指定的配置部署集群，并在进程内启动客户端。
        /// </summary>
        /// <param name="cancellationToken">用于取消部署的令牌。</param>
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

                // 重要：
                // 不要在此处重新抛出原始异常，也不要将其作为 AggregateException 的内部异常重新抛出。
                // 由于 Microsoft 测试框架的工作方式，如果原始异常为 Orleans 异常，
                // 其程序集可能在此测试阶段尚未加载。
                // 因此，我们将收到 "MSTest: Unit Test Adapter threw exception: Type is not resolved for member XXX"，
                // 并丢失原始异常。这会使测试调试非常困难！
                // 根本原因与我们在测试构造函数中而不是 TestInitialize 方法中初始化测试有关。
                // 更多详情：http://dobrzanski.net/2010/09/20/mstest-unit-test-adapter-threw-exception-type-is-not-resolved-for-member/
                //throw new Exception(
                //    string.Format("Exception during test initialization: {0}",
                //        LogFormatter.PrintException(baseExc)));
                throw;
            }
        }

        private async Task WaitForInitialStabilization(CancellationToken cancellationToken)
        {
            // 轮询每个 Silo，检查它是否已知预期数量的活动 Silo。
            // 如果任一 Silo 的集群成员资格视图中没有预期数量的活动 Silo，则重试。
            // 如果经过一段时间后集群成员资格仍未稳定，则放弃等待并继续执行。
            var totalWait = Stopwatch.StartNew();
            while (true)
            {
                var silos = this.Silos;
                var expectedCount = silos.Count;
                var remainingSilos = expectedCount;

                foreach (var silo in silos)
                {
                    var hooks = this.InternalClient!.GetTestHooks(silo); // 成员资格稳定过程需要已初始化的客户端。
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
        /// 获取当前活动 Silo 的列表。
        /// </summary>
        /// <returns>当前 Silo 列表。</returns>
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
        /// 查找指定 Silo 地址对应的 Silo 句柄。
        /// </summary>
        /// <param name="siloAddress">要查找的 Silo 地址。</param>
        /// <returns>对应 Silo 的 SiloHandle；若未找到则为 <c>null</c>。</returns>
        public SiloHandle? GetSiloForAddress(SiloAddress siloAddress)
        {
            var activeSilos = GetActiveSilos().ToList();
            var ret = activeSilos.Find(s => s.SiloAddress.Equals(siloAddress));
            return ret;
        }

        /// <summary>
        /// 等待 Silo 存活检测子系统发现并处理近期的集群成员资格变化。
        /// </summary>
        /// <param name="didKill">近期成员资格变化是否由正常 Stop 操作产生。</param>
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
        /// 等待活动 Silo 观测到所有活动 Silo 的集群清单更新。
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
        /// 获取等待 Silo 存活检测子系统发现并处理近期集群成员资格变化时使用的超时值。
        /// <seealso cref="WaitForLivenessToStabilizeAsync"/>
        /// </summary>
        public static TimeSpan GetLivenessStabilizationTime(ClusterMembershipOptions clusterMembershipOptions, bool didKill = false)
        {
            TimeSpan stabilizationTime = TimeSpan.Zero;
            if (didKill)
            {
                // 如果采用强制终止（Kill 而不是 Stop），应先给 Silo 留出检测故障的时间。
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
        /// 启动额外的 Silo，使其加入现有集群。
        /// </summary>
        /// <returns>新启动的 Silo 的 SiloHandle。</returns>
        public SiloHandle StartAdditionalSilo(bool startAdditionalSiloOnNewPort = false)
        {
            return StartAdditionalSiloAsync(startAdditionalSiloOnNewPort).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 启动额外的 Silo，使其加入现有集群。
        /// </summary>
        /// <returns>新启动的 Silo 的 SiloHandle。</returns>
        public async Task<SiloHandle> StartAdditionalSiloAsync(bool startAdditionalSiloOnNewPort = false)
        {
            return (await this.StartAdditionalSilosAsync(1, startAdditionalSiloOnNewPort)).Single();
        }

        /// <summary>
        /// 启动一定数量的额外 Silo，使其加入现有集群。
        /// </summary>
        /// <param name="silosToStart">要启动的 Silo 数量。</param>
        /// <param name="startAdditionalSiloOnNewPort"></param>
        /// <returns>新启动 Silo 的 SiloHandle 列表。</returns>
        public async Task<List<SiloHandle>> StartAdditionalSilosAsync(int silosToStart, bool startAdditionalSiloOnNewPort = false)
            => await StartAdditionalSilosAsync(
                silosToStart,
                startAdditionalSiloOnNewPort,
                CancellationToken.None);

        /// <summary>
        /// 启动一定数量的额外 Silo，使其加入现有集群。
        /// </summary>
        /// <param name="silosToStart">要启动的 Silo 数量。</param>
        /// <param name="startAdditionalSiloOnNewPort">是否在新端口上启动每个 Silo。</param>
        /// <param name="cancellationToken">用于取消 Silo 启动的令牌。</param>
        /// <returns>新启动 Silo 的句柄列表。</returns>
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
        /// 停止所有额外 Silo，不包括默认的主 Silo。
        /// </summary>
        public Task StopSecondarySilosAsync() => StopSecondarySilosAsync(CancellationToken.None);

        /// <summary>
        /// 停止所有额外 Silo，不包括默认的主 Silo。
        /// </summary>
        /// <param name="cancellationToken">用于取消关闭过程的令牌。</param>
        public async Task StopSecondarySilosAsync(CancellationToken cancellationToken)
        {
            foreach (var instance in this.additionalSilos.ToList())
            {
                await StopSiloAsync(instance, cancellationToken);
            }
        }

        /// <summary>
        /// 停止默认的主 Silo。
        /// </summary>
        public Task StopPrimarySiloAsync() => StopPrimarySiloAsync(CancellationToken.None);

        /// <summary>
        /// 停止默认的主 Silo。
        /// </summary>
        /// <param name="cancellationToken">用于取消关闭过程的令牌。</param>
        public async Task StopPrimarySiloAsync(CancellationToken cancellationToken)
        {
            if (Primary == null) throw new InvalidOperationException("There is no primary silo");
            await StopClusterClientAsync(cancellationToken);
            await StopSiloAsync(Primary, cancellationToken);
        }

        /// <summary>
        /// 以异步操作停止集群客户端。
        /// </summary>
        /// <returns>表示异步操作的 <see cref="Task"/>。</returns>
        public Task StopClusterClientAsync() => StopClusterClientAsync(CancellationToken.None);

        /// <summary>
        /// 以异步操作停止集群客户端。
        /// </summary>
        /// <param name="cancellationToken">用于取消关闭过程的令牌。</param>
        /// <returns>表示异步操作的 <see cref="Task"/>。</returns>
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
        /// 停止当前所有 Silo。
        /// </summary>
        public void StopAllSilos()
        {
            StopAllSilosAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 停止当前所有 Silo。
        /// </summary>
        public Task StopAllSilosAsync() => StopAllSilosAsync(CancellationToken.None);

        /// <summary>
        /// 停止当前所有 Silo。
        /// </summary>
        /// <param name="cancellationToken">用于取消关闭过程的令牌。</param>
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
        /// 对指定 Silo 执行半优雅停止操作。
        /// </summary>
        /// <param name="instance">要停止的 Silo。</param>
        public Task StopSiloAsync(SiloHandle instance) => StopSiloAsync(instance, CancellationToken.None);

        /// <summary>
        /// 对指定 Silo 执行半优雅停止操作。
        /// </summary>
        /// <param name="instance">要停止的 Silo。</param>
        /// <param name="cancellationToken">用于取消关闭过程的令牌。</param>
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
        /// 立即强制终止指定 Silo。
        /// </summary>
        /// <param name="instance">要强制终止的 Silo。</param>
        public Task KillSiloAsync(SiloHandle instance) => KillSiloAsync(instance, CancellationToken.None);

        /// <summary>
        /// 立即强制终止指定 Silo。
        /// </summary>
        /// <param name="instance">要强制终止的 Silo。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        public async Task KillSiloAsync(SiloHandle instance, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (instance != null)
            {
                try
                {
                    // 不要正常停止，直接强制终止，以模拟崩溃。
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
        /// 强制终止客户端。客户端不会清理资源。
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
        /// 对指定 Silo 执行 Stop 或 Kill 操作，随后重启。
        /// </summary>
        /// <param name="instance">要重启的 Silo。</param>
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
        /// 重启此前已停止的 Silo。
        /// </summary>
        /// <param name="siloName">要重启的 Silo。</param>
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
        /// 初始化 Grain 客户端。此操作应已由 <see cref="Deploy()"/> 或 <see cref="DeployAsync()"/> 完成。
        /// </summary>
        public Task InitializeClientAsync() => InitializeClientAsync(CancellationToken.None);

        /// <summary>
        /// 初始化 Grain 客户端。
        /// </summary>
        /// <param name="cancellationToken">用于取消初始化的令牌。</param>
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
                                // 默认使用 TCP。
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
        /// <see cref="CreateSiloAsync"/> 的默认值，用于创建新的 Silo 句柄。
        /// </summary>
        /// <param name="siloName">Silo 名称。</param>
        /// <param name="configuration">配置。</param>
        /// <returns>Silo 句柄。</returns>
        public async Task<SiloHandle> DefaultCreateSiloAsync(string siloName, IConfiguration configuration)
            => await DefaultCreateSiloAsync(siloName, configuration, CancellationToken.None);

        /// <summary>
        /// 创建新的 Silo 句柄。
        /// </summary>
        /// <param name="siloName">Silo 名称。</param>
        /// <param name="configuration">配置。</param>
        /// <param name="cancellationToken">用于取消 Silo 启动的令牌。</param>
        /// <returns>Silo 句柄。</returns>
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
        /// 在目标集群中启动一个新 Silo。
        /// </summary>
        /// <param name="cluster">应在其中部署 Silo 的 TestCluster。</param>
        /// <param name="instanceNumber">要部署的实例编号。</param>
        /// <param name="clusterOptions">要使用的选项。</param>
        /// <param name="configurationOverrides">配置覆盖项。</param>
        /// <param name="startSiloOnNewPort">是否在新端口而非默认端口上启动此 Silo。</param>
        /// <returns>已部署 Silo 的句柄。</returns>
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
        /// 启动新的 Silo。
        /// </summary>
        /// <param name="instanceNumber">要部署的实例编号。</param>
        /// <param name="clusterOptions">要使用的选项。</param>
        /// <param name="configurationOverrides">配置覆盖项。</param>
        /// <param name="startSiloOnNewPort">是否在新端口而非默认端口上启动此 Silo。</param>
        /// <returns>已部署 Silo 的句柄。</returns>
        public async Task<SiloHandle> StartSiloAsync(int instanceNumber, TestClusterOptions clusterOptions, IReadOnlyList<IConfigurationSource>? configurationOverrides = null, bool startSiloOnNewPort = false)
            => await StartSiloAsync(
                instanceNumber,
                clusterOptions,
                configurationOverrides,
                startSiloOnNewPort,
                CancellationToken.None);

        /// <summary>
        /// 启动新的 Silo。
        /// </summary>
        /// <param name="instanceNumber">要部署的实例编号。</param>
        /// <param name="clusterOptions">要使用的选项。</param>
        /// <param name="configurationOverrides">配置覆盖项。</param>
        /// <param name="startSiloOnNewPort">是否在新端口上启动此 Silo。</param>
        /// <param name="cancellationToken">用于取消 Silo 启动的令牌。</param>
        /// <returns>已部署 Silo 的句柄。</returns>
        public async Task<SiloHandle> StartSiloAsync(
            int instanceNumber,
            TestClusterOptions clusterOptions,
            IReadOnlyList<IConfigurationSource>? configurationOverrides,
            bool startSiloOnNewPort,
            CancellationToken cancellationToken)
        {
            var configurationSources = this.ConfigurationSources.ToList();

            // 添加配置覆盖项。
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
        /// 获取日志。
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

#pragma warning disable RS0030 // IDisposable 要求同步完成测试集群的清理。
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
