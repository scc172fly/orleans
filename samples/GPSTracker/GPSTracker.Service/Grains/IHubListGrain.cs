using Orleans.Runtime;

namespace GPSTracker.GrainImplementation;

/// <summary>
/// 用于发现处于活动状态的 SignalR Hub 的服务。
/// </summary>
public interface IHubListGrain : IGrainWithGuidKey
{
    ValueTask AddHub(SiloAddress host, IRemoteLocationHub hubReference);
    ValueTask<List<(SiloAddress Host, IRemoteLocationHub Hub)>> GetHubs();
}
