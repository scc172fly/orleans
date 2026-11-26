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
/// 本地测试的主机舱 Orleans 正在使用中处理 silos.
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
    /// 收集所有已知的 silos。
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
    /// 用于配置测试组集的选项。
    /// </summary>
    /// <remarks>这是您配置的测试组选项, 或者默认的选项 。
    /// 如果该组是通过 ClusterConfiguration配置的,则此对象可能不会反映真实设置。
    /// </remarks>
    public InProcessTestClusterOptions Options { get; }

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
    public IClusterClient Client => InternalClient ?? throw new InvalidOperationException(
        "The test cluster client is unavailable because the cluster has not been deployed or has been stopped.");

    /// <summary>
    /// 港口分配器
    /// </summary>
    public ITestClusterPortAllocator PortAllocator { get; }

    /// <summary>
    /// 返回地址是否属于此组 。
    /// </summary>
    /// <param name="siloAddress"> silo 地址。</param>
    /// 当地址属于本组时,<returns><see langword="true"/> 。</returns>
    public bool ContainsSilo(SiloAddress siloAddress)
    {
        ArgumentNullException.ThrowIfNull(siloAddress);
        lock (_siloEndpoints)
        {
            return _siloEndpoints.Contains(siloAddress.Endpoint);
        }
    }

    /// <summary>
    /// 配置测试组群集加客户端正在处理中 。
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
    /// 返回 <see cref="IServiceProvider"/> 与给定的 <paramref name="silo"/>.
    /// </summary>
    /// <param name="silo">下 silo 向服务提供方发送进程。</param>
    /// <remarks>如果 <paramref name="silo"/> 是 <see langword="null"/> 现有 silos 将随机选择。</remarks>
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
        if (TryGetGrainContext(grainId, out var grainContext))
        {
            grainContext!.Deactivate(new DeactivationReason(DeactivationReasonCode.ApplicationRequested, $"{nameof(DeactivateAsync)} was called."));
            await grainContext.Deactivated;
        }
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

        await Client.GetGrain(grainId).Cast<IGrainManagementExtension>().MigrateOnIdle(CancellationToken.None);
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
    /// </summary>
    public Task DeployAsync() => DeployAsync(CancellationToken.None);

    /// <summary>
    /// 使用指定的配置部署组群,并启动客户端的处理。
    /// </summary>
    /// <param name="cancellationToken">用于取消部署的标记。</param>
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
            var silos = Silos;
            var expectedCount = silos.Count;
            var remainingSilos = expectedCount;

            foreach (var silo in silos)
            {
                var hooks = InternalClient!.GetTestHooks(silo); // 成员稳定化需要部署的客户。
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
    /// 查找 silo 指定控件的控件 silo 地址。
    /// </summary>
    /// <param name="siloAddress">Silo 要找到地址 。</param>
    /// <returns>SiloHandle 适当 silo,或 <c>null</c> 找不到的话。</returns>
    public InProcessSiloHandle? GetSiloForAddress(SiloAddress siloAddress)
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
    /// 等待活动 silos 对所有活动 silos的集散清单进行观察更新。
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
        // 使用两个配置的 LAPL 活度间隔来计算 。 在默认八类流言配置下, 此
        // 包含5秒的提取数据重新试用,加上最后快照检查的一个排程间隔。
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
    /// 描述 silo最近一份清单更新尝试:它调和的会员版本
    /// 与它等待的同侪们,
    /// 最近抓取失败, 如果有的话。 用于诊断聚集中最显著的合并超时 。
    /// </summary>
    /// <remarks><see langword="internal"/> (而不是 <see langword="private"/>) 仅如此,其精确输出
    /// 格式,可直接从 <c>Orleans.Core.Tests</c> 无需整个过程
    /// 集束体聚合超时。</remarks>
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

        // 使用观察员 silo自己的钟表,该钟表可在测试中虚拟化,这样过的时间被测量
        // 与 AttemptStartedAt 相一致(通过同一 silo的 TimeProvider加以标 )。
        var elapsed = observerUtcNow - diagnostics.AttemptStartedAt;
        var pending = string.Join(", ", diagnostics.PendingSilos);
        var failure = diagnostics.LastFailedSilo is { } failedSilo
            ? $", last fetch failure for {failedSilo} at {diagnostics.LastFailureAt:O}: {diagnostics.LastFailureMessage}"
            : string.Empty;
        return $"{observer}: membership v{diagnostics.MembershipVersion}, published v{manifest.Version}, "
            + $"pending=[{pending}], attempt started {elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s ago{failure}";
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
    public InProcessSiloHandle StartAdditionalSilo()
    {
        return StartAdditionalSiloAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 启动一个额外的 silo,以便加入现有组群。
    /// </summary>
    /// <returns>SiloHandle 新建的 silo.</returns>
    public async Task<InProcessSiloHandle> StartAdditionalSiloAsync()
    {
        return (await StartSilosAsync(1)).Single();
    }

    /// <summary>
    /// 启动一个额外的 silo,以便加入现有组群。
    /// </summary>
    /// <returns>SiloHandle 新建的 silo.</returns>
    [Obsolete("Use overload which does not have a 'startAdditionalSiloOnNewPort' parameter.")]
    public InProcessSiloHandle StartAdditionalSilo(bool startAdditionalSiloOnNewPort)
    {
        return StartAdditionalSiloAsync(startAdditionalSiloOnNewPort).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 启动一个额外的 silo,以便加入现有组群。
    /// </summary>
    /// <returns>SiloHandle 新建的 silo.</returns>
    public async Task<InProcessSiloHandle> StartAdditionalSiloAsync(bool startAdditionalSiloOnNewPort)
    {
        return (await StartSilosAsync(1)).Single();
    }

    /// <summary>
    /// 启动若干额外的 silo,以便加入现有组群。
    /// </summary>
    /// <param name="silosToStart">数目数量 silos 开始。</param>
    /// <param name="startAdditionalSiloOnNewPort"></param>
    /// <returns>名单清单 SiloHandles 新建的 silos.</returns>
    [Obsolete("Use overload which does not have a 'startAdditionalSiloOnNewPort' parameter.")]
    public async Task<List<InProcessSiloHandle>> StartSilosAsync(int silosToStart, bool startAdditionalSiloOnNewPort)
    {
        return await StartSilosAsync(silosToStart);
    }

    /// <summary>
    /// 启动若干额外的 silo,以便加入现有组群。
    /// </summary>
    /// <param name="silosToStart">数目数量 silos 开始。</param>
    /// <returns>名单清单 SiloHandles 新建的 silos.</returns>
    public Task<List<InProcessSiloHandle>> StartSilosAsync(int silosToStart)
        => StartSilosAsync(silosToStart, CancellationToken.None);

    /// <summary>
    /// 启动若干额外的 silos,以便加入现有组群。
    /// </summary>
    /// <param name="silosToStart">数目数量 silos 开始。</param>
    /// <param name="cancellationToken">取消时使用的牌点 silo 启动。</param>
    /// <returns>列表 silo 中新启动的 silos的控件</returns>。
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
    /// 停止全部 silos。
    /// </summary>
    public Task StopSilosAsync() => StopSilosAsync(CancellationToken.None);

    /// <summary>
    /// 停止全部 silos。
    /// </summary>
    /// <param name="cancellationToken">取消关闭时使用的标记。</param>
    public async Task StopSilosAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in _silos.ToList())
        {
            await StopSiloAsync(instance, cancellationToken);
        }
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
            await StopSilosAsync(cancellationToken);
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
    public Task StopSiloAsync(InProcessSiloHandle instance) => StopSiloAsync(instance, CancellationToken.None);

    /// <summary>
    /// 执行指定的 silo半可观停止。
    /// </summary>
    /// <param name="instance">Silo 以停止。</param>
    /// <param name="cancellationToken">取消关闭时使用的标记。</param>
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
    /// 立即杀死指定 silo。
    /// </summary>
    /// <param name="instance">Silo 被杀死</param>
    public Task KillSiloAsync(InProcessSiloHandle instance) => KillSiloAsync(instance, CancellationToken.None);

    /// <summary>
    /// 立即杀死指定 silo。
    /// </summary>
    /// <param name="instance">Silo 被杀死</param>
    /// <param name="cancellationToken">取消操作所用的标记。</param>
    public async Task KillSiloAsync(InProcessSiloHandle instance, CancellationToken cancellationToken)
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
                lock (_silos)
                {
                    _silos.Remove(instance);
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
    /// 重新启动先前已停止的状态 。
    /// </summary>
    /// <param name="siloName">Silo 要重新开始。</param>
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
    /// 初始化 grain 客户端。 这项工作应该已经由 <see cref="DeployAsync()"/>完成
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
    /// 创建并启动一个进程 silo。
    /// </summary>
    /// <param name="siloOptions"> silo 选项。</param>
    /// <returns>启动 silo的控点。</returns>
    public Task<InProcessSiloHandle> CreateSiloAsync(InProcessTestSiloSpecificOptions siloOptions)
        => CreateSiloAsync(siloOptions, CancellationToken.None);

    /// <summary>
    /// 创建并启动一个进程 silo。
    /// </summary>
    /// <param name="siloOptions"> silo 选项。</param>
    /// <param name="cancellationToken">取消时使用的牌点 silo 启动。</param>
    /// <returns>启动 silo的控点。</returns>
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
                    // 测试正在内部调试器内运行 - 让超时 *\\ 无限
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
    /// 在目标组中开始新 silo
    /// </summary>
    /// <param name="cluster">下 InProcessTestCluster 和在 silo 应部署</param>
    /// <param name="instanceNumber">部署的试数</param>
    /// <param name="clusterOptions">使用选项。</param>
    /// <returns>部署的 silo 的手柄</returns>
    public static async Task<InProcessSiloHandle> StartSiloAsync(InProcessTestCluster cluster, int instanceNumber, InProcessTestClusterOptions clusterOptions)
    {
        if (cluster == null) throw new ArgumentNullException(nameof(cluster));
        return await cluster.StartSiloAsync(instanceNumber, clusterOptions);
    }

    /// <summary>
    /// 在目标组中开始新 silo
    /// </summary>
    /// <param name="cluster">下 InProcessTestCluster 和在 silo 应部署</param>
    /// <param name="instanceNumber">部署的试数</param>
    /// <param name="clusterOptions">使用选项。</param>
    /// <param name="configurationOverrides">配置覆盖。</param>
    /// <param name="startSiloOnNewPort">我们是否在新端口启动此 silo , 而不是默认的端口</param>
    /// <returns>部署的 silo 的手柄</returns>
    [Obsolete("Use the overload which does not have a 'startSiloOnNewPort' parameter.")]
    public static async Task<InProcessSiloHandle> StartSiloAsync(InProcessTestCluster cluster, int instanceNumber, InProcessTestClusterOptions clusterOptions, IReadOnlyList<IConfigurationSource> configurationOverrides, bool startSiloOnNewPort)
    {
        if (cluster == null) throw new ArgumentNullException(nameof(cluster));
        return await cluster.StartSiloAsync(instanceNumber, clusterOptions, configurationOverrides, startSiloOnNewPort);
    }

    /// <summary>
    /// 启动新的 silo。
    /// </summary>
    /// <param name="instanceNumber">部署的试数</param>
    /// <param name="clusterOptions">使用选项。</param>
    /// <returns>部署的 silo的把手。</returns>
    public Task<InProcessSiloHandle> StartSiloAsync(int instanceNumber, InProcessTestClusterOptions clusterOptions)
        => StartSiloAsync(instanceNumber, clusterOptions, CancellationToken.None);

    /// <summary>
    /// 启动新的 silo。
    /// </summary>
    /// <param name="instanceNumber">要部署的试数。</param>
    /// <param name="clusterOptions">使用选项。</param>
    /// <param name="cancellationToken">取消时使用的牌点 silo 启动。</param>
    /// <returns>部署的 silo的把手。</returns>
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
    /// 启动新的 silo。
    /// </summary>
    /// <param name="instanceNumber">部署的试数</param>
    /// <param name="clusterOptions">使用选项。</param>
    /// <param name="configurationOverrides">配置覆盖。</param>
    /// <param name="startSiloOnNewPort">我们是否在新端口启动此 silo , 而不是默认的端口</param>
    /// <returns>部署的 silo的把手。</returns>
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
    /// 拿到日志了
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
