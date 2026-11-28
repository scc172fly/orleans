using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示集群成员资格的快照。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class ClusterMembershipSnapshot : ISpanFormattable
    {
        [Id(3)]
        private readonly IReadOnlySet<SiloAddress>? _recentlyDeadSilos;

        /// <summary>
        /// 初始化 <see cref="ClusterMembershipSnapshot"/> 类的新实例。
        /// </summary>
        /// <param name="members">集群成员。</param>
        /// <param name="version">集群成员资格版本。</param>
        public ClusterMembershipSnapshot(ImmutableDictionary<SiloAddress, ClusterMember> members, MembershipVersion version)
            : this(members, version, 0)
        {
        }

        /// <summary>
        /// 使用本地已知的世代淘汰信息初始化集群视图。
        /// </summary>
        /// <param name="members">集群成员。</param>
        /// <param name="version">集群成员资格版本。</param>
        /// <param name="minimumSiloGeneration">已淘汰的正数 Silo 世代编号的排他上界。</param>
        /// <param name="recentlyDeadSilos">其记录已被移除的已知死亡标识。</param>
        internal ClusterMembershipSnapshot(
            ImmutableDictionary<SiloAddress, ClusterMember> members,
            MembershipVersion version,
            int minimumSiloGeneration,
            IReadOnlySet<SiloAddress>? recentlyDeadSilos = null)
        {
            this.Members = members;
            this.Version = version;
            this.MinimumSiloGeneration = minimumSiloGeneration;
            _recentlyDeadSilos = recentlyDeadSilos?.ToFrozenSet() ?? FrozenSet<SiloAddress>.Empty;
        }

        internal static ClusterMembershipSnapshot Default => new(ImmutableDictionary<SiloAddress, ClusterMember>.Empty, MembershipVersion.MinValue);

        /// <summary>
        /// 获取集群成员。
        /// </summary>
        /// <value>集群成员。</value>
        [Id(0)]
        public ImmutableDictionary<SiloAddress, ClusterMember> Members { get; }

        /// <summary>
        /// 获取集群成员资格版本。
        /// </summary>
        /// <value>集群成员资格版本。</value>
        [Id(1)]
        public MembershipVersion Version { get; }

        /// <summary>
        /// 获取已淘汰的正数 Silo 世代编号的排他上界。
        /// 缺失且低于此上界的 Silo 视为已死亡；当前存在的成员保留其记录中的状态。
        /// </summary>
        [Id(2)]
        internal int MinimumSiloGeneration { get; }

        // 旧版 Silo 的快照省略字段 3，不携带保留的死亡历史记录。
        internal IReadOnlySet<SiloAddress> RecentlyDeadSilos => _recentlyDeadSilos ?? FrozenSet<SiloAddress>.Empty;

        internal bool IsSuccessorTo(ClusterMembershipSnapshot previous)
        {
            if (Version != previous.Version)
            {
                return Version > previous.Version;
            }

            if (MinimumSiloGeneration < previous.MinimumSiloGeneration)
            {
                return false;
            }

            return MinimumSiloGeneration > previous.MinimumSiloGeneration
                || Members.Count < previous.Members.Count
                || RecentlyDeadSilos.IsProperSupersetOf(previous.RecentlyDeadSilos);
        }

        /// <summary>
        /// 获取指定 Silo 的状态。
        /// </summary>
        /// <remarks>
        /// 当前存在的成员保留其记录中的状态。缺失标识按以下信息判定：
        /// 快照的固定世代截止点、保留的死亡状态证据及后续运行世代标识。
        /// </remarks>
        /// <param name="silo">Silo。</param>
        /// <returns>指定 Silo 的状态。</returns>
        public SiloStatus GetSiloStatus(SiloAddress silo)
        {
            return this.Members.TryGetValue(silo, out var entry)
                ? entry.Status
                : MembershipTableSnapshot.GetMissingSiloStatus(silo, MinimumSiloGeneration, RecentlyDeadSilos, Members.Keys);
        }

        /// <summary>
        /// 获取指定 Silo 的状态；如果此快照比最后观察到该 Silo 时更新，则将未知 Silo 视为已死亡。
        /// </summary>
        /// <param name="silo">Silo。</param>
        /// <param name="seenAtVersion">最后观察到 Silo 时的成员资格版本。</param>
        /// <returns>指定 Silo 的状态。</returns>
        public SiloStatus GetSiloStatus(SiloAddress silo, MembershipVersion seenAtVersion)
        {
            var status = GetSiloStatus(silo);
            return status == SiloStatus.None && this.Version > seenAtVersion ? SiloStatus.Dead : status;
        }

        /// <summary>
        /// 返回表示此实例的 <see cref="ClusterMembershipUpdate"/>。
        /// </summary>
        /// <returns>表示此实例的 <see cref="ClusterMembershipUpdate"/>。</returns>
        public ClusterMembershipUpdate AsUpdate() => new ClusterMembershipUpdate(this, this.Members.Values.ToImmutableArray());

        /// <summary>
        /// 返回 <see cref="ClusterMembershipUpdate"/>，表示从给定快照到此实例的集群成员资格变化。
        /// </summary>
        /// <returns><see cref="ClusterMembershipUpdate"/>，表示从给定快照到此实例的集群成员资格变化。</returns>
        public ClusterMembershipUpdate CreateUpdate(ClusterMembershipSnapshot previous)
        {
            if (previous is null) throw new ArgumentNullException(nameof(previous));
            if (this.Version < previous.Version)
            {
                throw new ArgumentException($"Argument must have a previous version to the current instance. Expected <= {this.Version}, encountered {previous.Version}", nameof(previous));
            }

            if (this.Version == previous.Version)
            {
                return new ClusterMembershipUpdate(this, ImmutableArray<ClusterMember>.Empty);
            }

            var changes = ImmutableHashSet.CreateBuilder<ClusterMember>();
            foreach (var entry in this.Members)
            {
                // 纳入所有新条目或状态发生变化的条目。
                if (!previous.Members.TryGetValue(entry.Key, out var previousEntry) || previousEntry.Status != entry.Value.Status)
                {
                    changes.Add(entry.Value);
                }
            }

            // 处理被完全移除的条目。
            foreach (var entry in previous.Members)
            {
                if (!this.Members.TryGetValue(entry.Key, out _))
                {
                    changes.Add(new ClusterMember(entry.Key, SiloStatus.Dead, entry.Value.Name, wasDeclaredDead: true));
                }
            }

            return new ClusterMembershipUpdate(this, changes.ToImmutableArray());
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"ClusterMembershipSnapshot {{ Version = {this.Version}, Members.Count = {this.Members.Count}, Members = [");
            var first = true;
            foreach (var member in this.Members)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    sb.Append(", ");
                }

                sb.Append($"{member.Value}");
            }

            sb.Append("] }}");
            return sb.ToString();
        }

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            var written = 0;

            if (!destination.TryWrite($"ClusterMembershipSnapshot {{ Version = {this.Version}, Members.Count = {this.Members.Count}, Members = [", out var length))
            {
                goto fail;
            }

            Advance(ref destination, ref written, length);

            var first = true;
            foreach (var member in this.Members)
            {
                if (first)
                {
                    first = false;
                }
                else if (!Append(ref destination, ref written, ", "))
                {
                    goto fail;
                }

                if (!destination.TryWrite($"{member.Value}", out length))
                {
                    goto fail;
                }

                Advance(ref destination, ref written, length);
            }

            if (!Append(ref destination, ref written, "] }}"))
            {
                goto fail;
            }

            charsWritten = written;
            return true;

fail:
            charsWritten = 0;
            return false;

            static bool Append(ref Span<char> destination, ref int written, ReadOnlySpan<char> value)
            {
                if (!value.TryCopyTo(destination))
                {
                    return false;
                }

                Advance(ref destination, ref written, value.Length);
                return true;
            }

            static void Advance(ref Span<char> destination, ref int written, int length)
            {
                destination = destination[length..];
                written += length;
            }
        }
    }
}
