using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 分组成员简况。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class ClusterMembershipSnapshot : ISpanFormattable
    {
        [Id(3)]
        private readonly IReadOnlySet<SiloAddress>? _recentlyDeadSilos;

        /// <summary>
        /// 初始化 <see cref="ClusterMembershipSnapshot"/> 类的新实例。
        /// </summary>
        /// <param name="members">分组成员。</param>
        /// <param name="version">分组成员版本。</param>
        public ClusterMembershipSnapshot(ImmutableDictionary<SiloAddress, ClusterMember> members, MembershipVersion version)
            : this(members, version, 0)
        {
        }

        /// <summary>
        /// 利用当地退休知识,开始采用集群观点。
        /// </summary>
        /// <param name="members">分组成员。</param>
        /// <param name="version">分组成员版本。</param>
        /// <param name="minimumSiloGeneration">退休阳性退休人员的独上限 silo 几代人</param>
        /// <param name="recentlyDeadSilos">已知身份已死亡 记录已被删除。</param>
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
        /// 获得分组成员。
        /// </summary>
        /// <value>分组成员。</value>
        [Id(0)]
        public ImmutableDictionary<SiloAddress, ClusterMember> Members { get; }

        /// <summary>
        /// 获得分组成员版本。
        /// </summary>
        /// <value>分组成员版本。</value>
        [Id(1)]
        public MembershipVersion Version { get; }

        /// <summary>
        /// 获得退休阳性者独有的上限 silo 几代人
        /// 没有低于这一约束的 silos 即已死亡;现任成员仍保留其记录身份。
        /// </summary>
        [Id(2)]
        internal int MinimumSiloGeneration { get; }

        // 旧 silos 的截图忽略了字段3, 没有保留死亡历史 。
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
        /// 获取指定 silo状态。
        /// </summary>
        /// <remarks>
        /// 现成员保留其记录身份。
        /// 保存死亡证据 以及继承者化身 之后的代名词
        /// </remarks>
        /// <param name="silo">下 silo.</param>
        /// <returns>指定 silo的状况。</returns>
        public SiloStatus GetSiloStatus(SiloAddress silo)
        {
            return this.Members.TryGetValue(silo, out var entry)
                ? entry.Status
                : MembershipTableSnapshot.GetMissingSiloStatus(silo, MinimumSiloGeneration, RecentlyDeadSilos, Members.Keys);
        }

        /// <summary>
        /// 获取指定状态 silo,处理未知 silos 如果这个快照比当 silo 被看见
        /// </summary>
        /// <param name="silo">下 silo.</param>
        /// <param name="seenAtVersion">成员时的版本 silo 最后一次被看见。</param>
        /// <returns>指定 silo的状况。</returns>
        public SiloStatus GetSiloStatus(SiloAddress silo, MembershipVersion seenAtVersion)
        {
            var status = GetSiloStatus(silo);
            return status == SiloStatus.None && this.Version > seenAtVersion ? SiloStatus.Dead : status;
        }

        /// <summary>
        /// 返回代表此实例的 <see cref="ClusterMembershipUpdate"/> 。
        /// </summary>
        /// <returns>A <see cref="ClusterMembershipUpdate"/> ,代表此实例。</returns>
        public ClusterMembershipUpdate AsUpdate() => new ClusterMembershipUpdate(this, this.Members.Values.ToImmutableArray());

        /// <summary>
        /// a 返回 <see cref="ClusterMembershipUpdate"/> ,表示分组成员从提供的快照到此实例的变化。
        /// </summary>
        /// <returns>A <see cref="ClusterMembershipUpdate"/> ,表示组群成员从提供的简要情况改为这种情况。</returns>
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
                // 包括任何新条目或已更改状态的条目。
                if (!previous.Members.TryGetValue(entry.Key, out var previousEntry) || previousEntry.Status != entry.Value.Status)
                {
                    changes.Add(entry.Value);
                }
            }

            // 处理全部删除的条目 。
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
