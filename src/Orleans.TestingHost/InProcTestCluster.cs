using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
using Orleans.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Orleans.TestingHost.InMemoryTransport;
using System.Net;
using Orleans.Statistics;
using Orleans.TestingHost.InProcess;
using Orleans.Runtime.Hosting;
using Orleans.GrainDirectory;
using Orleans.Messaging;
using Orleans.Hosting;
using Orleans.Runtime.TestHooks;
using Orleans.Configuration.Internal;
using Orleans.TestingHost.Logging;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Metadata;
using Orleans.Metadata;

namespace Orleans.TestingHost;

/// <summary>
/// 使用进程内 Silo 进行 Orleans 本地测试的宿主类。
/// </summary>
public sealed class InProcessTestCluster : IDisposable, IAsyncDisposable
{
    private readonly List<InProcessSiloHandle> _silos = [];
    private readonly HashSet<IPEndPoint> _siloEndpoints = [];
    private readonly StringBuilder _log = new();
    private readonly object _logLock = new();
    private readonly InMemoryTransportConnectionHub _transportHub = new();
    private readonly GrainDirectoryObserver _grainDirectoryObserver = new();
    private readonly InProcessGrainDirectory _grainDirectory;
    private readonly InProcessMembershipTable _membershipTable;
    private bool _disposed;
    private int _startedInstances;

    /// <summary>
    /// 所有已知 Silo 的集合。
    /// </summary>
    public ReadOnlyCollection<InProcessSiloHandle> Silos
    {
        get
        {
            lock (_silos)
            {
                return new List<InProcessSiloHandle>(_silos).AsReadOnly();
            }
        }
    }

    /// <summary>
    /// 用于配置测试集群的选项。
    /// </summary>
    /// <remarks>这是配置测试集群时使用的选项，或默认选项。
    /// 如果通过 ClusterConfiguration 配置集群，则此对象可能不反映实际设置。
    /// </remarks>
    public InProcessTestClusterOptions Options { get; }

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
    public IClusterClient Client => InternalClient ?? throw new InvalidOperationException(
        "The test cluster client is unavailable because the cluster has not been deployed or has been stopped.");

    /// <summary>
    /// 端口分配器。
    /// </summary>
    public ITestClusterPortAllocator PortAllocator { get; }

    /// <summary>
    /// 返回地址是否属于此集群。
    /// </summary>
    /// <param name="siloAddress">Silo 地址。</param>
    /// <returns>当地址属于此集群时为 <see langword="true"/>。</returns>
    public bool ContainsSilo(SiloAddress siloAddress)
    {
        ArgumentNullException.ThrowIfNull(siloAddress);
        lock (_siloEndpoints)
        {
            return _siloEndpoints.Contains(siloAddress.Endpoint);
        }
    }

    /// <summary>
    /// 在进程内配置测试集群及客户端。
    /// </summary>
    public InProcessTestCluster(
        InProcessTestClusterOptions options,
        ITestClusterPortAllocator portAllocator)
    {
        Options = options;
        PortAllocator = portAllocator;
        _membershipTable = new(options.ClusterId);
        _grainDirectory = new(_membershipTable.GetSiloStatus);
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
            return handle != null ? handle.SiloHost.Services :
                throw new ArgumentException($"The provided silo address '{silo}' is unknown.");
        }
        else
        {
            var index = Random.Shared.Next(Silos.Count);
            return Silos[index].SiloHost.Services;
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
            var activationDirectory = silo.SiloHost.Services.GetRequiredService<ActivationDirectory>();
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
        if (TryGetGrainContext(grainId, out var grainContext))
        {
            grainContext!.Deactivate(new DeactivationReason(DeactivationReasonCode.ApplicationRequested, $"{nameof(DeactivateAsync)} was called."));
            await grainContext.Deactivated;
        }
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

        await Client.GetGrain(grainId).Cast<IGrainManagementExtension>().MigrateOnIdle(CancellationToken.None);
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
    /// </summary>
    public Task DeployAsync() => DeployAsync(CancellationToken.None);

    /// <summary>
    /// 使用指定的配置部署集群，并在进程内启动客户端。
    /// </summary>
    /// <param name="cancellationToken">用于取消部署的令牌。</param>
    public async Task DeployAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_silos.Count > 0) throw new InvalidOperationException("Cluster host already deployed.");

        AppDomain.CurrentDomain.UnhandledException += ReportUnobservedException;

        try
        {
            string startMsg = "----------------------------- STARTING NEW UNIT TEST SILO HOST: " + GetType().FullName + " -------------------------------------";
            WriteLog(startMsg);
            await InitializeAsync(cancellationToken);

            if (Options.InitializeClientOnDeploy)
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
            var silos = Silos;
            var expectedCount = silos.Count;
            var remainingSilos = expectedCount;

            foreach (var silo in silos)
            {
                var hooks = InternalClient!.GetTestHooks(silo); // 成员资格稳定过程需要已部署的客户端。
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
    public IEnumerable<InProcessSiloHandle> GetActiveSilos()
    {
        var additional = new List<InProcessSiloHandle>();
        lock (_silos)
        {
            additional.AddRange(_silos);
        }

        WriteLog("GetActiveSilos: {0} Silos={1}",
            additional.Count, Runtime.Utils.EnumerableToString(additional));

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
    public InProcessSiloHandle? GetSiloForAddress(SiloAddress siloAddress)
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
        var clusterMembershipOptions = Client!.ServiceProvider.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value;
        TimeSpan stabilizationTime = GetLivenessStabilizationTime(clusterMembershipOptions, didKill);
        var activeSilos = GetActiveSilos().ToArray();
        var testHooks = activeSilos.Select(static silo => (ITestHooks)silo.ServiceProvider.GetRequiredService<TestHooksSystemTarget>()).ToArray();
        var gatewayManager = Client.ServiceProvider.GetRequiredService<GatewayManager>();
        Func<TimeSpan, Task<bool>>? waitForGrainDirectoryConvergence =
            GrainDirectoryObserver.CanObserve(activeSilos)
                ? timeout => _grainDirectoryObserver.WaitForConvergenceAsync(activeSilos, timeout)
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

    /// <summary>
    /// 等待活动 Silo 观测到所有活动 Silo 的集群清单更新。
    /// </summary>
    public async Task WaitForClusterManifestToStabilizeAsync(bool didKill = false)
    {
        var clusterMembershipOptions = Client!.ServiceProvider.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value;
        var stabilizationTime = GetLivenessStabilizationTime(clusterMembershipOptions, didKill);
        var activeSilos = GetActiveSilos().ToArray();
        var testHooks = activeSilos.Select(static silo => (ITestHooks)silo.ServiceProvider.GetRequiredService<TestHooksSystemTarget>()).ToArray();

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

    internal async Task WaitForTopologyToConvergeAsync(
        CancellationToken cancellationToken,
        bool didKill = false)
    {
        var clusterMembershipOptions = Client!.ServiceProvider
            .GetRequiredService<IOptions<ClusterMembershipOptions>>().Value;
        var timeout = GetLivenessStabilizationTime(clusterMembershipOptions, didKill);
        // 集群清单使用两个已配置的存活检测时间间隔。在默认的 Gossip 配置下，这
        // 涵盖 5 秒的清单获取重试时间，以及最终快照检查的一个调度间隔。
        var manifestTimeout = TestingUtils.Multiply(timeout, 2);
        var activeSilos = GetActiveSilos().ToArray();
        var expectedSilos = string.Join(", ", activeSilos.Select(static silo => silo.SiloAddress));
        var testHooks = activeSilos
            .Select(static silo => (ITestHooks)silo.ServiceProvider.GetRequiredService<TestHooksSystemTarget>())
            .ToArray();
        var manifestProviders = activeSilos
            .Select(static silo => silo.ServiceProvider.GetRequiredService<ClusterManifestProvider>())
            .ToArray();
        var gatewayManager = Client.ServiceProvider.GetRequiredService<GatewayManager>();
        if (!GrainDirectoryObserver.CanObserve(activeSilos))
        {
            throw new InvalidOperationException(
                $"The grain directory cannot report convergence for the expected topology: {expectedSilos}.");
        }

        var topologyConverged = await LivenessStabilizationHelper
            .WaitForExpectedActiveSilosAndGatewaysAsync(
                activeSilos,
                testHooks,
                gatewayManager,
                timeout,
                remaining => _grainDirectoryObserver.WaitForConvergenceAsync(activeSilos, remaining))
            .WaitAsync(cancellationToken);
        if (!topologyConverged)
        {
            throw new TimeoutException(
                $"Membership, gateway, and grain-directory views did not converge within {timeout}. Expected active silos: {expectedSilos}.");
        }

        var manifestConverged = await ClusterManifestStabilizationHelper
            .WaitForExpectedClusterManifestAsync(activeSilos, testHooks, manifestTimeout)
            .WaitAsync(cancellationToken);
        if (!manifestConverged)
        {
            var expectedSiloSet = activeSilos.Select(static silo => silo.SiloAddress).ToHashSet();

            var snapshots = activeSilos.Select((silo, index) =>
            {
                var (manifest, diagnostics) = manifestProviders[index].GetManifestAndDiagnosticsSnapshot();
                var observerTime = silo.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
                return (Silo: silo, Manifest: manifest, Diagnostics: diagnostics, ObserverTime: observerTime);
            }).ToArray();

            var observedManifests = snapshots.Select(entry =>
                $"{entry.Silo.SiloAddress}=[{string.Join(", ", entry.Manifest.Silos.Keys.Order())}]");
            var pendingFetchDiagnostics = snapshots
                .Where(entry => !expectedSiloSet.SetEquals(entry.Manifest.Silos.Keys))
                .Select(entry => DescribePendingFetch(entry.Silo.SiloAddress, entry.Manifest, entry.Diagnostics, entry.ObserverTime));
            throw new TimeoutException(
                $"Cluster manifests did not converge within {manifestTimeout}. Expected active silos: {expectedSilos}. "
                + $"Observed manifests: {string.Join("; ", observedManifests)}. "
                + $"Pending fetch diagnostics: {string.Join("; ", pendingFetchDiagnostics)}.");
        }
    }

    /// <summary>
    /// 描述 Silo 最近一次清单更新尝试：正在协调的成员资格版本、
    /// 仍在等待的对等节点、尝试已经持续的时间，以及最近一次
    /// 获取失败（如果有）。用于诊断集群清单收敛超时。
    /// </summary>
    /// <remarks><see langword="internal"/>（而不是 <see langword="private"/>）仅为了让其精确的输出
    /// 格式能够直接从 <c>Orleans.Core.Tests</c> 中进行单元测试，而无需触发完整的进程内
    /// 集群收敛超时。</remarks>
    internal static string DescribePendingFetch(
        SiloAddress observer,
        ClusterManifest manifest,
        ManifestUpdateAttemptDiagnostics? diagnostics,
        DateTime observerUtcNow)
    {
        if (diagnostics is null)
        {
            return $"{observer}: no manifest-update attempt recorded (published version {manifest.Version})";
        }

        // 使用观察方 Silo 自己的时钟（在测试中可能虚拟化），使经过时间的测量方式
        // 与 AttemptStartedAt 一致（使用同一 Silo 的 TimeProvider 记录）。
        var elapsed = observerUtcNow - diagnostics.AttemptStartedAt;
        var pending = string.Join(", ", diagnostics.PendingSilos);
        var failure = diagnostics.LastFailedSilo is { } failedSilo
            ? $", last fetch failure for {failedSilo} at {diagnostics.LastFailureAt:O}: {diagnostics.LastFailureMessage}"
            : string.Empty;
        return $"{observer}: membership v{diagnostics.MembershipVersion}, published v{manifest.Version}, "
            + $"pending=[{pending}], attempt started {elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s ago{failure}";
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
    public InProcessSiloHandle StartAdditionalSilo()
    {
        return StartAdditionalSiloAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 启动额外的 Silo，使其加入现有集群。
    /// </summary>
    /// <returns>新启动的 Silo 的 SiloHandle。</returns>
    public async Task<InProcessSiloHandle> StartAdditionalSiloAsync()
    {
        return (await StartSilosAsync(1)).Single();
    }

    /// <summary>
    /// 启动额外的 Silo，使其加入现有集群。
    /// </summary>
    /// <returns>新启动的 Silo 的 SiloHandle。</returns>
    [Obsolete("Use overload which does not have a 'startAdditionalSiloOnNewPort' parameter.")]
    public InProcessSiloHandle StartAdditionalSilo(bool startAdditionalSiloOnNewPort)
    {
        return StartAdditionalSiloAsync(startAdditionalSiloOnNewPort).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 启动额外的 Silo，使其加入现有集群。
    /// </summary>
    /// <returns>新启动的 Silo 的 SiloHandle。</returns>
    public async Task<InProcessSiloHandle> StartAdditionalSiloAsync(bool startAdditionalSiloOnNewPort)
    {
        return (await StartSilosAsync(1)).Single();
    }

    /// <summary>
    /// 启动一定数量的额外 Silo，使其加入现有集群。
    /// </summary>
    /// <param name="silosToStart">要启动的 Silo 数量。</param>
    /// <param name="startAdditionalSiloOnNewPort"></param>
    /// <returns>新启动 Silo 的 SiloHandle 列表。</returns>
    [Obsolete("Use overload which does not have a 'startAdditionalSiloOnNewPort' parameter.")]
    public async Task<List<InProcessSiloHandle>> StartSilosAsync(int silosToStart, bool startAdditionalSiloOnNewPort)
    {
        return await StartSilosAsync(silosToStart);
    }

    /// <summary>
    /// 启动一定数量的额外 Silo，使其加入现有集群。
    /// </summary>
    /// <param name="silosToStart">要启动的 Silo 数量。</param>
    /// <returns>新启动 Silo 的 SiloHandle 列表。</returns>
    public Task<List<InProcessSiloHandle>> StartSilosAsync(int silosToStart)
        => StartSilosAsync(silosToStart, CancellationToken.None);

    /// <summary>
    /// 启动一定数量的额外 Silo，使其加入现有集群。
    /// </summary>
    /// <param name="silosToStart">要启动的 Silo 数量。</param>
    /// <param name="cancellationToken">用于取消 Silo 启动的令牌。</param>
    /// <returns>新启动 Silo 的句柄列表。</returns>
    public async Task<List<InProcessSiloHandle>> StartSilosAsync(
        int silosToStart,
        CancellationToken cancellationToken)
    {
        var instances = new List<InProcessSiloHandle>();
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
                        Options,
                        cancellationToken),
                    cancellationToken))
                .ToArray();

            try
            {
                await Task.WhenAll(siloStartTasks);
            }
            catch (Exception)
            {
                lock (_silos)
                {
                    _silos.AddRange(
                        siloStartTasks
                            .Where(static task => task.Status == TaskStatus.RanToCompletion)
                            .Select(static task => task.Result));
                }

                throw;
            }

            instances.AddRange(siloStartTasks.Select(t => t.Result));
            lock (_silos)
            {
                _silos.AddRange(instances);
            }
        }

        return instances;
    }

    /// <summary>
    /// 停止所有 Silo。
    /// </summary>
    public Task StopSilosAsync() => StopSilosAsync(CancellationToken.None);

    /// <summary>
    /// 停止所有 Silo。
    /// </summary>
    /// <param name="cancellationToken">用于取消关闭过程的令牌。</param>
    public async Task StopSilosAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in _silos.ToList())
        {
            await StopSiloAsync(instance, cancellationToken);
        }
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
        var client = ClientHost;
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
            await StopSilosAsync(cancellationToken);
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
    public Task StopSiloAsync(InProcessSiloHandle instance) => StopSiloAsync(instance, CancellationToken.None);

    /// <summary>
    /// 对指定 Silo 执行半优雅停止操作。
    /// </summary>
    /// <param name="instance">要停止的 Silo。</param>
    /// <param name="cancellationToken">用于取消关闭过程的令牌。</param>
    public async Task StopSiloAsync(InProcessSiloHandle instance, CancellationToken cancellationToken)
    {
        if (instance != null)
        {
            try
            {
                await StopSiloAsync(instance, true, cancellationToken);
            }
            finally
            {
                lock (_silos)
                {
                    _silos.Remove(instance);
                }
            }
        }
    }

    /// <summary>
    /// 立即强制终止指定 Silo。
    /// </summary>
    /// <param name="instance">要强制终止的 Silo。</param>
    public Task KillSiloAsync(InProcessSiloHandle instance) => KillSiloAsync(instance, CancellationToken.None);

    /// <summary>
    /// 立即强制终止指定 Silo。
    /// </summary>
    /// <param name="instance">要强制终止的 Silo。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    public async Task KillSiloAsync(InProcessSiloHandle instance, CancellationToken cancellationToken)
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
                lock (_silos)
                {
                    _silos.Remove(instance);
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
    public async Task<InProcessSiloHandle?> RestartSiloAsync(InProcessSiloHandle instance)
    {
        if (instance is not null)
        {
            var instanceNumber = instance.InstanceNumber;
            await StopSiloAsync(instance);
            var newInstance = await StartSiloAsync(instanceNumber, Options);
            lock (_silos)
            {
                _silos.Add(newInstance);
            }

            return newInstance;
        }

        return null;
    }

    /// <summary>
    /// 重启此前已停止的 Silo。
    /// </summary>
    /// <param name="siloName">要重启的 Silo。</param>
    public async Task<InProcessSiloHandle> RestartStoppedSecondarySiloAsync(string siloName)
    {
        if (siloName == null) throw new ArgumentNullException(nameof(siloName));
        var siloHandle = Silos.Single(s => s.Name.Equals(siloName, StringComparison.Ordinal));
        var newInstance = await StartSiloAsync(Silos.IndexOf(siloHandle), Options);
        lock (_silos)
        {
            _silos.Add(newInstance);
        }
        return newInstance;
    }

    /// <summary>
    /// 初始化 Grain 客户端。此操作应已由 <see cref="DeployAsync()"/> 完成。
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

        var hostBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            ApplicationName = "TestClusterClient",
            DisableDefaults = true,
        });

        foreach (var hostDelegate in Options.ClientHostConfigurationDelegates)
        {
            hostDelegate(hostBuilder);
        }

        hostBuilder.UseOrleansClient(clientBuilder =>
        {
            clientBuilder.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = Options.ClusterId;
                o.ServiceId = Options.ServiceId;
            });

            if (Options.UseTestClusterMembership)
            {
                clientBuilder.Services.AddSingleton<IGatewayListProvider>(_membershipTable);
            }

            clientBuilder.UseInMemoryTransport(_transportHub);
        });

        TryConfigureFileLogging(Options, hostBuilder.Services, "TestClusterClient");

        var clientHost = hostBuilder.Build();
        try
        {
            await clientHost.StartAsync(cancellationToken);
            ClientHost = clientHost;
        }
        catch
        {
            await DisposeAsync(clientHost);
            throw;
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var silosToStart = Options.InitialSilosCount;

        if (silosToStart > 0)
        {
            await StartSilosAsync(silosToStart, cancellationToken);
        }

        WriteLog("Done initializing cluster");

        if (Options.InitializeClientOnDeploy)
        {
            await InitializeClientAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 创建并启动进程内 Silo。
    /// </summary>
    /// <param name="siloOptions">Silo 选项。</param>
    /// <returns>已启动 Silo 的句柄。</returns>
    public Task<InProcessSiloHandle> CreateSiloAsync(InProcessTestSiloSpecificOptions siloOptions)
        => CreateSiloAsync(siloOptions, CancellationToken.None);

    /// <summary>
    /// 创建并启动进程内 Silo。
    /// </summary>
    /// <param name="siloOptions">Silo 选项。</param>
    /// <param name="cancellationToken">用于取消 Silo 启动的令牌。</param>
    /// <returns>已启动 Silo 的句柄。</returns>
    public async Task<InProcessSiloHandle> CreateSiloAsync(
        InProcessTestSiloSpecificOptions siloOptions,
        CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, siloOptions.SiloPort);
        bool endpointAdded;
        lock (_siloEndpoints)
        {
            endpointAdded = _siloEndpoints.Add(endpoint);
        }

        try
        {
            var host = await Task.Run(async () =>
            {
                var siloName = siloOptions.SiloName;

                var appBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    ApplicationName = siloName,
                    EnvironmentName = Environments.Development,
                    DisableDefaults = true
                });

                var services = appBuilder.Services;
                TryConfigureFileLogging(Options, services, siloName);

                if (Debugger.IsAttached)
                {
                    // 测试在调试器中运行，将超时时间设为近似无限。
                    services.Configure<SiloMessagingOptions>(op => op.ResponseTimeout = TimeSpan.FromMilliseconds(1000000));
                }

                foreach (var hostDelegate in Options.SiloHostConfigurationDelegates)
                {
                    hostDelegate(siloOptions, appBuilder);
                }

                appBuilder.UseOrleans(siloBuilder =>
                {
                    siloBuilder.Configure<ClusterOptions>(o =>
                    {
                        o.ClusterId = Options.ClusterId;
                        o.ServiceId = Options.ServiceId;
                    });

                    siloBuilder.Configure<SiloOptions>(o =>
                    {
                        o.SiloName = siloOptions.SiloName;
                    });

                    siloBuilder.Configure<EndpointOptions>(o =>
                    {
                        o.AdvertisedIPAddress = IPAddress.Loopback;
                        o.SiloPort = siloOptions.SiloPort;
                        o.GatewayPort = siloOptions.GatewayPort;
                    });

                    siloBuilder.Services
                        .Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
                    TestClusterFatalErrorHandler.Configure(siloBuilder.Services);

                    if (Options.UseTestClusterMembership)
                    {
                        services.AddSingleton<IMembershipTable>(_membershipTable);
                        if (!Options.UseDistributedGrainDirectory && Options.UseTestClusterGrainDirectory)
                        {
                            siloBuilder.AddGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, (_, _) => _grainDirectory);
                        }
                    }

                    if (Options.UseDistributedGrainDirectory)
                    {
                        new ConfigureDistributedGrainDirectory().Configure(siloBuilder);
                    }

                    siloBuilder.UseInMemoryTransport(_transportHub);

                    services.AddSingleton<TestHooksEnvironmentStatisticsProvider>();
                    services.AddSingleton<TestHooksSystemTarget>();
                    if (!Options.UseRealEnvironmentStatistics)
                    {
                        services.AddFromExisting<IEnvironmentStatisticsProvider, TestHooksEnvironmentStatisticsProvider>();
                    }
                });

                var host = appBuilder.Build();
                TestClusterFatalErrorHandler.Attach(host);
                InitializeTestHooksSystemTarget(host);
                try
                {
                    await host.StartAsync(cancellationToken);
                    return host;
                }
                catch
                {
                    await DisposeAsync(host);
                    throw;
                }
            }, cancellationToken);

            return new InProcessSiloHandle
            {
                Name = siloOptions.SiloName,
                SiloHost = host,
                SiloAddress = host.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress,
                GatewayAddress = host.Services.GetRequiredService<ILocalSiloDetails>().GatewayAddress,
            };
        }
        catch
        {
            if (endpointAdded)
            {
                lock (_siloEndpoints)
                {
                    _siloEndpoints.Remove(endpoint);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 在目标集群中启动一个新 Silo。
    /// </summary>
    /// <param name="cluster">应在其中部署 Silo 的 InProcessTestCluster。</param>
    /// <param name="instanceNumber">要部署的实例编号。</param>
    /// <param name="clusterOptions">要使用的选项。</param>
    /// <returns>已部署 Silo 的句柄。</returns>
    public static async Task<InProcessSiloHandle> StartSiloAsync(InProcessTestCluster cluster, int instanceNumber, InProcessTestClusterOptions clusterOptions)
    {
        if (cluster == null) throw new ArgumentNullException(nameof(cluster));
        return await cluster.StartSiloAsync(instanceNumber, clusterOptions);
    }

    /// <summary>
    /// 在目标集群中启动一个新 Silo。
    /// </summary>
    /// <param name="cluster">应在其中部署 Silo 的 InProcessTestCluster。</param>
    /// <param name="instanceNumber">要部署的实例编号。</param>
    /// <param name="clusterOptions">要使用的选项。</param>
    /// <param name="configurationOverrides">配置覆盖项。</param>
    /// <param name="startSiloOnNewPort">是否在新端口而非默认端口上启动此 Silo。</param>
    /// <returns>已部署 Silo 的句柄。</returns>
    [Obsolete("Use the overload which does not have a 'startSiloOnNewPort' parameter.")]
    public static async Task<InProcessSiloHandle> StartSiloAsync(InProcessTestCluster cluster, int instanceNumber, InProcessTestClusterOptions clusterOptions, IReadOnlyList<IConfigurationSource> configurationOverrides, bool startSiloOnNewPort)
    {
        if (cluster == null) throw new ArgumentNullException(nameof(cluster));
        return await cluster.StartSiloAsync(instanceNumber, clusterOptions, configurationOverrides, startSiloOnNewPort);
    }

    /// <summary>
    /// 启动新的 Silo。
    /// </summary>
    /// <param name="instanceNumber">要部署的实例编号。</param>
    /// <param name="clusterOptions">要使用的选项。</param>
    /// <returns>已部署 Silo 的句柄。</returns>
    public Task<InProcessSiloHandle> StartSiloAsync(int instanceNumber, InProcessTestClusterOptions clusterOptions)
        => StartSiloAsync(instanceNumber, clusterOptions, CancellationToken.None);

    /// <summary>
    /// 启动新的 Silo。
    /// </summary>
    /// <param name="instanceNumber">要部署的实例编号。</param>
    /// <param name="clusterOptions">要使用的选项。</param>
    /// <param name="cancellationToken">用于取消 Silo 启动的令牌。</param>
    /// <returns>已部署 Silo 的句柄。</returns>
    public async Task<InProcessSiloHandle> StartSiloAsync(
        int instanceNumber,
        InProcessTestClusterOptions clusterOptions,
        CancellationToken cancellationToken)
    {
        var siloOptions = InProcessTestSiloSpecificOptions.Create(this, clusterOptions, instanceNumber, assignNewPort: true);
        var handle = await CreateSiloAsync(siloOptions, cancellationToken);
        handle.InstanceNumber = (short)instanceNumber;
        Interlocked.Increment(ref _startedInstances);
        return handle;
    }

    /// <summary>
    /// 启动新的 Silo。
    /// </summary>
    /// <param name="instanceNumber">要部署的实例编号。</param>
    /// <param name="clusterOptions">要使用的选项。</param>
    /// <param name="configurationOverrides">配置覆盖项。</param>
    /// <param name="startSiloOnNewPort">是否在新端口而非默认端口上启动此 Silo。</param>
    /// <returns>已部署 Silo 的句柄。</returns>
    [Obsolete("Use the overload which does not have a 'startSiloOnNewPort' parameter.")]
    public async Task<InProcessSiloHandle> StartSiloAsync(int instanceNumber, InProcessTestClusterOptions clusterOptions, IReadOnlyList<IConfigurationSource> configurationOverrides, bool startSiloOnNewPort)
    {
        return await StartSiloAsync(instanceNumber, clusterOptions);
    }

    private async Task StopSiloAsync(
        InProcessSiloHandle instance,
        bool stopGracefully,
        CancellationToken cancellationToken)
    {
        try
        {
            await instance.StopSiloAsync(stopGracefully, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync(instance).ConfigureAwait(false);

            Interlocked.Decrement(ref _startedInstances);
        }
    }

    /// <summary>
    /// 获取日志。
    /// </summary>
    /// <returns>日志内容。</returns>
    public string GetLog()
    {
        lock (_logLock)
        {
            return _log.ToString();
        }
    }

    private void ReportUnobservedException(object? sender, UnhandledExceptionEventArgs eventArgs)
    {
        Exception exception = (Exception)eventArgs.ExceptionObject;
        WriteLog("Unobserved exception: {0}", exception);
    }

    private void WriteLog(string format, params object?[] args)
    {
        lock (_logLock)
        {
            _log.AppendFormat(format + Environment.NewLine, args);
        }
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
            foreach (var handle in Silos)
            {
                await DisposeAsync(handle).ConfigureAwait(false);
            }

            await DisposeAsync(ClientHost).ConfigureAwait(false);
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

        foreach (var handle in Silos)
        {
            handle.Dispose();
        }

        ClientHost?.Dispose();
        ClientHost = null;
        PortAllocator?.Dispose();
        _grainDirectoryObserver.Dispose();

        _disposed = true;
    }

    private static async Task DisposeAsync(IDisposable? value)
    {
        if (value is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }

    }
    private static void TryConfigureFileLogging(InProcessTestClusterOptions options, IServiceCollection services, string name)
    {
        if (options.ConfigureFileLogging)
        {
            var fileName = TestingUtils.CreateTraceFileName(name, options.ClusterId);
            services.AddLogging(loggingBuilder => loggingBuilder.AddFile(fileName));
        }
    }

    private static void InitializeTestHooksSystemTarget(IHost host)
    {
        _ = host.Services.GetRequiredService<TestHooksSystemTarget>();
    }
}
