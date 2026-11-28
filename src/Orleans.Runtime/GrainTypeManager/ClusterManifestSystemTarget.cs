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

            // 版本未发生变化时复用摘要。各内容哈希按不可变清单的标识缓存，
            // 可跨版本及具有相同清单的 Silo 复用。
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

            // 仅当更新后的清单比提供的版本更新时，才返回该清单。
            if (manifest.Version <= version)
            {
                return new(_noUpdate);
            }

            // 缓存当前清单是否包含所有活动服务端节点的结果，
            // 从而无需每次重新计算。
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
            // 不参与任何生命周期阶段，只需激活此实例即可。
        }
    }
}
