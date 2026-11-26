using Orleans.Runtime;

namespace GPSTracker.GrainImplementation;

/// <summary>
/// 活动 SignalR 枢纽的服务发现。
/// </summary>
public interface IHubListGrain : IGrainWithGuidKey
{
    ValueTask AddHub(SiloAddress host, IRemoteLocationHub hubReference);
    ValueTask<List<(SiloAddress Host, IRemoteLocationHub Hub)>> GetHubs();
}
