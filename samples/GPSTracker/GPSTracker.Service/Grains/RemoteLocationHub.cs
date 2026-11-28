using GPSTracker.Common;
using Microsoft.AspNetCore.SignalR;

namespace GPSTracker;

/// <summary>
/// 将位置消息广播给连接到本地 SignalR Hub 的客户端。
/// </summary>
internal sealed class RemoteLocationHub : IRemoteLocationHub
{
    private readonly IHubContext<LocationHub> _hub;

    public RemoteLocationHub(IHubContext<LocationHub> hub) => _hub = hub;

    // 向连接到此 Hub 的每个客户端发送消息。
    public ValueTask BroadcastUpdates(VelocityBatch messages) =>
        new(_hub.Clients.All.SendAsync(
            "locationUpdates", messages, CancellationToken.None));
}
