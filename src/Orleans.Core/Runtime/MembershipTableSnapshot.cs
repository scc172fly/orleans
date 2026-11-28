using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示规范成员资格视图及各 Silo 存活时间戳的不可变快照。
    /// </summary>
    /// <remarks>
    /// 同一版本在整个集群中标识相同的规范成员资格行。
    /// 更新保留同一版本的版本化字段，并保留每个 Silo 已观测到的最大 IAmAliveTime。
    /// 各对等节点独立于这些行保存本地获知的状态信息。
    /// </remarks>
    [GenerateSerializer, Immutable]
    internal sealed class MembershipTableSnapshot : ISpanFormattable
    {
        [Id(3)]
        private readonly IReadOnlySet<SiloAddress>? _recentlyDeadSilos;

        private static readonly MembershipTableSnapshot InitialValue = new(MembershipVersion.MinValue, ImmutableDictionary<SiloAddress, MembershipEntry>.Empty);

        /// <summary>
        /// 初始化 <see cref="MembershipTableSnapshot"/> 类的新实例。
        /// </summary>
        /// <param name="version">此快照表示的成员资格版本。</param>
        /// <param name="entries">此快照包含的成员条目。</param>
        /// <param name="minimumSiloGeneration">本地已淘汰的正数 Silo 世代编号的排他上界。</param>
        /// <param name="recentlyDeadSilos">其记录已被移除的已知死亡标识。</param>
        public MembershipTableSnapshot(
            MembershipVersion version,
            ImmutableDictionary<SiloAddress, MembershipEntry> entries,
            int minimumSiloGeneration = 0,
            IReadOnlySet<SiloAddress>? recentlyDeadSilos = null)
        {
            this.Version = version;
            this.Entries = entries;
            this.MinimumSiloGeneration = minimumSiloGeneration;
            // 冻结调用方拥有的集合，确保已发布的快照保持不可变。
            _recentlyDeadSilos = recentlyDeadSilos?.ToFrozenSet() ?? FrozenSet<SiloAddress>.Empty;
        }

        /// <summary>
        /// 根据成员表数据创建初始快照。
        /// </summary>
        /// <param name="table">成员表数据。</param>
        /// <returns>包含所提供表数据的快照。</returns>
        public static MembershipTableSnapshot Create(MembershipTableData table) => Update(InitialValue, table);

        /// <summary>
        /// 将成员表数据应用于先前的快照，创建新快照。
        /// </summary>
        /// <param name="previousSnapshot">先前的快照。</param>
        /// <param name="table">更新后的成员表数据。</param>
        /// <returns>生成的成员资格快照。</returns>
        public static MembershipTableSnapshot Update(MembershipTableSnapshot previousSnapshot, MembershipTableData table)
        {
            ArgumentNullException.ThrowIfNull(previousSnapshot);
            ArgumentNullException.ThrowIfNull(table);
            var version = (table.Version.Version == 0 && table.Version.VersionEtag == "0")
              ? MembershipVersion.MinValue
              : new MembershipVersion(table.Version.Version);
            return Update(previousSnapshot, version, table.Members.Select(t => t.Item1), FrozenSet<SiloAddress>.Empty);
        }

        /// <summary>
        /// 将较新快照的内容应用于先前的快照，创建新快照。
        /// </summary>
        /// <param name="previousSnapshot">先前的快照。</param>
        /// <param name="updated">更新后的快照。</param>
        /// <returns>生成的成员资格快照。</returns>
        public static MembershipTableSnapshot Update(MembershipTableSnapshot previousSnapshot, MembershipTableSnapshot updated)
        {
            ArgumentNullException.ThrowIfNull(previousSnapshot);
            ArgumentNullException.ThrowIfNull(updated);
            return Update(previousSnapshot, updated.Version, updated.Entries.Values, updated.RecentlyDeadSilos);
        }

        private static MembershipTableSnapshot Update(
            MembershipTableSnapshot previousSnapshot,
            MembershipVersion version,
            IEnumerable<MembershipEntry> updatedEntries,
            IReadOnlySet<SiloAddress> recentlyDeadSilos)
        {
            var entries = ImmutableDictionary.CreateBuilder<SiloAddress, MembershipEntry>();
            var dead = new HashSet<SiloAddress>(previousSnapshot.RecentlyDeadSilos);
            dead.UnionWith(recentlyDeadSilos);
            foreach (var item in updatedEntries)
            {
                var entry = item;
                if (version == previousSnapshot.Version
                    && entry.Status == SiloStatus.Dead
                    && !previousSnapshot.Entries.ContainsKey(entry.SiloAddress))
                {
                    // 相同版本的读取可能发生在 Dead 行被清理之前。
                    dead.Add(entry.SiloAddress);
                    continue;
                }

                if (previousSnapshot.Entries.TryGetValue(entry.SiloAddress, out var previousEntry))
                {
                    var iAmAliveTime = entry.IAmAliveTime > previousEntry.IAmAliveTime
                        ? entry.IAmAliveTime
                        : previousEntry.IAmAliveTime;
                    if (version == previousSnapshot.Version)
                    {
                        entry = previousEntry;
                    }

                    if (entry.IAmAliveTime < iAmAliveTime)
                    {
                        entry = entry.WithIAmAliveTime(iAmAliveTime);
                    }
                }

                entries.Add(entry.SiloAddress, entry);
            }

            var incomingEntries = entries.ToImmutable();
            var incomingIsOlder = version < previousSnapshot.Version;
            var canonicalEntries = incomingIsOlder ? previousSnapshot.Entries : incomingEntries;
            var olderEntries = incomingIsOlder ? incomingEntries : previousSnapshot.Entries;
            foreach (var (silo, entry) in olderEntries)
            {
                // 延迟返回的读取结果和成功完成的写入可能暴露某个注册记录，
                // 该记录已从发布的较新视图中移除。
                if (!canonicalEntries.ContainsKey(silo)
                    && (version != previousSnapshot.Version || entry.Status == SiloStatus.Dead))
                {
                    dead.Add(silo);
                }
            }

            dead.RemoveWhere(silo => canonicalEntries.ContainsKey(silo)
                || IsGenerationRetired(silo, previousSnapshot.MinimumSiloGeneration));
            var retainedDeaths = dead.SetEquals(previousSnapshot.RecentlyDeadSilos)
                ? previousSnapshot.RecentlyDeadSilos
                : dead.ToFrozenSet();
            return new MembershipTableSnapshot(
                incomingIsOlder ? previousSnapshot.Version : version,
                canonicalEntries,
                previousSnapshot.MinimumSiloGeneration,
                retainedDeaths);
        }

        internal MembershipTableSnapshot RetireGenerations(DateTime utcNow, TimeSpan? retirementAge)
        {
            if (retirementAge is not { } age)
            {
                return this;
            }

            var minimum = Math.Max(MinimumSiloGeneration, SiloAddress.GetGenerationRetirementFloor(utcNow, age));
            if (minimum == MinimumSiloGeneration)
            {
                return this;
            }

            var dead = RecentlyDeadSilos;
            if (dead.Any(silo => IsGenerationRetired(silo, minimum)))
            {
                dead = dead.Where(silo => !IsGenerationRetired(silo, minimum)).ToFrozenSet();
            }

            return new(Version, Entries, minimum, dead);
        }

        /// <summary>
        /// 获取此快照表示的成员资格版本。
        /// </summary>
        [Id(0)]
        public MembershipVersion Version { get; }

        /// <summary>
        /// 获取此快照中包含的成员条目。
        /// </summary>
        [Id(1)]
        public ImmutableDictionary<SiloAddress, MembershipEntry> Entries { get; }

        /// <summary>
        /// 获取此对等节点已淘汰的正数 Silo 世代编号的排他上界。
        /// </summary>
        [Id(2)]
        public int MinimumSiloGeneration { get; }

        /// <summary>
        /// 获取独立于规范成员资格行保留的已知死亡标识。
        /// </summary>
        // 旧版 Silo 的快照省略字段 3，不携带保留的死亡历史记录。
        public IReadOnlySet<SiloAddress> RecentlyDeadSilos => _recentlyDeadSilos ?? FrozenSet<SiloAddress>.Empty;

        /// <summary>
        /// 获取此快照中活跃 Silo 的数量。
        /// </summary>
        public int ActiveNodeCount
        {
            get
            {
                var count = 0;
                foreach (var entry in this.Entries)
                {
                    if (entry.Value.Status == SiloStatus.Active)
                    {
                        ++count;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 获取此快照中指定 Silo 的状态。
        /// </summary>
        /// <param name="silo">Silo 地址。</param>
        /// <returns>Silo 状态。</returns>
        public SiloStatus GetSiloStatus(SiloAddress silo) => GetSiloStatus(silo, includeRetirement: true);

        internal SiloStatus GetSiloStatus(SiloAddress silo, bool includeRetirement)
        {
            return this.Entries.TryGetValue(silo, out var entry)
                ? entry.Status
                : GetMissingSiloStatus(silo, includeRetirement ? MinimumSiloGeneration : 0, RecentlyDeadSilos, Entries.Keys);
        }

        internal static SiloStatus GetMissingSiloStatus(
            SiloAddress silo,
            int minimumSiloGeneration,
            IReadOnlySet<SiloAddress> recentlyDeadSilos,
            IEnumerable<SiloAddress> members)
        {
            if (IsGenerationRetired(silo, minimumSiloGeneration) || recentlyDeadSilos.Contains(silo))
            {
                return SiloStatus.Dead;
            }

            foreach (var member in members)
            {
                if (member.IsSuccessorOf(silo))
                {
                    return SiloStatus.Dead;
                }
            }

            foreach (var member in recentlyDeadSilos)
            {
                if (member.IsSuccessorOf(silo))
                {
                    return SiloStatus.Dead;
                }
            }

            return SiloStatus.None;
        }

        private static bool IsGenerationRetired(SiloAddress silo, int minimumSiloGeneration) =>
            silo.Generation > 0 && silo.Generation < minimumSiloGeneration;

        /// <summary>
        /// 判断此快照是否为另一快照的后继快照。
        /// </summary>
        /// <remarks>
        /// 在相同的规范成员资格版本下，进展体现为更新的存活时间戳，
        /// 或清理原先处于 Dead 状态的行。非 Dead 行予以保留。
        /// </remarks>
        /// <param name="other">用于比较的快照。</param>
        /// <returns><see langword="true"/> 表示此快照是 <paramref name="other"/> 的后继快照；否则为 <see langword="false"/>。</returns>
        public bool IsSuccessorTo(MembershipTableSnapshot other)
        {
            if (Version > other.Version)
            {
                return true;
            }

            if (Version < other.Version)
            {
                return false;
            }

            if (MinimumSiloGeneration < other.MinimumSiloGeneration)
            {
                return false;
            }

            if (Entries.Count > other.Entries.Count)
            {
                // 添加行需要新的规范成员资格版本。
                return false;
            }

            var heartbeatAdvanced = false;
            foreach (var (silo, entry) in Entries)
            {
                if (!other.Entries.TryGetValue(silo, out var otherEntry))
                {
                    // 成员资格变更要求推进表版本。
                    return false;
                }

                heartbeatAdvanced |= entry.IAmAliveTime > otherEntry.IAmAliveTime;
            }

            if (Entries.Count == other.Entries.Count)
            {
                return heartbeatAdvanced
                    || MinimumSiloGeneration > other.MinimumSiloGeneration
                    || !RecentlyDeadSilos.SetEquals(other.RecentlyDeadSilos);
            }

            foreach (var (silo, previousEntry) in other.Entries)
            {
                if (previousEntry.Status != SiloStatus.Dead && !Entries.ContainsKey(silo))
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"[Version: {this.Version}, MinimumSiloGeneration: {MinimumSiloGeneration}, {RecentlyDeadSilos.Count} retained deaths, {this.Entries.Count} silos");
            foreach (var entry in this.Entries) sb.Append($", {entry.Value}");
            sb.Append(']');
            return sb.ToString();
        }

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            var written = 0;

            if (!destination.TryWrite($"[Version: {this.Version}, MinimumSiloGeneration: {MinimumSiloGeneration}, {RecentlyDeadSilos.Count} retained deaths, {this.Entries.Count} silos", out var length))
            {
                goto fail;
            }

            Advance(ref destination, ref written, length);

            foreach (var entry in this.Entries)
            {
                if (!destination.TryWrite($", {entry.Value}", out length))
                {
                    goto fail;
                }

                Advance(ref destination, ref written, length);
            }

            if (!Append(ref destination, ref written, "]"))
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
