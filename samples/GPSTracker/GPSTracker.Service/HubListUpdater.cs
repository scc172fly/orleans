using GPSTracker.GrainImplementation;
using Microsoft.AspNetCore.SignalR;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace GPSTracker;

/// <summary>
/// 定期更新 <see cref="IHubListGrain"/> 执行情况,并参照当地 <see cref="RemoteLocationHub"/>。
/// </summary>
[Reentrant]
internal sealed class HubListUpdater : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<HubListUpdater> _logger;
    private readonly ILocalSiloDetails _localSiloDetails;
    private readonly RemoteLocationHub _locationBroadcaster;

    public HubListUpdater(
        IGrainFactory grainFactory,
        ILogger<HubListUpdater> logger,
        ILocalSiloDetails localSiloDetails,
        IHubContext<LocationHub> hubContext)
    {
        _grainFactory = grainFactory;
        _logger = logger;
        _localSiloDetails = localSiloDetails;
        _locationBroadcaster = new RemoteLocationHub(hubContext);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IHubListGrain hubListGrain = _grainFactory.GetGrain<IHubListGrain>(Guid.Empty);
        SiloAddress localSiloAddress = _localSiloDetails.SiloAddress;
        IRemoteLocationHub selfReference = _grainFactory.CreateObjectReference<IRemoteLocationHub>(_locationBroadcaster);

        // 循环运行是因为 HubListGrain 不使用任何形式的持久性,所以如果
        // 停止时激活的主机, 然后会失去任何内部状态 。
        // 如果修改 HubListGrain 以使用持久性,则此循环可以安全地去除。
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await hubListGrain.AddHub(localSiloAddress, selfReference);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Error polling location hub list");
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch
                {
                    // 忽略取消的例外, 因为注销由外部环处理 。
                }
            }
        }
    }
}
