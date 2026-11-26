using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// 代表一个永恒的会员观点和每个silo 活性时间戳。
    /// </summary>
    /// <remarks>
    /// 版本确定了整个组群中相同的大宗会员。
    /// 更新保留在同一版本的已更新字段和最大遵守 IAmAliveTime 每个 silo.
    /// 每个同侪将保留与这些行分开的当地身份知识。
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
        /// <param name="version">本快照所代表的成员版本。</param>
        /// <param name="entries">本快照所载成员条目。</param>
        /// <param name="minimumSiloGeneration">退休阳性者在当地独有的上限 silo 几代人</param>
        /// <param name="recentlyDeadSilos">已知身份已死亡 记录已被删除。</param>
        public MembershipTableSnapshot(
            MembershipVersion version,
            ImmutableDictionary<SiloAddress, MembershipEntry> entries,
            int minimumSiloGeneration = 0,
            IReadOnlySet<SiloAddress>? recentlyDeadSilos = null)
        {
            this.Version = version;
            this.Entries = entries;
            this.MinimumSiloGeneration = minimumSiloGeneration;
            // 冻结电话机,因此已公布的快照仍然无法改变。
            _recentlyDeadSilos = recentlyDeadSilos?.ToFrozenSet() ?? FrozenSet<SiloAddress>.Empty;
        }

        /// <summary>
        /// 创建成员表格数据的初步快照。
        /// </summary>
        /// <param name="table">成员表数据。</param>
        /// <returns>载有所提供的表格数据的简况。</returns>
        public static MembershipTableSnapshot Create(MembershipTableData table) => Update(InitialValue, table);

        /// <summary>
        /// 将会籍表数据应用到前一抓图中, 创建快照 。
        /// </summary>
        /// <param name="previousSnapshot">上一个快照。</param>
        /// <param name="table">更新的成员数据表。</param>
        /// <returns>由此产生的成员简况。</returns>
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
        /// 将更新的快照内容应用到上一个快照中, 创建快照 。
        /// </summary>
        /// <param name="previousSnapshot">上一个快照。</param>
        /// <param name="updated">最新快照。</param>
        /// <returns>由此产生的成员简况。</returns>
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
                    // 等版本读取可在死列的剪裁前完成 。
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
                // 阅读延迟,写作成功完成,可以显示登记
                // 已经从已出版的较新视图中删除。
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
        /// 获得由此快照所代表的会员版本。
        /// </summary>
        [Id(0)]
        public MembershipVersion Version { get; }

        /// <summary>
        /// 获得会员条目包含在这张快照中。
        /// </summary>
        [Id(1)]
        public ImmutableDictionary<SiloAddress, MembershipEntry> Entries { get; }

        /// <summary>
        /// 获得这个同侪独有的退休阳性 silo 几代人
        /// </summary>
        [Id(2)]
        public int MinimumSiloGeneration { get; }

        /// <summary>
        /// 获得已知的死身份 与卡尼科州会员排分开保留。
        /// </summary>
        // 旧 silos 的截图忽略了字段3, 没有保留死亡历史 。
        public IReadOnlySet<SiloAddress> RecentlyDeadSilos => _recentlyDeadSilos ?? FrozenSet<SiloAddress>.Empty;

        /// <summary>
        /// 在此快照中获取活动 silos 的数 。
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
        /// 获取此快照中指定 silo 的状态。
        /// </summary>
        /// <param name="silo"> silo 地址。</param>
        /// <returns> silo 状态。</returns>
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
        /// 确定此快照是否是另一个快照的后继者 。
        /// </summary>
        /// <remarks>
        /// 在同一种卡尼卡成员制版本中,进步包括较新的活性时间戳
        /// 或前行的剪裁。保留非死列。
        /// </remarks>
        /// <param name="other">与该快照比较。</param>
        /// <returns><see langword="true"/> 如果该快照是 <paramref name="other"/>的继承者;否则, <see langword="false"/>。</returns>
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
                // 添加一行需要一个新的卡通会籍版本 。
                return false;
            }

            var heartbeatAdvanced = false;
            foreach (var (silo, entry) in Entries)
            {
                if (!other.Entries.TryGetValue(silo, out var otherEntry))
                {
                    // 成员变动要求表格转换预发款。
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
