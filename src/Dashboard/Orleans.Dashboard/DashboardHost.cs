using System;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Orleans.Runtime;
using Orleans.Dashboard.Implementation;
using Microsoft.Extensions.Hosting;
using Orleans.Dashboard.Core;

namespace Orleans.Dashboard;

internal sealed partial class DashboardHost(
    ILogger<DashboardHost> logger,
    ILocalSiloDetails localSiloDetails,
    OrleansInstruments instruments,
    IGrainFactory grainFactory,
    DashboardTelemetryExporter dashboardTelemetryExporter,
    ISiloGrainClient siloGrainClient) : IHostedService, ILifecycleParticipant<ISiloLifecycle>, IAsyncDisposable, IDisposable
{
    private MeterProvider? _meterProvider;

    /// <summary>
    /// 获得定期遥测出口之间的间隔。 <see cref="Timeout.Infinite"/> 禁用周期出口,
    /// 所以只有清晰的冲水器 才能进行遥测
    /// </summary>
    internal int ExportIntervalMilliseconds { get; init; } = 1000;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            ActivateDashboardGrainAsync(cancellationToken),
            ActivateSiloGrainAsync(cancellationToken)).ConfigureAwait(false);
    }

    public void Participate(ISiloLifecycle lifecycle)
    {
        // 从门户在活动阶段打开那一刻起,观察客户端连接。
        lifecycle.Subscribe<DashboardHost>(ServiceLifecycleStage.BecomeActive, _ => StartOpenTelemetryConsumerAsync());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ActivateSiloGrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            var siloGrain = siloGrainClient.GrainService(localSiloDetails.SiloAddress);
            await siloGrain.SetVersion(GetOrleansVersion(), GetHostVersion(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogWarningActivateSiloGrainServiceStartupFailed(logger, ex);
        }
    }

    private async Task ActivateDashboardGrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dashboardGrain = grainFactory.GetGrain<IDashboardGrain>(0);
            await dashboardGrain.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogWarningActivateDashboardGrainStartupFailed(logger, ex);
        }
    }

    private Task StartOpenTelemetryConsumerAsync()
    {
        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(instruments.Meter.Name)
            .AddView(instrument =>
            {
                if (!ReferenceEquals(instrument.Meter, instruments.Meter))
                {
                    return MetricStreamConfiguration.Drop;
                }

                // 仪表板显示 silo- 整个总数,因此只收集直方图数字的单流。
                return instrument.GetType().IsGenericType && instrument.GetType().GetGenericTypeDefinition() == typeof(Histogram<>)
                    ? new ExplicitBucketHistogramConfiguration { TagKeys = [], Boundaries = [], RecordMinMax = false }
                    : null;
            })
            .AddReader(new PeriodicExportingMetricReader(dashboardTelemetryExporter, ExportIntervalMilliseconds, 1000))
            .Build();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(_meterProvider).ConfigureAwait(false);

        static async ValueTask DisposeAsync(object? obj)
        {
            try
            {
                if (obj is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (obj is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
                // 忽略。 忽略。
            }
        }
    }


    public void Dispose()
    {
        try
        {
            _meterProvider?.Dispose();
        }
        catch
        {
            /* 诺 诺 */
        }
    }

    private static string GetOrleansVersion()
    {
        var assembly = typeof(SiloAddress).GetTypeInfo().Assembly;
        return $"{assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} ({assembly.GetName().Version})";
    }

    private static string GetHostVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();

            if (assembly != null)
            {
                return assembly.GetName().Version!.ToString();
            }
        }
        catch
        {
            /* 诺 诺 */
        }

        return "1.0.0.0";
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Unable to activate silo grain service during startup. The service will be activated on first use."
    )]
    private static partial void LogWarningActivateSiloGrainServiceStartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Unable to activate dashboard grain during startup. The grain will be activated on first use."
    )]
    private static partial void LogWarningActivateDashboardGrainStartupFailed(ILogger logger, Exception exception);
}
