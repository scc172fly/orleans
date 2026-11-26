using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Metadata;
using Orleans.Runtime.Metadata;

namespace Orleans.Runtime
{
    internal sealed class ClusterManifestSystemTarget : SystemTarget, IClusterManifestSystemTarget, ISiloManifestSystemTarget, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly GrainManifest _siloManifest;
        private readonly IClusterMembershipService _clusterMembershipService;
        private readonly IClusterManifestProvider _clusterManifestProvider;
        private readonly ClusterManifestUpdate? _noUpdate = default;
        private MembershipVersion _cachedMembershipVersion;
        private ClusterManifestUpdate? _cachedUpdate;
        private MajorMinorVersion _cachedHashSummaryVersion;
        private ClusterManifestHashSummary? _cachedHashSummary;

        public ClusterManifestSystemTarget(
            IClusterMembershipService clusterMembershipService,
            IClusterManifestProvider clusterManifestProvider,
            SystemTargetShared shared)
            : base(Constants.ManifestProviderType, shared)
        {
            _siloManifest = clusterManifestProvider.LocalGrainManifest;
            _clusterMembershipService = clusterMembershipService;
            _clusterManifestProvider = clusterManifestProvider;
            shared.ActivationDirectory.RecordNewTarget(this);
        }

        public ValueTask<ClusterManifest> GetClusterManifest(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(_clusterManifestProvider.Current);
        }

        public ValueTask<ClusterManifestHashSummary> GetClusterManifestHashSummary(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = _clusterManifestProvider.Current;

            // 在版本未改变时重新使用摘要。 个人内容的杂列由
            // 不同版本和 silos 之间均具有相同的表单,且不改变的明显身份。
            if (_cachedHashSummary is null || manifest.Version != _cachedHashSummaryVersion)
            {
                var hashes = new Dictionary<SiloAddress, ManifestHash>();
                foreach (var siloManifest in manifest.Silos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hashes[siloManifest.Key] = ManifestHashCalculator.ComputeHash(siloManifest.Value);
                }

                _cachedHashSummary = new ClusterManifestHashSummary(manifest.Version, hashes);
                _cachedHashSummaryVersion = manifest.Version;
            }

            return new(_cachedHashSummary);
        }

        public ValueTask<ManifestHash> GetSiloManifestHash(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(ManifestHashCalculator.ComputeHash(_siloManifest));
        }

        public ValueTask<GrainManifest?> GetSiloManifestByHash(ManifestHash hash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(hash == ManifestHashCalculator.ComputeHash(_siloManifest) ? _siloManifest : null);
        }

        public ValueTask<ClusterManifestUpdate?> GetClusterManifestUpdate(
            MajorMinorVersion version,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = _clusterManifestProvider.Current;

            // 只有在更新的列表比所提供的版本新时,才返回该列表。
            if (manifest.Version <= version)
            {
                return new(_noUpdate);
            }

            // 保存一个缓存, 以显示当前列表是否包含所有活动服务器, 以便它
            // 不需要每次进行重新计数。
            var membershipSnapshot = _clusterMembershipService.CurrentSnapshot;
            if (_cachedUpdate is null
                || membershipSnapshot.Version > _cachedMembershipVersion
                || manifest.Version > _cachedUpdate.Version)
            {
                var includesAllActiveServers = true;
                foreach (var server in membershipSnapshot.Members)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (server.Value.Status == SiloStatus.Active)
                    {
                        if (!manifest.Silos.ContainsKey(server.Key))
                        {
                            includesAllActiveServers = false;
                        }
                    }
                }

                _cachedUpdate = new ClusterManifestUpdate(manifest.Version, manifest.Silos, includesAllActiveServers);
                _cachedMembershipVersion = membershipSnapshot.Version;
            }

            return new(_cachedUpdate);
        }

        public ValueTask<GrainManifest> GetSiloManifest(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(_siloManifest);
        }
        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            // 我们不参与任何生命周期阶段: 启动这个实例是必需的。
        }
    }
}
