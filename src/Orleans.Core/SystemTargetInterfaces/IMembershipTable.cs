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
    /// 成员界面表
    /// </summary>
    /// <remarks>
    /// 供应商可以采用取消通知的自动合成方法,取消其基本业务。
    /// 默认执行取消呼叫者等待, 而无一遗漏的操作完成时,
    /// 并遵守该项行动的任何例外。
    /// 承认取消RPC方法保留了现有的操作假名和序列应用论证。
    /// 取消与这些论点分开宣传。
    /// </remarks>
    public interface IMembershipTable
    {
        /// <summary>
        /// 将先要求采用其他所有方法,然后才请成员表格的初始化
        /// </summary>
        /// <param name="tryInitTableVersion">是否试图将基础表格入内</param>
        /// <returns>一项代表初始化操作的任务。</returns>
        [Obsolete("Use InitializeMembershipTableAsync instead.")]
        Task InitializeMembershipTable(bool tryInitTableVersion);

        /// <inheritdoc cref="InitializeMembershipTable(bool)"/>
        /// <param name="tryInitTableVersion">是否初始化基础表格版本。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Alias("FB89E5E9")]
        async Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = InitializeMembershipTable(tryInitTableVersion);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 删除给定 clusterId的所有表格条目
        /// </summary>
        /// <param name="clusterId">条目被删除的组群标识符。</param>
        /// <returns>代表删除操作的任务。</returns>
        [Obsolete("Use DeleteMembershipTableEntriesAsync instead.")]
        Task DeleteMembershipTableEntries(string clusterId);

        /// <inheritdoc cref="DeleteMembershipTableEntries(string)"/>
        /// <param name="clusterId">条目被删除的组。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Alias("BF899C85")]
        async Task DeleteMembershipTableEntriesAsync(string clusterId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = DeleteMembershipTableEntries(clusterId);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 删除已死 silo 其上次已知更新先于此条目的条目 <paramref name="beforeDate"/>.
        /// </summary>
        /// <param name="beforeDate">条目最后一次更新时要删除的独家上下限。</param>
        /// <returns>一项代表清理行动的任务。</returns>
        [Obsolete("Use CleanupDefunctSiloEntriesAsync instead.")]
        Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate);

        /// <inheritdoc cref="CleanupDefunctSiloEntries(DateTimeOffset)"/>
        /// <param name="beforeDate">条目最后一次更新时要删除的独家上下限。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Alias("7A519C2E")]
        async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = CleanupDefunctSiloEntries(beforeDate);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 通过供应商的遗留行读取业务阅读会员信息。
        /// </summary>
        /// <remarks>
        /// 使用 <see cref="ReadAllAsync"/> 获取原子快照
        /// <see cref="MembershipTableData.TryGet"/> 选择 silo 和实体标签。
        /// 内置供应商报告 <see cref="NotSupportedException"/> 将行读取。
        /// </remarks>
        /// <param name="key">大会主席的地址 silo 需要阅读其成员资格信息。</param>
        /// <returns>给定 silo: MembershipTableData 的成员资格信息,包括一个 MembershipEntry 条目和
        /// TableVersion,从原子上读取。</returns>
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        Task<MembershipTableData> ReadRow(SiloAddress key);

        /// <inheritdoc cref="ReadRow(SiloAddress)"/>
        /// <param name="key">大会主席的地址 silo 改为。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Obsolete("Use ReadAllAsync and MembershipTableData.TryGet instead.")]
        [Alias("D851FB33")]
        async Task<MembershipTableData> ReadRowAsync(SiloAddress key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = ReadRow(key);
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 原子读取成员表的全部内容。
        /// 返回的 MembershipTableData 包括所有 MembershipEntry 全部条目 silos 表中和表格中的 
        /// 本表格的TableVersion 。 MembershipEntries 和 TableVersion 必须从原子角度阅读。
        /// </summary>
        /// <returns>某一表格的成员信息: MembershipTableData ,包括多个 MembershipEntry 条目和
        /// TableVersion全部从原子上读取</returns>
        [Obsolete("Use ReadAllAsync instead.")]
        Task<MembershipTableData> ReadAll();

        /// <inheritdoc cref="ReadAll()"/>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Alias("00BCE16F")]
        async Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = ReadAll();
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 原子试图插入(添加)一个新的 MembershipEntry ,一个 silo ,并更新 TableVersion。
        /// 如果操作成功,将对表格作如下修改:
        /// (1) 表格中将增加新的 MembershipEntry 。
        /// 2) 新增的 MembershipEntry 将有一个由供应商定义的行 eTag。
        /// 3) TableVersion.Version 表格中的表格将更新到新的 TableVersion.Version.
        /// 4) TableVersion 表中的牌将更新到新的独有自动生成 eTag。
        /// 所有对表格的修改,插入新行和更新表格版本及相关的标签,应原生化或原子衰落,无副作用。
        /// 操作在下列每一条件下均应失败:
        /// 1) A MembershipEntry 给定 silo 表格中已经存在
        /// (2) TableVersion 更新失败,因为给定 TableVersion etag(按 TableVersion.VersionEtag 属性指定)与表中的 TableVersion etag不匹配。
        /// </summary>
        /// <param name="entry">MembershipEntry 插入。</param>
        /// <param name="tableVersion">本表格的新 TableVersion 及其表单。</param>
        /// <returns>如果插入操作成功且以其他方式虚假, 则正确。</returns>
        [Obsolete("Use InsertRowAsync instead.")]
        Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion);

        /// <inheritdoc cref="InsertRow(MembershipEntry, TableVersion)"/>
        /// <param name="entry">插入成员条目。</param>
        /// <param name="tableVersion">新表格版本及其预期的etag。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Alias("FEF3AC5A")]
        async Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = InsertRow(entry, tableVersion);
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 原子试图更新 MembershipEntry 一对一 silo 更新 TableVersion.
        /// 如果操作成功,将对表格作如下修改:
        /// (1) 与 MembershipEntry 为此 silo 将使用所提供的条目更新。
        /// (2) 横 eTag 更新的《公约》和《公约》 MembershipEntry 将反映供应商的元数据或货币汇率政策。
        /// 3) TableVersion.Version 表格中的表格将更新到新的 TableVersion.Version.
        /// 4) TableVersion 表中的牌将更新到新的独有自动生成 eTag。
        /// 所有对表格的修改、新行更新以及表格版本和相关星格的更新,都应原生化地进行,否则原子衰落,无副作用。
        /// 操作在下列每一条件下均应失败:
        /// 1) A MembershipEntry 给定 silo 表格中不存在
        /// (2) TableVersion 更新失败,因为给定 TableVersion etag(按 TableVersion.VersionEtag 属性指定)与表中的 TableVersion etag不匹配。
        /// (3) 供应商确认另外一种金币行同价证券,而所提供的同价证券与该货币牌不符。
        /// </summary>
        /// <remarks>
        /// 原生供应商要求现有一行并验证 <see cref="TableVersion.VersionEtag"/>。
        /// 提供者可以使用行 eTag 作为额外的心心中中性同音符号,或者当
        /// 表格-ETag 验证保护现有行更新。
        /// 保持对先前读取的行和表格输入的验证
        /// 时行存在,表格ETag仍然保持当前状态。
        /// </remarks>
        /// <param name="entry">MembershipEntry 待更新。</param>
        /// <param name="etag">从成员处获得的由供应商定义的行实体标记读取。</param>
        /// <param name="tableVersion">本表格的新 TableVersion 及其表单。</param>
        /// <returns>如果更新操作成功且以其他方式虚假, 则正确。</returns>
        [Obsolete("Use UpdateRowAsync instead.")]
        Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion);

        /// <inheritdoc cref="UpdateRow(MembershipEntry, string, TableVersion)"/>
        /// <param name="entry">更新成员条目。</param>
        /// <param name="etag">从成员处获得的由供应商定义的行实体标记读取。</param>
        /// <param name="tableVersion">新表格版本及其预期的etag。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Alias("E06D3DBC")]
        async Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = UpdateRow(entry, etag, tableVersion);
#pragma warning restore CS0618
            operation.Ignore();
            return await operation.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 更新 IAmAliveTime 列一 MembershipEntry 为此 silo.
        /// </summary>
        /// <remarks>
        /// 每个 silo 都有心跳,并将所提供的时间戳直接写到其 IAmAliveTime 列。
        /// 保留其他会员字段和表格版本,包括其ETag。
        /// 返回的行ETag 可能反映后端管理的元数据,并随心跳变化。
        /// 虽然行存在,表ETag仍然时尚,但只心跳活动在先前
        /// 读取 <see cref="UpdateRowAsync"/>所使用的行和表格输入。
        /// </remarks>
        /// <param name="entry">载有增订的 <see cref="MembershipEntry.IAmAliveTime"/> 值。</param>
        /// <returns>代表更新操作的任务。</returns>
        [Obsolete("Use UpdateIAmAliveAsync instead.")]
        Task UpdateIAmAlive(MembershipEntry entry);

        /// <inheritdoc cref="UpdateIAmAlive(MembershipEntry)"/>
        /// <param name="entry">包含更新活度时间戳的会员条目。</param>
        /// <param name="cancellationToken">取消操作的标记。</param>
        [Alias("B1A52D2B")]
        async Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility dispatch to existing tokenless providers.
            var operation = UpdateIAmAlive(entry);
#pragma warning restore CS0618
            operation.Ignore();
            await operation.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 系统目标实施成员表接口。
    /// </summary>
    public interface IMembershipTableSystemTarget : IMembershipTable, ISystemTarget
    {
    }

    /// <summary>
    /// 表示成员表的版本和用于乐观货币的实体标记。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class TableVersion : ISpanFormattable, IEquatable<TableVersion>
    {
        /// <summary>
        /// 此 TableVersion的版本部分。 单倍增长的数量 。
        /// </summary>
        [Id(0)]
        public int Version { get; }

        /// <summary>
        /// 用于验证表格更新操作的 TableVersion。
        /// </summary>
        [Id(1)]
        public string VersionEtag { get; }

        /// <summary>
        /// 初始化 <see cref="TableVersion"/> 类的新实例。
        /// </summary>
        /// <param name="version">成员表版本。</param>
        /// <param name="eTag">用于验证本版本更新的实体标记。</param>
        public TableVersion(int version, string eTag)
        {
            Version = version;
            VersionEtag = eTag;
        }

        /// <summary>
        /// 创建下一个会籍表版本,同时保留当前实体标签以进行同值货币验证。
        /// </summary>
        /// <returns>下一份成员表版本。</returns>
        /// <exception cref="OverflowException">成员表的版本已达到 <see cref="int.MaxValue"/>.</exception>
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
        /// 确定两个成员表格版本是否相等。
        /// </summary>
        /// <param name="left">比较第一个成员表版本。</param>
        /// <param name="right">比较第二个成员表版本。</param>
        /// <returns><see langword="true"/> ,如果版本和实体标签相等;否则, <see langword="false"/>。</returns>
        public static bool operator ==(TableVersion? left, TableVersion? right) => EqualityComparer<TableVersion>.Default.Equals(left, right);

        /// <summary>
        /// 确定两个成员表格版本是否相等。
        /// </summary>
        /// <param name="left">比较第一个成员表版本。</param>
        /// <param name="right">比较第二个成员表版本。</param>
        /// <returns><see langword="true"/> ,如果版本或实体标签不同;否则, <see langword="false"/>。</returns>
        public static bool operator !=(TableVersion? left, TableVersion? right) => !(left == right);
    }

    /// <summary>
    /// 代表成员条目和成员表版本的原子简况。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class MembershipTableData
    {
        /// <summary>
        /// 获取会员条目及其供应商定义的行实体标签。
        /// </summary>
        [Id(0)]
        public IReadOnlyList<Tuple<MembershipEntry, string>> Members { get; private set; }

        /// <summary>
        /// 获取会员表快照版本。
        /// </summary>
        [Id(1)]
        public TableVersion Version { get; private set; }

        /// <summary>
        /// 初始化多个会籍行的 <see cref="MembershipTableData"/> 类新实例。
        /// </summary>
        /// <param name="list">成员条目及其实体标签。</param>
        /// <param name="version">成员表简况的版本。</param>
        public MembershipTableData(List<Tuple<MembershipEntry, string>> list, TableVersion version)
        {
            // 死在尽头,只是用来伐木
            list.Sort(
               (x, y) =>
               {
                   if (x.Item1.Status == SiloStatus.Dead) return 1; // 死尸在最后
                   if (y.Item1.Status == SiloStatus.Dead) return -1; // 死尸在最后
                   return string.CompareOrdinal(x.Item1.SiloName, y.Item1.SiloName);
               });
            Members = list;
            Version = version;
        }

        /// <summary>
        /// 从一排成员开始启用一个新的 <see cref="MembershipTableData"/> 等级实例。
        /// </summary>
        /// <param name="tuple">成员入境及其实体标签。</param>
        /// <param name="version">成员表简况的版本。</param>
        public MembershipTableData(Tuple<MembershipEntry, string> tuple, TableVersion version)
        {
            Members = new[] { tuple };
            Version = version;
        }

        /// <summary>
        /// 初始化 <see cref="MembershipTableData"/> 类的空实例。
        /// </summary>
        /// <param name="version">成员表简况的版本。</param>
        public MembershipTableData(TableVersion version)
        {
            Members = Array.Empty<Tuple<MembershipEntry, string>>();
            Version = version;
        }

        /// <summary>
        /// 查找指定 silo的会籍行。
        /// </summary>
        /// <param name="silo">查找的 silo 地址。</param>
        /// <returns>成员入境和实体标签,或 <see langword="null"/> 如果 silo 不存在。</returns>
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
        /// 创建成员表快照,只保留每个终点的最新死亡 silo 一代。
        /// </summary>
        /// <returns>
        /// 一份成员表快照,其中列出所有非死条目和每个终点的最新死亡条目。
        /// </returns>
        public MembershipTableData WithoutDuplicateDeads()
        {
            var dead = new Dictionary<IPEndPoint, Tuple<MembershipEntry, string>>();
            // 仅选择每个实例的最新死亡
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
                    // 之后就死了 已经死了
                    if (next.Item1.SiloAddress.Generation.CompareTo(prev.Item1.SiloAddress.Generation) > 0)
                        dead[ipEndPoint] = next;
                }
            }

            //现在将不死后添加到后面
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
    /// 代表 silo的会员记录。
    /// </summary>
    [GenerateSerializer]
    [Serializable]
    public sealed class MembershipEntry : ISpanFormattable
    {
        /// <summary>
        /// silo 独特身份(ip:port:epo:och),主要用于《成员议定书》。
        /// </summary>
        [Id(0)]
        public SiloAddress SiloAddress { get; set; } = default!;

        /// <summary>
        /// silo 状态。 由成员议定书管理。
        /// </summary>
        [Id(1)]
        public SiloStatus Status { get; set; }

        /// <summary>
        /// 列表列表 silos 怀疑的 silo由《成员议定书》管理。
        /// </summary>
        [Id(2)]
        public List<Tuple<SiloAddress, DateTime>>? SuspectTimes { get; set; }

        /// <summary>
        /// Silo 至客户端 TCP 端口。 设置在 silo 启动上 。
        /// </summary>    
        [Id(3)]
        public int ProxyPort { get; set; }

        /// <summary>
        /// DNS silo主机名。 等于 Dns.GetHostName()。 设置在 silo 启动时 。
        /// </summary>
        [Id(4)]
        public string HostName { get; set; } = default!;

        /// <summary>
        /// 组合体中具体 silo 实例的名称。 
        /// 如果在 Azure 运行 - 此角色实例的名称。 设置为 silo 启动 。
        /// </summary>
        [Id(5)]
        public string SiloName { get; set; } = default!;

        /// <summary>
        /// 获取或设置与 silo相关的角色名称。
        /// </summary>
        [Id(6)]
        public string? RoleName { get; set; } // 仅允许 - 只允许 Azure 角色

        /// <summary>
        /// 获取或设置 silo更新区域。
        /// </summary>
        [Id(7)]
        public int UpdateZone { get; set; }  // 仅允许 - 只允许 Azure 角色

        /// <summary>
        /// 获取或设置 silo的断层区。
        /// </summary>
        [Id(8)]
        public int FaultZone { get; set; }   // 仅允许 - 只允许 Azure 角色

        /// <summary>
        /// 开始此 silo 时间。 只用于诊断和排除故障 。
        /// </summary>
        [Id(9)]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 最后一次 silo 报告它还活着。只用于诊断和排除故障。
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
        /// 获取已知此条目更新的最新时间。 这是最近
        /// <see cref="EffectiveIAmAliveTime"/> 和最近的时间 silo 投票怀疑此事
        /// silo (见 <see cref="SuspectTimes"/>). 宣布一个 silo 死亡记录了可疑投票,所以这个数值
        /// 最近一次死亡申报表明,即使上次的 silo 报告是很久以前就活着的。
        /// 用于在清理会籍表时保留最近期更新的已过期条目,所以
        /// 成员照片中仍然可以看到最近宣布死亡的 silos 。
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
        /// 添加或更新 silo的投票,宣布该 silo 无法使用。
        /// </summary>
        /// <param name="localSilo">下 silo 进行表决。</param>
        /// <param name="voteTime">投票时间。</param>
        /// <param name="maxVotes">保留票的上限。</param>
        public void AddOrUpdateSuspector(SiloAddress localSilo, DateTime voteTime, int maxVotes)
        {
            var allVotes = SuspectTimes ??= new List<Tuple<SiloAddress, DateTime>>();

            // 查找投票地点:
            //      投票之前如果我投票,我投票将更新我的投票结果
            //      或如果名单不满 -- -- 仅增加一票
            //      或覆盖最古老的条目。
            int indexToWrite = allVotes.FindIndex(voter => localSilo.Equals(voter.Item1));
            if (indexToWrite == -1)
            {
                // 找到最过时的选票 如果名单满,并覆盖
                if (allVotes.Count >= maxVotes) // 如果名单是完整
                {
                    // 列表已满, 请选择最过时的值来覆盖 。
                    DateTime minVoteTime = allVotes.Min(voter => voter.Item2);

                    // 只有在地方时间超过目前最低投票时间时,才能覆盖现有投票。
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
        /// 添加 silo的投票,宣布此 silo 不可用。
        /// </summary>
        /// <param name="suspectingSilo">下 silo 进行表决。</param>
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

            // 找到从疑犯时间和当地时间中 找到最新的时间
            // 这使得当地时钟无法造成不同的新票计数。
            var recencyWindowEndTime = Max(now, SuspectTimes);
            foreach (var voter in this.SuspectTimes)
            {
                // 如果现在小于 otherVoterTime,则比假设 otherVoterTime 新鲜。
                // 如果时钟不同步,而其他选民的时钟比我提前,这种情况就可能发生。
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
        /// 返回成员条目的详细字符串代表。
        /// </summary>
        /// <returns>成员入会的详细字符串代表。</returns>
        public string ToFullString()
        {
            var suspecters = SuspectTimes == null ? null : Utils.EnumerableToString(SuspectTimes.Select(tuple => tuple.Item1));
            var suspectTimes = SuspectTimes == null ? null : Utils.EnumerableToString(SuspectTimes.Select(tuple => LogFormatter.PrintDate(tuple.Item2)));

            return @$"[SiloAddress={SiloAddress} SiloName={SiloName} Status={Status} HostName={HostName} ProxyPort={ProxyPort} RoleName={RoleName} UpdateZone={UpdateZone} FaultZone={FaultZone} StartTime={LogFormatter.PrintDate(StartTime)} IAmAliveTime={LogFormatter.PrintDate(IAmAliveTime)}{(suspecters == null ? null : " Suspecters=")}{suspecters}{(suspectTimes == null ? null : " SuspectTimes=")}{suspectTimes}]";
        }
    }
}
