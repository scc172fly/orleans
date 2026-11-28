using Orleans.Runtime;

namespace GPSTracker.GrainImplementation;

public class HubListGrain : Grain, IHubListGrain
{
    private readonly IClusterMembershipService _clusterMembership;
    private readonly Dictionary<SiloAddress, IRemoteLocationHub> _hubs = new();
    private MembershipVersion _cacheMembershipVersion;
    private List<(SiloAddress Host, IRemoteLocationHub Hub)>? _cache;

    public HubListGrain(IClusterMembershipService clusterMembershipService)
    {
        _clusterMembership = clusterMembershipService;
    }

    public ValueTask AddHub(SiloAddress host, IRemoteLocationHub hubReference)
    {
        // 使缓存失效。
        _cache = null;
        _hubs[host] = hubReference;

        return default;
    }

    public ValueTask<List<(SiloAddress Host, IRemoteLocationHub Hub)>> GetHubs() =>
        new(GetCachedHubs());

    private List<(SiloAddress Host, IRemoteLocationHub Hub)> GetCachedHubs()
    {
        // 缓存有效时返回缓存中的 Hub 列表，否则构建 Hub 列表。
        ClusterMembershipSnapshot clusterMembers = _clusterMembership.CurrentSnapshot;
        if (_cache is { } && clusterMembers.Version == _cacheMembershipVersion)
        {
            return _cache;
        }

        // 过滤尚未激活或已从集群移除的宿主。
        var hubs = new List<(SiloAddress Host, IRemoteLocationHub Hub)>();
        var toDelete = new List<SiloAddress>();
        foreach (KeyValuePair<SiloAddress, IRemoteLocationHub> pair in _hubs)
        {
            SiloAddress host = pair.Key;
            IRemoteLocationHub hubRef = pair.Value;
            SiloStatus hostStatus = clusterMembers.GetSiloStatus(host);
            if (hostStatus is SiloStatus.Dead)
            {
                toDelete.Add(host);
            }

            if (hostStatus is SiloStatus.Active)
            {
                hubs.Add((host, hubRef));
            }
        }

        foreach (SiloAddress host in toDelete)
        {
            _hubs.Remove(host);
        }

        _cache = hubs;
        _cacheMembershipVersion = clusterMembers.Version;
        return hubs;
    }
}
