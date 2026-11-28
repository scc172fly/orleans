using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.Dashboard.Metrics;
using Orleans.Dashboard.Model;
using Orleans.Dashboard.Core;

namespace Orleans.Dashboard.Implementation;

internal sealed partial class SiloGrainService : GrainService, ISiloGrainService
{
    private const int DefaultTimerIntervalMs = 1000; // 1 秒
    private readonly Queue<SiloRuntimeStatistics?> _statistics;
    private readonly Dictionary<string, StatCounter> _counters = [];
    private readonly DashboardOptions _options;
    private readonly IGrainProfiler _profiler;
    private readonly IGrainFactory _grainFactory;
    private readonly ISiloLifecycleSubject _siloLifecycle;
    private readonly ILogger<SiloGrainService> _logger;
    private IGrainTimer? _timer;
    private string? _versionOrleans;
    private string? _versionHost;

    public SiloGrainService(
        GrainId grainId,
        Silo silo,
        ILoggerFactory loggerFactory,
        IGrainProfiler profiler,
        IOptions<DashboardOptions> options,
        IGrainFactory grainFactory,
        ISiloLifecycleSubject siloLifecycle) : base(grainId, silo, loggerFactory)
    {
        _profiler = profiler;
        _options = options.Value;
        _grainFactory = grainFactory;
        _siloLifecycle = siloLifecycle;
        _statistics = new Queue<SiloRuntimeStatistics?>(_options.HistoryLength + 1);
        _logger = loggerFactory.CreateLogger<SiloGrainService>();
    }

    public override async Task Start()
    {
        foreach (var _ in Enumerable.Range(1, _options.HistoryLength))
        {
            _statistics.Enqueue(null);
        }

        var updateInterval = TimeSpan.FromMilliseconds(
            Math.Max(_options.CounterUpdateIntervalMs, DefaultTimerIntervalMs)
        );
        try
        {
            _timer = RegisterGrainTimer(ct => CollectStatistics(true, ct), updateInterval, updateInterval);

            await CollectStatistics(false, StoppedCancellationTokenSource.Token);
        }
        catch (InvalidOperationException)
        {
            LogWarningNotRunningInOrleansRuntime(_logger);
        }

        await base.Start();
    }

    public override Task Stop()
    {
        _timer?.Dispose();
        _timer = null;
        return base.Stop();
    }

    private async Task CollectStatistics(bool canDeactivate, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || StoppedCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var managementGrain = _grainFactory.GetGrain<IManagementGrain>(0);
            var siloAddress = SiloAddress.FromParsableString(this.GetPrimaryKeyString());

            var results = (await managementGrain.GetRuntimeStatistics([siloAddress], cancellationToken)).FirstOrDefault();

            if (cancellationToken.IsCancellationRequested || StoppedCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            _statistics.Enqueue(results);

            while (_statistics.Count > _options.HistoryLength)
            {
                _statistics.Dequeue();
            }
        }
        catch (Exception)
        {
            // 无法获取 Silo 统计数据，它可能已死亡，因此终止该 Grain。
            if (canDeactivate)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    public Task SetVersion(
        string orleans,
        string host,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _versionOrleans = orleans;
        _versionHost = host;

        return Task.CompletedTask;
    }

    public Task<Immutable<Dictionary<string, string?>>> GetExtendedProperties(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new Dictionary<string, string?>
        {
            ["hostVersion"] = _versionHost,
            ["orleansVersion"] = _versionOrleans
        };

        return Task.FromResult(results.AsImmutable());
    }

    public Task ReportCounters(
        Immutable<StatCounter[]> reportCounters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var counter in reportCounters.Value)
        {
            if (!string.IsNullOrWhiteSpace(counter.Name))
            {
                _counters[counter.Name] = counter;
            }
        }

        return Task.CompletedTask;
    }

    public Task<Immutable<SiloRuntimeStatistics?[]>> GetRuntimeStatistics(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_statistics.ToArray().AsImmutable());
    }

    public Task<Immutable<StatCounter[]>> GetCounters(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_counters.Values.OrderBy(x => x.Name).ToArray().AsImmutable());
    }

    public Task<Immutable<LifecycleStageInfo[]>> GetLifecycleStages(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LifecycleStageInspector.GetStages(_siloLifecycle).AsImmutable());
    }

    public Task Enable(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _profiler.Enable(enabled);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Not running in Orleans runtime"
    )]
    private static partial void LogWarningNotRunningInOrleansRuntime(ILogger logger);
}
