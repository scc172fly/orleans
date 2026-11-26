using GPSTracker.Common;
using Microsoft.AspNetCore.SignalR;

namespace GPSTracker;

/// <summary>
/// 向与本地 SignalR 枢纽连接的客户发送定位信息。
/// </summary>
internal sealed class RemoteLocationHub : IRemoteLocationHub
{
    private readonly IHubContext<LocationHub> _hub;

    public RemoteLocationHub(IHubContext<LocationHub> hub) => _hub = hub;

    // 向与中心连接的每个客户发送信息
    public ValueTask BroadcastUpdates(VelocityBatch messages) =>
        new(_hub.Clients.All.SendAsync(
            "locationUpdates", messages, CancellationToken.None));
}
