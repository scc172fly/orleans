using Microsoft.AspNetCore.SignalR;

namespace GPSTracker;

/// <summary>
/// 网络客户端连接到接收位置更新的中心。 信件由 <see cref="RemoteLocationHub"/> 使用 <see cref="IHubContext{LocationHub}"/>广播 。
/// </summary>
public sealed class LocationHub : Hub
{
}
