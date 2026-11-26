namespace Orleans.Runtime
{
    /// <summary>
    /// 保持对环中下一个 silo 的最新提及,从而帮助构建一个一致的散列圈。
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
        /// 得到 <see cref="SiloAddress"/> 活动 silo 最小的、 最小的 一致的散数值, 最大
        /// 大于此 silo,或如果不存在这种 silo ,则使用绝对最小的一致散列代码的活动 silo ,
        /// 或 <see langword="null"/> 如果没有其他活动 silos 组内。
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

            // 查找 silo ,使用最小的散列码,该散列码大于此 silo。
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

                // 当地 silo 有可能是圈中最后的,因此我们也发现圈中的第一个 silo 。
                // 届时,该企业将是当地 silo的继承人。
                if (memberHashCode < firstInRing.HashCode)
                {
                    firstInRing = (member.SiloAddress, memberHashCode);
                }

                // 这名成员来到环中这个 silo 之前,但不是第一个在环中。
                if (memberHashCode < localSiloHashCode)
                {
                    continue;
                }

                // 该成员在环中此 silo 之后,但先于现任候选人。
                // 因此,这位成员是新候选人。
                if (memberHashCode < candidate.HashCode)
                {
                    candidate = (member.SiloAddress, memberHashCode);
                }
            }

            // 结果是 silo ,其最小的散列码大于 silo,
            // 或环中的第一个 silo ,或在没有其他活动 silos的情况下无效。
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
