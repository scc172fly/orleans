using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 成员表接口。
    /// </summary>
    /// <remarks>
    /// 提供程序可实现支持取消的 Async 方法，以取消其底层操作。
    /// 默认实现取消调用方的等待，同时让不接受取消令牌的操作继续完成，
    /// 并观察该操作产生的任何异常。
    /// 支持取消的 RPC 方法保留现有操作别名和序列化的应用参数。
    /// 取消信号独立于这些参数进行传播。
    /// </remarks>
    public interface IMembershipTable
    {
        /// <summary>
        /// 初始化成员表；此方法将在所有其他方法之前调用。
        /// </summary>
        /// <param name="tryInitTableVersion">是否尝试初始化底层表。</param>
        /// <returns>表示初始化操作的任务。</returns>
        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        Task InitializeMembershipTable(bool tryInitTableVersion);

        /// <inheritdoc cref="InitializeMembershipTable(bool)"/>
        /// <param name="tryInitTableVersion">是否初始化底层表版本。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Alias("FB89E5E9")]
        async Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = InitializeMembershipTable(tryInitTableVersion);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 删除指定 clusterId 的所有表条目。
        /// </summary>
        /// <param name="clusterId">要删除其条目的集群标识符。</param>
        /// <returns>表示删除操作的任务。</returns>
        [Obsolete("Use DeleteMembershipTableEntriesAsync instead.")]
        Task DeleteMembershipTableEntries(string clusterId);

        /// <inheritdoc cref="DeleteMembershipTableEntries(string)"/>
        /// <param name="clusterId">要删除其条目的集群。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Alias("BF899C85")]
        async Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = DeleteMembershipTableEntries(clusterId);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 移除最近已知更新时间早于 <paramref name="beforeDate"/> 的已死亡 Silo 条目。
        /// </summary>
        /// <param name="beforeDate">待删除条目的最近已知更新时间的排他上界。</param>
        /// <returns>表示清理操作的任务。</returns>
        [Obsolete("Use CleanupDefunctSiloEntriesAsync instead.")]
        Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate);

        /// <inheritdoc cref="CleanupDefunctSiloEntries(DateTimeOffset)"/>
        /// <param name="beforeDate">待删除条目最后更新时间的排他上界。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Alias("7A519C2E")]
        async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = CleanupDefunctSiloEntries(beforeDate);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 通过提供程序的旧版单行读取操作读取成员信息。
        /// </summary>
        /// <remarks>
        /// 使用 <see cref="ReadAllAsync"/> 获取原子快照，并使用
        /// <see cref="MembershipTableData.TryGet"/> 选取某个 Silo 及其实体标记。
        /// 内置提供程序在读取单行时报告 <see cref="NotSupportedException"/>。
        /// </remarks>
        /// <param name="key">需要读取成员信息的 Silo 地址。</param>
        /// <returns>给定 Silo 的成员信息：由一个 MembershipEntry 条目与
        /// TableVersion 组成的 MembershipTableData，二者以原子方式读取。</returns>
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        Task<MembershipTableData> ReadRow(SiloAddress key);

        /// <inheritdoc cref="ReadRow(SiloAddress)"/>
        /// <param name="key">要读取的 Silo 地址。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        [Alias("D851FB33")]
        async Task<MembershipTableData> ReadRowAsync(SiloAddress key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = ReadRow(key);
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 以原子方式读取成员表的全部内容。
        /// 返回的 MembershipTableData 包含表中所有 Silo 的全部 MembershipEntry 条目，以及
        /// 此表的 TableVersion。MembershipEntry 条目与 TableVersion 必须以原子方式读取。
        /// </summary>
        /// <returns>给定成员表的成员信息：由多个 MembershipEntry 条目和
        /// TableVersion 组成的 MembershipTableData，全部以原子方式读取。</returns>
        [Obsolete("Use ReadAllAsync instead.")]
        Task<MembershipTableData> ReadAll();

        /// <inheritdoc cref="ReadAll()"/>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Alias("00BCE16F")]
        async Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = ReadAll();
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 以原子方式尝试为一个 Silo 插入（添加）新的 MembershipEntry，同时更新 TableVersion。
        /// 如果操作成功，则将对表进行以下更改：
        /// 1) 新的 MembershipEntry 将被添加到表中。
        /// 2) 新添加的 MembershipEntry 将具有由提供程序定义的行 ETag。
        /// 3) 表中的 TableVersion.Version 将更新为新的 TableVersion.Version。
        /// 4) 表中的 TableVersion ETag 将更新为自动生成的新的唯一 ETag。
        /// 对表的这些变更，包括插入新行、更新表版本及相关 ETag，必须全部原子地发生，或原子地失败且不产生副作用。
        /// 操作应在以下每种条件下失败：
        /// 1) 表中已存在给定 Silo 的 MembershipEntry。
        /// 2) 更新 TableVersion 失败，因为传入的 TableVersion ETag（由 TableVersion.VersionEtag 属性指定）与表中的 TableVersion ETag 不匹配。
        /// </summary>
        /// <param name="entry">要插入的 MembershipEntry。</param>
        /// <param name="tableVersion">此表的新 TableVersion 及其 ETag。</param>
        /// <returns>插入操作成功时为 true，否则为 false。</returns>
        [Obsolete("Use InsertRowAsync instead.")]
        Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion);

        /// <inheritdoc cref="InsertRow(MembershipEntry, TableVersion)"/>
        /// <param name="entry">要插入的成员条目。</param>
        /// <param name="tableVersion">新的表版本及其预期 ETag。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Alias("FEF3AC5A")]
        async Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = InsertRow(entry, tableVersion);
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 以原子方式尝试更新一个 Silo 的 MembershipEntry，同时更新 TableVersion。
        /// 如果操作成功，则将对表进行以下更改：
        /// 1) 使用提供的条目更新此 Silo 的 MembershipEntry。
        /// 2) 更新后的 MembershipEntry 的行 ETag 将反映提供程序的元数据或并发令牌策略。
        /// 3) 表中的 TableVersion.Version 将更新为新的 TableVersion.Version。
        /// 4) 表中的 TableVersion ETag 将更新为自动生成的新的唯一 ETag。
        /// 对表的这些变更，包括更新行、更新表版本及相关 ETag，必须全部原子地发生，或原子地失败且不产生副作用。
        /// 操作应在以下每种条件下失败：
        /// 1) 表中不存在给定 Silo 的 MembershipEntry。
        /// 2) 更新 TableVersion 失败，因为传入的 TableVersion ETag（由 TableVersion.VersionEtag 属性指定）与表中的 TableVersion ETag 不匹配。
        /// 3) 提供程序验证独立的规范行并发令牌，而所提供的 ETag 与之不匹配。
        /// </summary>
        /// <remarks>
        /// 提供程序以原子方式要求行存在并验证 <see cref="TableVersion.VersionEtag"/>。
        /// 提供程序可以将行 ETag 用作不受心跳影响的额外并发令牌，也可在
        /// 表 ETag 验证能够保护现有行的更新时忽略它。额外的行保护条件必须
        /// 在仅有心跳活动时仍然成立；只要行存在且表 ETag 仍为当前值，
        /// 就应保证先前读取的行和表输入仍可通过验证。
        /// </remarks>
        /// <param name="entry">要更新的 MembershipEntry。</param>
        /// <param name="etag">从成员表读取操作中获得的、由提供程序定义的行实体标记。</param>
        /// <param name="tableVersion">此表的新 TableVersion 及其 ETag。</param>
        /// <returns>更新操作成功时为 true，否则为 false。</returns>
        [Obsolete("Use UpdateRowAsync instead.")]
        Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion);

        /// <inheritdoc cref="UpdateRow(MembershipEntry, string, TableVersion)"/>
        /// <param name="entry">要更新的成员条目。</param>
        /// <param name="etag">从成员表读取操作中获得的、由提供程序定义的行实体标记。</param>
        /// <param name="tableVersion">新的表版本及其预期 ETag。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Alias("E06D3DBC")]
        async Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = UpdateRow(entry, etag, tableVersion);
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 更新此 Silo 的 MembershipEntry 的 IAmAliveTime 列。
        /// </summary>
        /// <remarks>
        /// 每个 Silo 拥有自己的心跳，并将提供的时间戳直接写入其 IAmAliveTime 列。
        /// 保留其他成员字段及表版本（包括其 ETag）。
        /// 返回的行 ETag 可能反映后端管理的元数据，并随心跳变化。
        /// 只要行仍存在且表 ETag 仍为当前值，仅有心跳活动时，之前
        /// 读取的行和表输入仍可供 <see cref="UpdateRowAsync"/> 使用。
        /// </remarks>
        /// <param name="entry">包含更新后的 <see cref="MembershipEntry.IAmAliveTime"/> 值的成员条目。</param>
        /// <returns>表示更新操作的任务。</returns>
        [Obsolete("Use UpdateIAmAliveAsync instead.")]
        Task UpdateIAmAlive(MembershipEntry entry);

        /// <inheritdoc cref="UpdateIAmAlive(MembershipEntry)"/>
        /// <param name="entry">包含更新后存活时间戳的成员条目。</param>
        /// <param name="cancellationToken">用于取消该操作的令牌。</param>
        [Alias("B1A52D2B")]
        async Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // 为兼容现有不接受取消令牌的提供程序而转发调用。
            var operation = UpdateIAmAlive(entry);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 基于系统目标的实现所使用的成员表接口。
    /// </summary>
    public interface IMembershipTableSystemTarget : IMembershipTable, ISystemTarget
    {
    }

    /// <summary>
    /// 表示成员表的版本及用于乐观并发控制的实体标记。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class TableVersion : ISpanFormattable, IEquatable<TableVersion>
    {
        /// <summary>
        /// 此 TableVersion 的版本部分，是单调递增的数字。
        /// </summary>
        [Id(0)]
        public int Version { get; }

        /// <summary>
        /// 此 TableVersion 的 ETag，用于验证表更新操作。
        /// </summary>
        [Id(1)]
        public string VersionEtag { get; }

        /// <summary>
        /// 初始化 <see cref="TableVersion"/> 类的新实例。
        /// </summary>
        /// <param name="version">成员表版本。</param>
        /// <param name="eTag">用于验证对此版本更新的实体标记。</param>
        public TableVersion(int version, string eTag)
        {
            Version = version;
            VersionEtag = eTag;
        }

        /// <summary>
        /// 创建下一个成员表版本，同时保留当前实体标记以供并发验证。
        /// </summary>
        /// <returns>下一个成员表版本。</returns>
        /// <exception cref="OverflowException">成员表版本已达到 <see cref="int.MaxValue"/>。</exception>
        public TableVersion Next() => new(checked(Version + 1), VersionEtag);

        /// <inheritdoc />
        public override string ToString() => $"<{Version}, {VersionEtag}>";
        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"<{Version}, {VersionEtag}>", out charsWritten);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as TableVersion);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Version, VersionEtag);

        /// <inheritdoc />
        public bool Equals(TableVersion? other) => other is not null && Version == other.Version && VersionEtag == other.VersionEtag;

        /// <summary>
        /// 判断两个成员表版本是否相等。
        /// </summary>
        /// <param name="left">要比较的第一个成员表版本。</param>
        /// <param name="right">要比较的第二个成员表版本。</param>
        /// <returns><see langword="true"/> 表示版本与实体标记均相等；否则为 <see langword="false"/>。</returns>
        public static bool operator ==(TableVersion? left, TableVersion? right) => EqualityComparer<TableVersion>.Default.Equals(left, right);

        /// <summary>
        /// 判断两个成员表版本是否不相等。
        /// </summary>
        /// <param name="left">要比较的第一个成员表版本。</param>
        /// <param name="right">要比较的第二个成员表版本。</param>
        /// <returns><see langword="true"/> 表示版本或实体标记不同；否则为 <see langword="false"/>。</returns>
        public static bool operator !=(TableVersion? left, TableVersion? right) => !(left == right);
    }

    /// <summary>
    /// 表示成员条目及成员表版本的原子快照。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class MembershipTableData
    {
        /// <summary>
        /// 获取成员条目及其由提供程序定义的行实体标记。
        /// </summary>
        [Id(0)]
        public IReadOnlyList<Tuple<MembershipEntry, string>> Members { get; private set; }

        /// <summary>
        /// 获取成员表快照的版本。
        /// </summary>
        [Id(1)]
        public TableVersion Version { get; private set; }

        /// <summary>
        /// 根据多个成员行初始化 <see cref="MembershipTableData"/> 类的新实例。
        /// </summary>
        /// <param name="list">成员条目及其实体标记。</param>
        /// <param name="version">成员表快照的版本。</param>
        public MembershipTableData(List<Tuple<MembershipEntry, string>> list, TableVersion version)
        {
            // 将死亡条目置于末尾，仅用于日志记录。
            list.Sort(
               (x, y) =>
               {
                   if (x.Item1.Status == SiloStatus.Dead) return 1; // 将死亡条目置于末尾。
                   if (y.Item1.Status == SiloStatus.Dead) return -1; // 将死亡条目置于末尾。
                   return string.CompareOrdinal(x.Item1.SiloName, y.Item1.SiloName);
               });
            Members = list;
            Version = version;
        }

        /// <summary>
        /// 根据一个成员行初始化 <see cref="MembershipTableData"/> 类的新实例。
        /// </summary>
        /// <param name="tuple">成员条目及其实体标记。</param>
        /// <param name="version">成员表快照的版本。</param>
        public MembershipTableData(Tuple<MembershipEntry, string> tuple, TableVersion version)
        {
            Members = new[] { tuple };
            Version = version;
        }

        /// <summary>
        /// 初始化 <see cref="MembershipTableData"/> 类的空实例。
        /// </summary>
        /// <param name="version">成员表快照的版本。</param>
        public MembershipTableData(TableVersion version)
        {
            Members = Array.Empty<Tuple<MembershipEntry, string>>();
            Version = version;
        }

        /// <summary>
        /// 查找指定 Silo 的成员行。
        /// </summary>
        /// <param name="silo">要查找的 Silo 地址。</param>
        /// <returns>成员条目及实体标记；若 Silo 不存在，则为 <see langword="null"/>。</returns>
        public Tuple<MembershipEntry, string>? TryGet(SiloAddress silo)
        {
            foreach (var item in Members)
                if (item.Item1.SiloAddress.Equals(silo))
                    return item;

            return null;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            int active = Members.Count(e => e.Item1.Status == SiloStatus.Active);
            int dead = Members.Count(e => e.Item1.Status == SiloStatus.Dead);
            int created = Members.Count(e => e.Item1.Status == SiloStatus.Created);
            int joining = Members.Count(e => e.Item1.Status == SiloStatus.Joining);
            int shuttingDown = Members.Count(e => e.Item1.Status == SiloStatus.ShuttingDown);
            int stopping = Members.Count(e => e.Item1.Status == SiloStatus.Stopping);

            return @$"{Members.Count} silos, {active} are Active, {dead} are Dead{(created > 0 ? $", {created} are Created" : null)}{(joining > 0 ? $", {joining} are Joining" : null)}{(shuttingDown > 0 ? $", {shuttingDown} are ShuttingDown" : null)}{(stopping > 0 ? $", {stopping} are Stopping" : null)}, Version={Version}. All silos: {Utils.EnumerableToString(Members.Select(t => t.Item1))}";
        }

        /// <summary>
        /// 创建成员表快照，其中每个端点仅保留最新的死亡 Silo 世代。
        /// </summary>
        /// <returns>
        /// 包含所有非死亡条目及每个端点最新死亡条目的成员表快照。
        /// </returns>
        public MembershipTableData WithoutDuplicateDeads()
        {
            var dead = new Dictionary<IPEndPoint, Tuple<MembershipEntry, string>>();
            // 为每个实例仅选择最新的 Dead 条目。
            foreach (var next in Members.Where(item => item.Item1.Status == SiloStatus.Dead))
            {
                var ipEndPoint = next.Item1.SiloAddress.Endpoint;
                Tuple<MembershipEntry, string>? prev;
                if (!dead.TryGetValue(ipEndPoint, out prev))
                {
                    dead[ipEndPoint] = next;
                }
                else
                {
                    // 更晚的死亡条目。
                    if (next.Item1.SiloAddress.Generation.CompareTo(prev.Item1.SiloAddress.Generation) > 0)
                        dead[ipEndPoint] = next;
                }
            }

            // 现在重新加入非死亡条目。
            List<Tuple<MembershipEntry, string>> all = dead.Values.ToList();
            all.AddRange(Members.Where(item => item.Item1.Status != SiloStatus.Dead));
            return new MembershipTableData(all, Version);
        }

        internal Dictionary<SiloAddress, SiloStatus> GetSiloStatuses(Func<SiloStatus, bool> filter, bool includeMyself, SiloAddress myAddress)
        {
            var result = new Dictionary<SiloAddress, SiloStatus>();
            foreach (var memberEntry in this.Members)
            {
                var entry = memberEntry.Item1;
                if (!includeMyself && entry.SiloAddress.Equals(myAddress)) continue;
                if (filter(entry.Status)) result[entry.SiloAddress] = entry.Status;
            }

            return result;
        }
    }

    /// <summary>
    /// 表示一个 Silo 的成员记录。
    /// </summary>
    [GenerateSerializer]
    [Serializable]
    public sealed class MembershipEntry : ISpanFormattable
    {
        /// <summary>
        /// Silo 的唯一标识（ip:port:epoch），主要由成员资格协议使用。
        /// </summary>
        [Id(0)]
        public SiloAddress SiloAddress { get; set; } = default!;

        /// <summary>
        /// Silo 状态，由成员资格协议管理。
        /// </summary>
        [Id(1)]
        public SiloStatus Status { get; set; }

        /// <summary>
        /// 怀疑此 Silo 的 Silo 列表，由成员资格协议管理。
        /// </summary>
        [Id(2)]
        public List<Tuple<SiloAddress, DateTime>>? SuspectTimes { get; set; }

        /// <summary>
        /// Silo 面向客户端的 TCP 端口，在 Silo 启动时设置。
        /// </summary>    
        [Id(3)]
        public int ProxyPort { get; set; }

        /// <summary>
        /// Silo 的 DNS 主机名，等于 Dns.GetHostName() 的返回值，在 Silo 启动时设置。
        /// </summary>
        [Id(4)]
        public string HostName { get; set; } = default!;

        /// <summary>
        /// 集群内此特定 Silo 实例的名称。
        /// 若运行于 Azure，则为该角色实例的名称。在 Silo 启动时设置。
        /// </summary>
        [Id(5)]
        public string SiloName { get; set; } = default!;

        /// <summary>
        /// 获取或设置与 Silo 关联的角色名称。
        /// </summary>
        [Id(6)]
        public string? RoleName { get; set; } // 可选，仅适用于 Azure 角色。

        /// <summary>
        /// 获取或设置 Silo 的更新域。
        /// </summary>
        [Id(7)]
        public int UpdateZone { get; set; }  // 可选，仅适用于 Azure 角色。

        /// <summary>
        /// 获取或设置 Silo 的故障域。
        /// </summary>
        [Id(8)]
        public int FaultZone { get; set; }   // 可选，仅适用于 Azure 角色。

        /// <summary>
        /// 此 Silo 的启动时间，仅用于诊断与故障排查。
        /// </summary>
        [Id(9)]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 此 Silo 最后一次报告存活的时间，仅用于诊断与故障排查。
        /// </summary>
        [Id(10)]
        public DateTime IAmAliveTime { get; set; }

        internal DateTime EffectiveIAmAliveTime
        {
            get
            {
                var startTimeUtc = DateTime.SpecifyKind(StartTime, DateTimeKind.Utc);
                var iAmAliveTimeUtc = DateTime.SpecifyKind(IAmAliveTime, DateTimeKind.Utc);
                return startTimeUtc > iAmAliveTimeUtc ? startTimeUtc : iAmAliveTimeUtc;
            }
        }

        /// <summary>
        /// 获取此条目最近一次已知的更新时间，即以下两者中的较晚者：
        /// <see cref="EffectiveIAmAliveTime"/> 与最近一次某个 Silo 投票怀疑此
        /// Silo 的时间（参见 <see cref="SuspectTimes"/>）。宣告 Silo 死亡会记录一票怀疑票，因此此值
        /// 即使在 Silo 很早之前就停止报告存活的情况下，也能反映近期的死亡宣告。
        /// 清理成员表时，使用此值保留最近更新的失效条目，
        /// 从而使近期被宣告死亡的 Silo 仍可在成员资格快照中看到。
        /// </summary>
        internal DateTime EffectiveUpdateTime
        {
            get
            {
                var result = EffectiveIAmAliveTime;
                if (SuspectTimes is { } suspectTimes)
                {
                    foreach (var vote in suspectTimes)
                    {
                        var voteTimeUtc = DateTime.SpecifyKind(vote.Item2, DateTimeKind.Utc);
                        if (voteTimeUtc > result)
                        {
                            result = voteTimeUtc;
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 添加或更新某个 Silo 宣告此 Silo 不可用的投票。
        /// </summary>
        /// <param name="localSilo">投票的 Silo。</param>
        /// <param name="voteTime">投票时间。</param>
        /// <param name="maxVotes">最多保留的投票数。</param>
        public void AddOrUpdateSuspector(SiloAddress localSilo, DateTime voteTime, int maxVotes)
        {
            var allVotes = SuspectTimes ??= new List<Tuple<SiloAddress, DateTime>>();

            // 查找投票位置：
            // 如果我之前投过票，则更新自己的投票；
            // 否则，如果列表尚未满，直接添加新投票；
            // 否则覆盖最早的条目。
            int indexToWrite = allVotes.FindIndex(voter => localSilo.Equals(voter.Item1));
            if (indexToWrite == -1)
            {
                // 我的投票尚未记录。如果列表已满，找到最早的投票并覆盖它，
                if (allVotes.Count >= maxVotes) // 条件是列表已满。
                {
                    // 列表已满，因此选择最早的值覆盖。
                    DateTime minVoteTime = allVotes.Min(voter => voter.Item2);

                    // 仅当本地时间大于当前最早的投票时间时，才覆盖现有投票。
                    if (voteTime >= minVoteTime)
                    {
                        indexToWrite = allVotes.FindIndex(voter => voter.Item2.Equals(minVoteTime));
                    }
                }
            }

            if (indexToWrite == -1)
            {
                AddSuspector(localSilo, voteTime);
            }
            else
            {
                var newEntry = new Tuple<SiloAddress, DateTime>(localSilo, voteTime);
                SuspectTimes[indexToWrite] = newEntry;
            }
        }

        /// <summary>
        /// 添加某个 Silo 宣告此 Silo 不可用的投票。
        /// </summary>
        /// <param name="suspectingSilo">投票的 Silo。</param>
        /// <param name="suspectingTime">投票时间。</param>
        public void AddSuspector(SiloAddress suspectingSilo, DateTime suspectingTime)
            => (SuspectTimes ??= new()).Add(Tuple.Create(suspectingSilo, suspectingTime));

        internal MembershipEntry Copy()
        {
            var copy = new MembershipEntry
            {
                SiloAddress = SiloAddress,
                Status = Status,
                SuspectTimes = SuspectTimes is null ? null : new(SuspectTimes),
                ProxyPort = ProxyPort,
                HostName = HostName,
                SiloName = SiloName,
                RoleName = RoleName,
                UpdateZone = UpdateZone,
                FaultZone = FaultZone,
                StartTime = StartTime,
                IAmAliveTime = IAmAliveTime
            };

            return copy;
        }

        internal MembershipEntry WithStatus(SiloStatus status)
        {
            var updated = this.Copy();
            updated.Status = status;
            return updated;
        }

        internal MembershipEntry WithIAmAliveTime(DateTime iAmAliveTime)
        {
            var updated = this.Copy();
            updated.IAmAliveTime = iAmAliveTime;
            return updated;
        }

        internal ImmutableList<Tuple<SiloAddress, DateTime>> GetFreshVotes(DateTime now, TimeSpan expiration)
        {
            if (this.SuspectTimes == null)
                return ImmutableList<Tuple<SiloAddress, DateTime>>.Empty;

            var result = ImmutableList.CreateBuilder<Tuple<SiloAddress, DateTime>>();

            // 在怀疑投票时间集合及本地时间中找到最新时间。
            // 这可防止本地时钟偏差导致对未过期投票的计数结果不同。
            var recencyWindowEndTime = Max(now, SuspectTimes);
            foreach (var voter in this.SuspectTimes)
            {
                // 如果 now 小于 otherVoterTime，则认为 otherVoterTime 对应的投票尚未过期。
                // 当时钟未同步且其他投票方的时钟领先于本地时钟时，可能发生这种情况。
                var suspectTime = voter.Item2;
                if (recencyWindowEndTime.Subtract(suspectTime) < expiration)
                {
                    result.Add(voter);
                }
            }

            return result.ToImmutable();

            static DateTime Max(DateTime localTime, List<Tuple<SiloAddress, DateTime>> suspectTimes)
            {
                var maxValue = localTime;
                foreach (var entry in suspectTimes)
                {
                    var suspectTime = entry.Item2;
                    if (suspectTime > maxValue) maxValue = suspectTime;
                }

                return maxValue;
            }
        }

        /// <inheritdoc />
        public override string ToString() => $"SiloAddress={SiloAddress} SiloName={SiloName} Status={Status}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"SiloAddress={SiloAddress} SiloName={SiloName} Status={Status}", out charsWritten);

        /// <summary>
        /// 返回成员条目的详细字符串表示形式。
        /// </summary>
        /// <returns>成员条目的详细字符串表示形式。</returns>
        public string ToFullString()
        {
            var suspecters = SuspectTimes == null ? null : Utils.EnumerableToString(SuspectTimes.Select(tuple => tuple.Item1));
            var suspectTimes = SuspectTimes == null ? null : Utils.EnumerableToString(SuspectTimes.Select(tuple => LogFormatter.PrintDate(tuple.Item2)));

            return @$"[SiloAddress={SiloAddress} SiloName={SiloName} Status={Status} HostName={HostName} ProxyPort={ProxyPort} RoleName={RoleName} UpdateZone={UpdateZone} FaultZone={FaultZone} StartTime={LogFormatter.PrintDate(StartTime)} IAmAliveTime={LogFormatter.PrintDate(IAmAliveTime)}{(suspecters == null ? null : " Suspecters=")}{suspecters}{(suspectTimes == null ? null : " SuspectTimes=")}{suspectTimes}]";
        }
    }
}
