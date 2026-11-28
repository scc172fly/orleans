namespace Orleans.Runtime
{
    /// <summary>
    /// 通过维护指向环中下一个 Silo 的最新引用，帮助构建一致性哈希环。
    /// </summary>
    internal class SimpleConsistentRingProvider
    {
        private readonly SiloAddress _localSilo;
        private readonly IClusterMembershipService _clusterMembershipService;
        private readonly Lock _lockObj = new();
        private VersionedSuccessor _successor = new VersionedSuccessor(MembershipVersion.MinValue, null);

        public SimpleConsistentRingProvider(ILocalSiloDetails localSiloDetails, IClusterMembershipService clusterMembershipService)
        {
            _localSilo = localSiloDetails.SiloAddress;
            _clusterMembershipService = clusterMembershipService;
            FindSuccessor(_clusterMembershipService.CurrentSnapshot);
        }

        /// <summary>
        /// 获取一致性哈希值大于当前 Silo 且其中最小的活动 Silo 的 <see cref="SiloAddress"/>；
        /// 如果不存在这样的 Silo，则取所有活动 Silo 中一致性哈希值最小者；
        /// 如果集群中没有其他活动 Silo，则为 <see langword="null"/>。
        /// </summary>
        public SiloAddress? Successor
        {
            get
            {
                var snapshot = _clusterMembershipService.CurrentSnapshot;
                var (successorVersion, successor) = _successor;

                if (successorVersion < snapshot.Version)
                {
                    lock (_lockObj)
                    {
                        successor = FindSuccessor(snapshot);
                        _successor = new VersionedSuccessor(snapshot.Version, successor);
                    }
                }

                return successor;
            }
        }

        private SiloAddress? FindSuccessor(ClusterMembershipSnapshot snapshot)
        {
            var (successorVersion, successor) = _successor;
            if (successorVersion >= snapshot.Version)
            {
                return successor;
            }

            // 查找哈希值大于当前 Silo 且其中最小的 Silo。
            (SiloAddress? Silo, int HashCode) firstInRing = (default, int.MaxValue);
            (SiloAddress? Silo, int HashCode) candidate = (default, int.MaxValue);
            var localSiloHashCode = _localSilo.GetConsistentHashCode();
            foreach (var member in snapshot.Members.Values)
            {
                if (member.SiloAddress.Equals(_localSilo))
                {
                    continue;
                }

                if (member.Status != SiloStatus.Active)
                {
                    continue;
                }

                var memberHashCode = member.SiloAddress.GetConsistentHashCode();

                // 本地 Silo 可能位于环的末尾，因此还需找到环中的第一个 Silo；
                // 在这种情况下，它就是本地 Silo 的后继。
                if (memberHashCode < firstInRing.HashCode)
                {
                    firstInRing = (member.SiloAddress, memberHashCode);
                }

                // 此成员位于当前 Silo 之前，但不是环中的第一个成员。
                if (memberHashCode < localSiloHashCode)
                {
                    continue;
                }

                // 此成员位于当前 Silo 之后，但位于当前候选者之前。
                // 因此，该成员成为新的候选者。
                if (memberHashCode < candidate.HashCode)
                {
                    candidate = (member.SiloAddress, memberHashCode);
                }
            }

            // 结果为哈希值大于当前 Silo 且其中最小的 Silo，
            // 或环中的第一个 Silo；如果没有其他活动 Silo，则为 null。
            successor = candidate.Silo ?? firstInRing.Silo;
            return successor;
        }

        private sealed class VersionedSuccessor
        {
            public VersionedSuccessor(MembershipVersion membershipVersion, SiloAddress? siloAddress)
            {
                MembershipVersion = membershipVersion;
                SiloAddress = siloAddress;
            }

            public void Deconstruct(out MembershipVersion version, out SiloAddress? siloAddress)
            {
                version = MembershipVersion;
                siloAddress = SiloAddress;
            }

            public MembershipVersion MembershipVersion { get; }
            public SiloAddress? SiloAddress { get; }
        }
    }
}
