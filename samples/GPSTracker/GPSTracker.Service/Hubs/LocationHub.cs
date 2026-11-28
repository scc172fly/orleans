using Microsoft.AspNetCore.SignalR;

namespace GPSTracker;

/// <summary>
/// Web 客户端连接此 Hub 以接收位置更新。消息由 <see cref="RemoteLocationHub"/> 使用 <see cref="IHubContext{LocationHub}"/> 广播。
/// </summary>
public sealed class LocationHub : Hub
{
}
