using GPSTracker.GrainImplementation;
using Microsoft.AspNetCore.SignalR;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace GPSTracker;

/// <summary>
/// 定期更新 <see cref="IHubListGrain"/> 实现，使用本地 <see cref="RemoteLocationHub"/> 的引用。
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

        // 此处循环执行是因为 HubListGrain 不使用任何形式的持久化，所以如果
        // 托管其激活实例的宿主停止，它将丢失全部内部状态。
        // 如果将 HubListGrain 改为使用持久化存储，则可以安全地移除此循环。
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
                    // 忽略取消异常，因为外层循环负责处理取消。
                }
            }
        }
    }
}
